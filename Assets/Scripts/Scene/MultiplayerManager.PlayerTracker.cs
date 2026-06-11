using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using Core;
using System.Collections;

namespace Multiplayer{
    public partial class MultiplayerManager
    {
        [Header("Player Management")]
        public NetworkList<PlayerNetworkData> PlayerDataList { get; private set; }
        private List<IPlayer> _activePlayers = new();
        public List<IPlayer> AllPlayers => _activePlayers;
        public IPlayer LocalPlayer { get; private set; }

        private List<ulong> _participants = new();
        private HashSet<ulong> _readyPlayers = new();
        private HashSet<ulong> _finishedPlayers = new();
        private Dictionary<ulong, float> _playerFinishTimes = new();

        [Header("HUD Sync")]
        private NetworkVariable<int> _netAliveCount = new(0);
        private NetworkVariable<int> _netTotalParticipants = new(0);
        private NetworkVariable<int> _netReadyCount = new(0);

        [Header("Respawn Settings")]
        [SerializeField] private float _respawnDelay = 3f;
        private Dictionary<ulong, float> _playerRespawnTimers = new();

        private void OnPlayerJoinedHandler(IPlayer player)
        {
            if (!_activePlayers.Contains(player))
            {
                _activePlayers.Add(player);
                if (IsServer) SubscribeToPlayerEvents(player);
            }
        }

        private void OnPlayerLeftHandler(IPlayer player)
        {
            _activePlayers.Remove(player);
            if (IsServer) CheckRoundCompletion();
        }

        private void OnLocalPlayerSpawnedHandler(IPlayer localPlayer)
        {
            LocalPlayer = localPlayer;
            localPlayer.IsAFK.Value = true; // Mặc định vào phòng là AFK (Host có thể tự động chuyển sang Active)

            // Đăng ký listener để UI tự động cập nhật khi LocalPlayer thay đổi trạng thái
            localPlayer.IsAFK.OnValueChanged += (oldVal, newVal) => _uiManager?.UpdatePlayStatus(newVal);
            localPlayer.Status.OnValueChanged += (oldVal, newVal) => {
                _uiManager?.UpdateSpectateStatus(newVal == PlayerStatus.Spectating);
                _uiManager?.SetHUDMode(newVal == PlayerStatus.InGame || newVal == PlayerStatus.Finished);
            };

            // Cập nhật UI ngay lập tức
            _uiManager?.UpdatePlayStatus(localPlayer.IsAFK.Value);
            _uiManager?.UpdateSpectateStatus(localPlayer.Status.Value == PlayerStatus.Spectating);
            _uiManager?.SetHUDMode(localPlayer.Status.Value == PlayerStatus.InGame);
            
            // Đồng bộ trạng thái nút Vote ngay khi spawn (cho trường hợp join giữa chừng phase Voting)
            _uiManager?.SetVotingButtonVisible(CurrentState.Value == GameState.Voting);

            StartCoroutine(SetupLocalPlayerRoutine(localPlayer));
        }

        /// <summary>
        /// Server RPC để đăng ký thông tin người chơi mới vào PlayerDataList đồng bộ.
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="playerName"></param>
        /// <param name="isHost"></param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RegisterPlayerServerRpc(ulong clientId, string playerName, bool isHost)
        {
            for (int i = 0; i < PlayerDataList.Count; i++)
            {
                if (PlayerDataList[i].ClientId == clientId) return;
            }

            PlayerDataList.Add(new PlayerNetworkData
            {
                ClientId = clientId,
                PlayerName = playerName,
                IsHost = isHost,
                IsAFK = true
            });
        }

        private void OnPlayerDataListChanged(NetworkListEvent<PlayerNetworkData> changeEvent)
        {
            if (IsServer && LANDiscovery.Instance != null)
            {
                LANDiscovery.Instance.UpdateBroadcastData(PlayerDataList.Count);
            }
        }

        private void OnClientDisconnectedFromServer(ulong clientId)
        {
            if (IsServer)
            { // Server side cleanup
                for (int i = 0; i < PlayerDataList.Count; i++)
                {
                    if (PlayerDataList[i].ClientId == clientId)
                    {
                        PlayerDataList.RemoveAt(i);
                        break;
                    }
                }
                // Remove from active players list
                _activePlayers.RemoveAll(p => p is NetworkBehaviour nb && nb.OwnerClientId == clientId);
            }

            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                StopBackgroundMusic();
            }
        }

        /// <summary>
        /// Đăng ký lắng nghe các sự kiện quan trọng của Player như AFK, vào Lobby, chết, 
        /// để Server có thể cập nhật trạng thái và đồng bộ UI chính xác cho tất cả mọi người.
        /// </summary>
        /// <param name="player"></param>
        private void SubscribeToPlayerEvents(IPlayer player)
        {
            if (!IsServer) return;
            
            // Đăng ký lắng nghe các thay đổi trạng thái và truyền chính xác Player vào callback
            player.IsAFK.OnValueChanged += (oldVal, newVal) => OnPlayerAFKStatusChanged(player, oldVal, newVal);
            player.Status.OnValueChanged += (oldVal, newVal) => OnPlayerStatusChangedServer(player, oldVal, newVal);
            player.NetworkIsDead.OnValueChanged += (oldVal, newVal) => OnPlayerDeathStatusChangedServer(player, oldVal, newVal);
            
            // Cập nhật PlayerDataList ngay khi có người mới vào (để UI RoomInfo thấy luôn)
            // SyncPlayerToDataList(player); // This is handled by RegisterPlayerServerRpc for new players

            if (CurrentState.Value == GameState.Intermission)
            {
                CheckAutoStart();
            }
        }

