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
        public void SetLocalParticipant(bool isParticipant) => _localIsRoundParticipant = isParticipant;

        // Local Session Tracking
        private List<RoundSummaryData> _localSessionResults = new();
        private int _localRoundFinishCount = 0; // Đếm số người về đích trước mình
        private int _localRoundButtonsPressed = 0;
        private float _localFinishTime = -1f;

        /// <summary>
        /// Hàm gộp để lấy thời gian thực tế từ lúc bắt đầu round đến hiện tại.
        /// Luôn chính xác bất kể trạng thái NetworkTime thay đổi.
        /// </summary>
        private float GetElapsedRoundTime()
        {
            if (!IsMapMechanicsStartedNet.Value || _netStartTime.Value <= 0) return 0f;
            return (float)(NetworkManager.Singleton.ServerTime.Time - _netStartTime.Value);
        }

        private void OnLocalButtonPressed()
        {
            // FIX: Chỉ đếm nút khi Game đang trong phase Playing 
            // VÀ mechanics đã thực sự bắt đầu (đã hiện chữ GO!)
            // Điều này ngăn việc đếm nhầm các nút của map cũ chưa kịp hủy khi round mới đang setup.
            if (CurrentState.Value == GameState.Playing && IsMapMechanicsStartedNet.Value)
            {
                _localRoundButtonsPressed++;
            }
        }

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
                
                // Spectate Hooks
                if (newVal == PlayerStatus.Spectating) StartSpectating();
                else if (oldVal == PlayerStatus.Spectating && newVal != PlayerStatus.Spectating) StopSpectating();
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
            }

            if (IsServer || IsClient) {
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
                    
                    // Server tính toán Rank dựa trên số người đã có trong danh sách và gửi về cho Client đó
                    int assignedRank = _finishedPlayers.Count;
                    NotifyRankClientRpc(assignedRank, RpcTarget.Single(clientId, RpcTargetUse.Temp));

                    Debug.Log($"[Server] Player {clientId} finished at {NetworkTime.Value}s");
                }
            }

            if (newVal == PlayerStatus.Lobby || newVal == PlayerStatus.Finished) 
                CheckRoundCompletion();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void NotifyRankClientRpc(int rank, RpcParams rpcParams = default)
        {
            // Gán rank chính xác do Server cấp phát cho Local Player
            _localRoundFinishCount = rank;

            // FIX: Chỉ hiển thị thông báo và ghi nhận kết quả khi đã nhận được Rank từ Server
            _uiManager?.ShowFloatNotification($"Completed {NetCurrentMapName.Value} (#{rank})", Color.green, 2f);
            
            // Ghi lại kết quả thắng vào Session
            RecordLocalRoundResult(true);

            // Cập nhật streak vào Profile cục bộ
            if (DataManager.Instance != null)
            {
                DataManager.Instance.Profile.RegisterMultiplayerWin();
            }
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
            StartCoroutine(RespawnLocalPlayerRoutine());
        }

        private IEnumerator RespawnLocalPlayerRoutine()
        {
            if (LocalPlayer != null)
            {
                // Tìm đúng điểm spawn Public (Lobby)
                if (_lobbySpawn == null) 
                    _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => !s.IsMapSpawn);

                if (_lobbySpawn != null && _vcam != null)
                {
                    Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
                    
                    // 1. Ngắt follow hoàn toàn
                    _vcam.Follow = null;
                    
                    LocalPlayer.Teleport(spawnPos);
                    LocalPlayer.Revive();
                    LocalPlayer.PrepareForNewRound();
                    _localIsRoundParticipant = false;
                    LocalPlayer.SetStatus(PlayerStatus.Lobby);
                    PlayLobbyMusic();

                    // 2. Warp camera đến vị trí mới (Lobby 0,0)
                    CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);
                    
                    // 3. QUAN TRỌNG: Đợi đến cuối frame để Cinemachine cập nhật Transform vật lý.
                    // Điều này triệt tiêu hoàn toàn damping trễ khi nhảy khoảng cách lớn.
                    yield return new WaitForEndOfFrame();

                    // 4. Bật lại follow khi mọi thứ đã ổn định
                    _vcam.Follow = (LocalPlayer as MonoBehaviour).transform;

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
            if (player == LocalPlayer)
            {
                // "Chốt" thời gian về đích cục bộ ngay lập tức
                _localFinishTime = GetElapsedRoundTime();

                _uiManager?.ShowPlayerFinishFlag(true); // Hiển thị cờ hoàn thành
                LocalPlayer?.SetInvincible(true);
            }
        }

        private void RecordLocalRoundResult(bool isWin)
        {
            MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == NetCurrentMapName.Value.ToString());

            float elapsed = GetElapsedRoundTime();
            // Nếu thắng, ưu tiên lấy thời gian đã chốt lúc chạm đích. Nếu thua, lấy thời gian trôi qua tại thời điểm gọi Record.
            float timeToRecord = (isWin && _localFinishTime > 0) ? _localFinishTime : elapsed;

            _localSessionResults.Add(new RoundSummaryData
            {
                MapName = data != null ? data.Name : "Unknown",
                Tier = _palette != null && data != null ? _palette.GetTierFromRating(data.Difficulty) : DifficultyPalette.Tier.Easy,
                Rank = _localRoundFinishCount,
                ButtonsPressed = _localRoundButtonsPressed,
                FinishTime = timeToRecord,
                MapPreviewSprite = data != null ? data.MapPreviewImage : null,
                CoinsEarned = CalculateSessionCoins(isWin, _localRoundButtonsPressed),
                IsWin = isWin
            });
        }

        private void OnPlayerDiedHandler()
        {
            // 1. Client gửi yêu cầu lên Server để bắt đầu đếm ngược hồi sinh
            if (LocalPlayer is MonoBehaviour playerMono && playerMono.TryGetComponent<NetworkObject>(out var netObj))
            {
                OnPlayerDeadServerRpc(netObj.OwnerClientId);
            }

            // DEBUG: Kiểm tra tại sao modal không hiện
            Debug.Log($"[DeathLog] State: {CurrentState.Value}, IsParticipant: {_localIsRoundParticipant}, LocalPlayer: {LocalPlayer != null}");

            // FIX: Khi chết, nếu là người cuối cùng, State có thể đã chuyển sang Voting ngay lập tức (đặc biệt là Host)
            // nên ta cho phép hiện Summary nếu State là Playing HOẶC Voting.
            bool isPlayingOrVoting = CurrentState.Value == GameState.Playing || CurrentState.Value == GameState.Voting;

            if (isPlayingOrVoting && _localIsRoundParticipant && LocalPlayer != null)
            {
                StartCoroutine(ShowSummaryWithDelayRoutine());
            }
        }

        private IEnumerator ShowSummaryWithDelayRoutine()
        {
            // FIX: Chỉ ghi nhận kết quả Thua nếu người chơi chưa về đích ở round này.
            // Nếu đã Win (Finished), ta không cần Record thêm một lượt Lose cho cùng một map vào danh sách.
            if (LocalPlayer != null && LocalPlayer.Status.Value != PlayerStatus.Finished)
                RecordLocalRoundResult(false);
            
            // Yêu cầu 3: Delay 1s trước khi hiện SummaryModal để người chơi kịp thấy mình chết
            yield return new WaitForSeconds(1f);
          
            int winCount = _localSessionResults.Count(r => r.IsWin);
            int totalCoins = _localSessionResults.Sum(r => r.CoinsEarned);

            // Hiển thị Summary Modal (Yêu cầu 1: OnPlayerDiedHandler chỉ chạy trên Owner nên modal chỉ hiện cho local)
            _uiManager?.ShowSummary(_localSessionResults);

            // Cộng xu và reset streak trong Profile (Commit)
            if (DataManager.Instance != null)
            {
                var profile = DataManager.Instance.Profile;
                
                totalCoins = _localSessionResults.Sum(r => r.CoinsEarned);
                if (totalCoins > 0) profile.TotalCoins += totalCoins;

                // Yêu cầu 2: Reset streak tại thời điểm chết lần cuối
                profile.ResetMultiplayerStreak();
                
                DataManager.Instance.SaveData();
            }

            // Reset danh sách session để chuẩn bị cho chuỗi chơi tiếp theo sau khi hồi sinh
            _localSessionResults.Clear();
            _localRoundButtonsPressed = 0; // Đảm bảo reset sạch sẽ sau khi hiện Summary
        }

        private int CalculateSessionCoins(bool isWin, int buttons)
        {
            return (buttons * 5) + (isWin ? 20 : 0);
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

            if (localPlayer is MonoBehaviour playerMono && _vcam != null)
            {
                if (!_localIsRoundParticipant)
                {
                    _vcam.Priority = 10;
                    _vcam.Follow = null; // 1. Tạm dừng follow
                    _vcam.LookAt = playerMono.transform;

                    Vector3 spawnPos = _lobbySpawn != null ? _lobbySpawn.GetRandomSpawnPosition() : Vector3.zero;
                    localPlayer.Teleport(spawnPos);
                    if (_lobbySpawn != null) localPlayer.SetFacing(_lobbySpawn.IsFacingRight);
                    
                    // 2. Warp camera đến vị trí player ở Lobby
                    CameraHelper.WarpToTarget(_vcam, playerMono);
                    
                    yield return new WaitForEndOfFrame();
                    _vcam.Follow = playerMono.transform;
                }
            }
            _uiManager?.ShowJoiningLoadingScreen(false);
        }
    }
}