        private void OnPlayerAFKStatusChanged(IPlayer player, bool oldVal, bool newVal)
        {
            if (!IsServer) return;
            
            // Cập nhật PlayerDataList để đồng bộ UI RoomInfo cho tất cả mọi người
            SyncPlayerToDataList(player);

            // Kiểm tra điều kiện bắt đầu Voting bất cứ khi nào trạng thái AFK thay đổi (FIX Bug 4: Only if spawned)
            CheckAutoStart();
        }

        private void OnPlayerStatusChangedServer(IPlayer player, PlayerStatus oldVal, PlayerStatus newVal)
        {
            if (!IsServer || player is not NetworkBehaviour nb) return;
            ulong clientId = nb.OwnerClientId;

            // Nếu player về đích (Finished) trong lúc đang chơi, Server ghi nhận kết quả
            if (newVal == PlayerStatus.Finished && CurrentState.Value == GameState.Playing)
            {
                if (!_finishedPlayers.Contains(clientId))
                {
                    _finishedPlayers.Add(clientId);
                    if (!_playerFinishTimes.ContainsKey(clientId))
                    {
                        _playerFinishTimes[clientId] = IsMapMechanicsStartedNet.Value ? NetworkTime.Value : 0f;
                    }
                    Debug.Log($"[Server] Player {clientId} finished at {NetworkTime.Value}s");
                }
            }

            if (newVal == PlayerStatus.Lobby || newVal == PlayerStatus.Finished) 
                CheckRoundCompletion();
        }

        private void OnPlayerDeathStatusChangedServer(IPlayer player, bool oldVal, bool newVal)
        {
            // Nếu Player chuyển sang trạng thái chết trong lúc đang chơi, kiểm tra xem round có kết thúc không
            if (IsServer && newVal && CurrentState.Value == GameState.Playing) 
                CheckRoundCompletion();
        }

        /// <summary>
        /// Đồng bộ trạng thái AFK của player vào PlayerDataList để cập nhật UI RoomInfo cho tất cả mọi người.
        /// </summary>
        /// <param name="player"></param>
        private void SyncPlayerToDataList(IPlayer player)
        {
            if (!IsServer || player == null || !(player is NetworkBehaviour nb)) return;
            if (PlayerDataList == null) return;

            ulong clientId = nb.OwnerClientId;
            for (int i = 0; i < PlayerDataList.Count; i++)
            {
                if (PlayerDataList[i].ClientId == clientId)
                {
                    var data = PlayerDataList[i];
                    if (data.IsAFK != player.IsAFK.Value)
                    {
                        data.IsAFK = player.IsAFK.Value;
                        PlayerDataList[i] = data;
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Quản lý thời gian hồi sinh của người chơi. Khi một player chết, họ sẽ được thêm vào _playerRespawnTimers với thời gian đếm ngược.
        /// </summary>
        private void HandleRespawnTimers()
        {
            if (_playerRespawnTimers.Count == 0) return;

            var clientIds = _playerRespawnTimers.Keys.ToList();
            
            foreach (var clientId in clientIds)
            {
                _playerRespawnTimers[clientId] -= Time.deltaTime;

                if (_playerRespawnTimers[clientId] <= 0f)
                {
                    // Gửi lệnh hồi sinh cho Client cụ thể
                    RespawnPlayerClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
                    _playerRespawnTimers.Remove(clientId);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void OnPlayerDeadServerRpc(ulong clientId)
        {
            if (!_playerRespawnTimers.ContainsKey(clientId))
            {
                _playerRespawnTimers[clientId] = _respawnDelay;
            }
            CheckRoundCompletion();
        }

        /// <summary>
        /// Client RPC để thực thi việc hồi sinh player sau khi thời gian đếm ngược kết thúc.
        /// </summary>
        /// <param name="rpcParams"></param>
        [Rpc(SendTo.SpecifiedInParams)]
        private void RespawnPlayerClientRpc(RpcParams rpcParams)
        {
            if (LocalPlayer != null)
            {
                // Tìm đúng điểm spawn Public (Lobby)
                if (_lobbySpawn == null) 
                    _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => !s.IsMapSpawn);

                if (_lobbySpawn != null)
                {
                    Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
                    LocalPlayer.Teleport(spawnPos);
                    LocalPlayer.Revive();
                    LocalPlayer.PrepareForNewRound();
                    LocalPlayer.SetStatus(PlayerStatus.Lobby); // Quay về Lobby
                    PlayLobbyMusic(); // Chuyển về nhạc Lobby ngay khi hồi sinh
                    CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);

                    _uiManager?.ShowPlayerFinishFlag(false);
                }
            }
        }

        /// <summary>
        /// Yêu cầu kick một player khỏi phòng. Chỉ Server mới có quyền thực hiện hành động này. 
        /// Khi được gọi, sẽ hiển thị hộp thoại xác nhận và nếu được đồng ý, client sẽ bị ngắt kết nối khỏi Server.
        /// </summary>
        /// <param name="clientId"></param>
        public void RequestKickPlayer(ulong clientId)
        {
            if (!IsServer) return;
            _uiManager?.AskConfirmation("Are you sure you want to kick this player?", () => {
                Debug.Log($"[MultiplayerManager] Kicking client {clientId}");
                NetworkManager.Singleton.DisconnectClient(clientId);
            });
        }

        /// <summary>
        /// Yêu cầu reset player (thường dùng khi AFK trong Lobby hoặc khi muốn tự nguyện hồi sinh trong map).
        /// </summary>
        public void RequestResetPlayer()
        {
            ResetPlayerServerRpc(NetworkManager.Singleton.LocalClientId);
        }

        /// <summary>
        /// Server RPC để yêu cầu reset player. Khi được gọi, Server sẽ gửi lệnh cho Client tương ứng để tự gọi hàm Die() và hồi sinh lại.
        /// </summary>
        /// <param name="clientId"></param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ResetPlayerServerRpc(ulong clientId)
        {
            if (!IsServer) return;
            Debug.Log($"[MultiplayerManager] Requesting character reset for client {clientId}");
                
            // Server yêu cầu Client tự gọi hàm Die() để đảm bảo đúng quyền Owner
            ForceDieClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>
        /// Client RPC để thực thi việc reset player. Khi được gọi, Client sẽ tự gọi hàm Die() trên Player của mình để kích hoạt quá trình hồi sinh.
        /// </summary>
        /// <param name="rpcParams"></param>
        [Rpc(SendTo.SpecifiedInParams)]
        private void ForceDieClientRpc(RpcParams rpcParams)
        {
            // Chạy trên Client sở hữu Player
            LocalPlayer?.Die(DeathReason.Reset);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SetLocalParticipantStatusClientRpc(PlayerStatus status, RpcParams rpcParams = default)
        {
            LocalPlayer?.SetStatus(status);
        }

                private void OnPlayerFinishedHandler(IPlayer player)
        {
            // [Giai đoạn Playing - Client Side]
            if (player == LocalPlayer)
            {
                _uiManager?.ShowPlayerFinishFlag(true);
                _uiManager?.ShowFloatNotification($"Completed {NetCurrentMapName.Value}!", Color.green, 2f);
                LocalPlayer?.SetInvincible(true);
            }
        }

        private void OnPlayerDiedHandler()
        {
            // 1. Client gửi yêu cầu lên Server để bắt đầu đếm ngược hồi sinh
            if (LocalPlayer is MonoBehaviour playerMono && playerMono.TryGetComponent<NetworkObject>(out var netObj))
            {
                OnPlayerDeadServerRpc(netObj.OwnerClientId);
            }
        }

        /// <summary>
        /// Quản lý quá trình setup LocalPlayer sau khi spawn: Đợi 1 frame để ổn định, 
        /// tìm điểm spawn lobby, teleport player về lobby, thiết lập camera, và chuẩn bị cho giai đoạn tiếp theo.
        /// </summary>
        /// <param name="localPlayer"></param>
        /// <returns></returns>
        private IEnumerator SetupLocalPlayerRoutine(IPlayer localPlayer)
        {
            yield return null; // Chờ 1 frame để các thành phần Network ổn định

            // Tìm đúng điểm spawn Public (Lobby)
            if (_lobbySpawn == null) _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => !s.IsMapSpawn);

            // Tìm và thiết lập Camera
            if (_vcam == null) _vcam = FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();

            if (localPlayer is MonoBehaviour playerMono)
            {
                // 1. Gán mục tiêu cho Camera TRƯỚC khi teleport để camera biết cần snap vào đâu
                if (_vcam != null)
                {
                    _vcam.Priority = 10;
                    _vcam.Follow = playerMono.transform;
                    _vcam.LookAt = playerMono.transform;
                }

                // 2. Thực hiện Teleport về Lobby
                // FIX: Nếu là người chơi join mid-game (không phải participant), 
                // chúng ta vẫn phải đưa họ về Lobby và gán camera.
                if (!_localIsRoundParticipant)
                {
                    Vector3 spawnPos = _lobbySpawn != null ? _lobbySpawn.GetRandomSpawnPosition() : Vector3.zero;
                    localPlayer.Teleport(spawnPos);
                    if (_lobbySpawn != null) localPlayer.SetFacing(_lobbySpawn.IsFacingRight);
                    
                    if (_vcam != null) CameraHelper.WarpToTarget(_vcam, playerMono);
                }
            }
            _uiManager?.ShowJoiningLoadingScreen(false);
        }
    }
}
