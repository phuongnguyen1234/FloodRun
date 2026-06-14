using System.Collections;
using System.Linq;
using Core;
using Core.Interfaces;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Multiplayer
{
    public partial class MultiplayerManager
    {
        public NetworkVariable<GameState> CurrentState = new(GameState.Intermission);
        public NetworkVariable<float> NetworkTime { get; } = new NetworkVariable<float>(0f);
        private NetworkVariable<double> _netStartTime = new(0);

        public NetworkVariable<float> Difficulty { get; } = new NetworkVariable<float>(1.0f);
        public NetworkVariable<bool> IsMapMechanicsStartedNet = new(false);
        public NetworkList<FixedString64Bytes> VotingMapNames { get; private set; }
        public NetworkList<int> MapVotes { get; private set; }
        private int _roundGeneration;

        private bool _isStarting = false; // Flag đánh dấu đang trong 3s đếm ngược (trước khi Mechanics chạy)
        private bool _playingPhaseStarted; // Chặn StartPlaying / countdown chồng giữa các round
        private bool _setupTimeoutHandled; // Tránh xử lý timeout setup nhiều lần mỗi frame

        private void OnStateChanged(GameState oldState, GameState newState)
        {
            UpdateUIForState(newState);

            if (IsServer && newState == GameState.Intermission)
            {
                NetworkTime.Value = 0f;
                NetCurrentMapName.Value = ""; // Xóa thông tin map khi quay về chờ
            }

            if (IsClient)
            {
                // Chỉ hủy setup khi RỜI Playing — tránh kill SetupClientRoutine khi state chuyển Voting → Playing
                if (oldState == GameState.Playing && newState != GameState.Playing)
                {
                    DismissClientRoundSetup();
                    PlayLobbyMusic();
                }

                if (newState == GameState.Intermission)
                {
                    _uiManager?.SetWaitingForPlayersText("Waiting for players...");
                    ClearLocalMapReference();
                    PlayLobbyMusic();
                }
            }
        }

        private void OnDifficultyChanged(float oldVal, float newVal)
        {
            if (IsClient)
            {
                _uiManager?.UpdateLobbyDifficultyOnly(newVal);
            }
        }

        private void OnMapMechanicsStartedChanged(bool oldVal, bool newVal)
        {
            if (!IsClient || CurrentState.Value != GameState.Playing || !newVal) return;
            UpdateHUDAndBoardPlayerCount();
            if (LocalPlayer != null && LocalPlayer.Status.Value != PlayerStatus.Lobby)
                PlayCurrentMapMusic();
        }

        /// <summary>
        /// [Giai đoạn Intermission]
        /// Kiểm tra điều kiện để tự động bắt đầu game (Event Driven)
        /// </summary>
        private void CheckAutoStart()
        {
            if (!IsServer || CurrentState.Value != GameState.Intermission) return;

            // Kiểm tra trực tiếp từ danh sách ActivePlayers để lấy dữ liệu IsAFK thực tế nhất trên Server
            bool hasActivePlayer = _activePlayers.Any(p => p != null && p.IsSpawned && !p.IsAFK.Value);

            if (hasActivePlayer)
            {
                Debug.Log("[MultiplayerManager] Active player detected. Transitioning to Voting.");
                StartVoting();
            }
        }

        /// <summary>
        /// [Giai đoạn Voting]
        /// Bắt đầu 10s bình chọn map
        /// </summary>
        private void StartVoting()
        {
            if (!IsServer) return;
            if (!IsSpawned) return; // FIX Bug 4: Ensure OnNetworkSpawn has completed before modifying NetworkLists

            NetworkTime.Value = 10f; // 10s voting
            
            // Lấy 3 map ngẫu nhiên cùng Tier
            var tier = _palette.GetTierFromRating(Difficulty.Value); var maps = _mapDatabase.AllMaps
                .Where(m => _palette.GetTierFromRating(m.Difficulty) == tier)
                .OrderBy(x => Random.value)
                .Take(3).ToList();

            VotingMapNames.Clear();
            MapVotes.Clear();
            foreach (var m in maps)
            { // FIX Bug 4: Modify existing elements instead of adding/removing
                VotingMapNames.Add(m.Name);
                MapVotes.Add(0);
            }

            CurrentState.Value = GameState.Voting;
        }

        /// <summary>
        /// [Giai đoạn Voting]
        /// Kết thúc bình chọn, xác định map thắng cuộc và chuyển sang giai đoạn Setup
        /// </summary>
        private void HandleVotingEnd()
        {
            int winnerIndex = 0;
            int maxVotes = -1;
            for (int i = 0; i < MapVotes.Count; i++)
            {
                if (MapVotes[i] > maxVotes) { maxVotes = MapVotes[i]; winnerIndex = i; }
                else if (MapVotes[i] == maxVotes && Random.value > 0.5f) winnerIndex = i;
            }
            Debug.Log($"[MultiplayerManager] Voting ended. Winning map: {VotingMapNames[winnerIndex]} with {maxVotes} votes.");
            SetupRound(VotingMapNames[winnerIndex].ToString());
        }

        public void SubmitVote(int mapIndex)
        {
            SubmitVoteServerRpc(mapIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitVoteServerRpc(int mapIndex)
        {
            if (mapIndex >= 0 && mapIndex < MapVotes.Count) MapVotes[mapIndex]++;
        }

        /// <summary>
        /// [Giai đoạn Setup round]
        /// Khởi tạo map, teleport player và chuẩn bị bắt đầu
        /// </summary>
        private void SetupRound(string mapName)
        {
            if (!IsServer) return;

            _roundGeneration++;
            int generation = _roundGeneration;
            NetCurrentMapName.Value = mapName;
            _playingPhaseStarted = false;
            StopRoundCoroutines();

            // Lấy danh sách người tham gia (Active players)
            _participants.Clear();
            _readyPlayers.Clear();
            _finishedPlayers.Clear();
            
            // Reset trạng thái Server-side trước khi bắt đầu
            _isStarting = false;
            _setupTimeoutHandled = false;
            IsMapMechanicsStartedNet.Value = false;
            _netReadyCount.Value = 0;
            _playerRespawnTimers.Clear();

            _playerFinishTimes.Clear();

            // SỬA LỖI: Sử dụng _activePlayers (authoritative) thay vì PlayerDataList (sync UI)
            foreach (var p in _activePlayers)
            {
                if (p != null && p.IsSpawned && !p.IsAFK.Value && p is NetworkBehaviour nb)
                    _participants.Add(nb.OwnerClientId);
            }

            // FIX Bug 2: Initialize alive/total counts for HUD
            _netTotalParticipants.Value = _participants.Count;
            _netAliveCount.Value = _participants.Count; // Initially all are alive
            _netReadyCount.Value = 0;
            if (_participants.Count == 0)
            {
                CurrentState.Value = GameState.Intermission;
                Debug.Log("[MultiplayerManager] No active players to start the round. Returning to Intermission.");
                return;
            }

            MapData mapData = _mapDatabase.AllMaps.First(m => m.Name == mapName);
            RegisterAllMapNetworkPrefabs();

            CleanupCurrentMap();
            NetCurrentMapName.Value = mapName;

            _currentMapInstance = Instantiate(mapData.MapPrefab, new Vector3(1000, 1000, 0), Quaternion.identity);
            if (!_currentMapInstance.TryGetComponent<NetworkObject>(out var mapNetObj))
            {
                Debug.LogError($"[MultiplayerManager] Map prefab '{mapName}' is missing NetworkObject.");
                Destroy(_currentMapInstance);
                _currentMapInstance = null;
                AbortRoundSetup(returnToVoting: true);
                return;
            }

            mapNetObj.Spawn();
            _currentMapManager = FindMapManagerOn(_currentMapInstance);

            CurrentState.Value = GameState.Playing;
            NetworkTime.Value = 20f;

            UpdateLobbyWorldUIClientRpc(mapName, Difficulty.Value);

            foreach (ulong clientId in _participants)
                SetLocalParticipantStatusClientRpc(PlayerStatus.InGame, RpcTarget.Single(clientId, RpcTargetUse.Temp));

            ulong mapNetId = mapNetObj.NetworkObjectId;
            ulong[] participantSnapshot = _participants.ToArray();
            StartCoroutine(DelayedStartRoundRoutine(mapName, generation, mapNetId, participantSnapshot));
        }

        private IEnumerator DelayedStartRoundRoutine(string mapName, int generation, ulong mapNetworkObjectId, ulong[] participantIds)
        {
            yield return null;
            if (!IsServer || !IsRoundGenerationCurrent(generation) || CurrentState.Value != GameState.Playing) yield break;
            StartRoundClientRpc(mapName, generation, mapNetworkObjectId, participantIds);
        }

        /// <summary>
        /// [Giai đoạn Setup round]
        /// Client nhận lệnh bắt đầu round, đợi map load xong, teleport player vào vị trí spawn và chuẩn bị cho giai đoạn Playing.
        /// </summary>
        /// <param name="mapName"></param>
        [Rpc(SendTo.ClientsAndHost)]
        private void StartRoundClientRpc(string mapName, int roundGeneration, ulong mapNetworkObjectId, ulong[] participantIds)
        {
            _roundGeneration = roundGeneration;
            _localRoundButtonsPressed = 0;
            _localFinishTime = -1f;

            ulong localId = NetworkManager.Singleton.LocalClientId;
            _localIsRoundParticipant = participantIds != null && participantIds.Contains(localId);

            DismissClientRoundSetup();
            ClearLocalMapReference();
            _uiManager?.ResetGameplayHUD();

            if (_localIsRoundParticipant && LocalPlayer != null)
                LocalPlayer.SetStatus(PlayerStatus.InGame);

            if (_localIsRoundParticipant && LocalPlayer != null && !LocalPlayer.IsAFK.Value)
                BackgroundMusicManager.Instance?.FadeTo(_loadingMusic, 0.25f, true, true);

            // Reset đếm số người về đích cho round mới
            _localRoundFinishCount = 0;

            _setupClientCoroutine = StartCoroutine(SetupClientRoutine(mapName, roundGeneration, mapNetworkObjectId));
        }

        /// <summary>
        /// Quản lý quá trình setup round trên Client: Hiển thị loading screen, đợi map load xong, teleport player vào vị trí spawn, và chuẩn bị cho giai đoạn Playing.
        /// Giai đoạn này có timeout 20s để tránh treo máy nếu load map thất bại. Nếu timeout xảy ra, client sẽ được teleport trở lại lobby và thông báo lỗi.
        /// </summary>
        /// <param name="mapName"></param>
        /// <returns></returns>
        private IEnumerator SetupClientRoutine(string mapName, int roundGeneration, ulong mapNetworkObjectId)
        {
            _uiManager?.ShowLoadingScreen(true);

            MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == mapName);
            _uiManager?.SetupMapLoadingScreen(data);
            UpdateSetupWaitingText();

            float t = 0;
            while (!TryResolveMapManager(mapName, mapNetworkObjectId, out _currentMapManager) && t < 20f)
            {
                if (!IsRoundGenerationCurrent(roundGeneration))
                {
                    _uiManager?.ShowLoadingScreen(false);
                    yield break;
                }

                if (CurrentState.Value != GameState.Playing)
                {
                    _uiManager?.ShowLoadingScreen(false);
                    yield break;
                }

                t += Time.deltaTime;
                yield return null;
            }

            if (!IsRoundGenerationCurrent(roundGeneration) || CurrentState.Value != GameState.Playing)
            {
                _uiManager?.ShowLoadingScreen(false);
                yield break;
            }

            if (_currentMapManager == null)
            {
                Debug.LogError("[MultiplayerManager] Client map load timed out! Aborting SetupClientRoutine.");
                if (_localIsRoundParticipant)
                    HandleClientLoadTimeout(roundGeneration);
                yield break;
            }

            if (!_localIsRoundParticipant)
            {
                _uiManager?.ShowLoadingScreen(false);
                _setupClientCoroutine = null;
                yield break;
            }

            if (LocalPlayer != null && LocalPlayer.Status.Value != PlayerStatus.InGame)
                LocalPlayer.SetStatus(PlayerStatus.InGame);

            if (LocalPlayer != null)
            {
                var currentMap = CurrentMapManager;

                LocalPlayer.PrepareForNewRound();
                LocalPlayer.SetInvincible(true);

                PlayerProfile profile = SaveSystem.LoadProfile();
                MapRecord record = profile?.MapRecords.Find(r => r.MapName == mapName);
                _uiManager?.SetRecordTime(record != null ? record.BestTime : -1f, currentMap.GetMaxMapTime());

                TeleportLocalPlayerToMapSpawn(currentMap);

                // QUAN TRỌNG: Đợi đến cuối frame để Cinemachine thực hiện Warp vật lý cho Camera Transform.
                // Nếu dùng yield return null, transform.position có thể vẫn đang ở Lobby (0,0).
                yield return new WaitForEndOfFrame();

                if (!IsRoundGenerationCurrent(roundGeneration) || CurrentState.Value != GameState.Playing)
                {
                    _uiManager?.ShowLoadingScreen(false);
                    yield break;
                }

                _uiManager?.UpdateAlivePlayerCount(_netAliveCount.Value, _netTotalParticipants.Value);
                _uiManager?.UpdateButtonProgress(0, currentMap.GetTotalButtonsCount());

                // Lúc này Camera đã thực sự ở Map (1000, 1000), Reset Parallax sẽ lấy mốc chuẩn.
                currentMap?.PrepareMapBackgrounds();

                // Sau khi mọi thứ đã ổn định mới bật lại Follow
                if (_vcam != null) _vcam.Follow = (LocalPlayer as MonoBehaviour).transform;
            }

            if (!IsRoundGenerationCurrent(roundGeneration) || CurrentState.Value != GameState.Playing)
            {
                _uiManager?.ShowLoadingScreen(false);
                yield break;
            }

            _uiManager?.ShowLoadingScreen(false);
            _setupClientCoroutine = null;
            ReportReadyServerRpc();
        }

        /// <summary>
        /// [Giai đoạn Playing]
        /// Bắt đầu đếm ngược 3s trước khi cho phép di chuyển
        /// </summary>
        private void StartPlaying()
        {
            if (!IsServer || _playingPhaseStarted) return;
            if (CurrentState.Value != GameState.Playing) return;

            _playingPhaseStarted = true;
            _isStarting = true;
            IsMapMechanicsStartedNet.Value = false;
            NetworkTime.Value = 0f;

            if (_serverCountdownCoroutine != null)
                StopCoroutine(_serverCountdownCoroutine);
            _serverCountdownCoroutine = StartCoroutine(ServerCountdownRoutine(_roundGeneration));
            StartCountdownClientRpc(_roundGeneration);
        }

        /// <summary>
        /// Quản lý đếm ngược 3s trên Server trước khi bắt đầu cơ chế map và cho phép di chuyển.
        /// </summary>
        /// <returns></returns>
        private IEnumerator ServerCountdownRoutine(int roundGeneration)
        {
            yield return new WaitForSeconds(3f);

            _serverCountdownCoroutine = null;

            if (!IsServer || !IsRoundGenerationCurrent(roundGeneration)) yield break;
            if (CurrentState.Value != GameState.Playing) yield break;

            IsMapMechanicsStartedNet.Value = true;
            _isStarting = false;
            NetworkTime.Value = 0f;
            _netStartTime.Value = NetworkManager.Singleton.ServerTime.Time; // Lưu mốc bắt đầu thực tế

            var map = CurrentMapManager;
            if (map == null && !string.IsNullOrEmpty(NetCurrentMapName.Value.ToString()) && _currentMapInstance != null)
            {
                ulong mapId = _currentMapInstance.TryGetComponent<NetworkObject>(out var mapNetObj)
                    ? mapNetObj.NetworkObjectId
                    : 0;
                TryResolveMapManager(NetCurrentMapName.Value.ToString(), mapId, out map);
            }

            if (map != null)
                map.StartMapMechanics();
            else
                Debug.LogError("[MultiplayerManager] Cannot start map mechanics — IMapManager is null on server.");

            CheckRoundCompletion();
        }

        /// <summary>
        /// Client RPC để bắt đầu đếm ngược 3s trên Client. 
        /// Trong thời gian này, UI sẽ hiển thị text "Get Ready: X" và sau khi kết thúc đếm ngược, text sẽ biến mất và player sẽ được mở khóa input.
        /// </summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void StartCountdownClientRpc(int roundGeneration)
        {
            _uiManager?.SetWaitingForPlayersText("");

            if (_clientCountdownCoroutine != null)
                StopCoroutine(_clientCountdownCoroutine);
            _clientCountdownCoroutine = StartCoroutine(CountdownRoutine(roundGeneration));
        }

        /// <summary>
        /// Quản lý đếm ngược 3s trên Client, hiển thị text "Get Ready: X" và sau khi kết thúc đếm ngược, mở khóa input cho player.
        /// </summary>
        /// <returns></returns>
        private IEnumerator CountdownRoutine(int roundGeneration)
        {
            for (int i = 3; i > 0; i--)
            {
                if (!IsRoundGenerationCurrent(roundGeneration)) yield break;
                _uiManager?.SetCountdownText($"Get Ready: {i}");
                yield return new WaitForSeconds(1f);
            }

            _clientCountdownCoroutine = null;

            if (!IsRoundGenerationCurrent(roundGeneration)) yield break;

            _uiManager?.SetCountdownText("");

            if (LocalPlayer != null && !LocalPlayer.IsAFK.Value && LocalPlayer.Status.Value == PlayerStatus.InGame)
                LocalPlayer.SetInvincible(false);

            PlayCurrentMapMusic();
        }

        /// <summary>
        /// Kiểm tra điều kiện kết thúc round
        /// </summary>
        private void CheckRoundCompletion()
        {
            // FIX Bug 4: Chỉ kiểm tra kết thúc round khi Map thực sự đã bắt đầu (sau countdown)
            if (CurrentState.Value != GameState.Playing || !IsMapMechanicsStartedNet.Value) return;
            
            // Thêm Safe-check: Nếu vừa bắt đầu mechanics < 1s, đợi thêm để sync network hoàn tất
            if (NetworkTime.Value < 1.0f) return;

            // Đếm những người thực sự còn đang tham gia thử thách (để check kết thúc round)
            int competingCount = _activePlayers.Count(p => 
                p != null && 
                p.IsSpawned &&
                _participants.Contains(((NetworkBehaviour)p).OwnerClientId) &&
                p.Status.Value == PlayerStatus.InGame && 
                !p.IsDead);

            // Đếm tất cả những người chưa chết (bao gồm cả người đã về đích) để hiển thị lên HUD
            int totalAliveCount = _activePlayers.Count(p => 
                p != null && 
                p.IsSpawned &&
                _participants.Contains(((NetworkBehaviour)p).OwnerClientId) &&
                (p.Status.Value == PlayerStatus.InGame || p.Status.Value == PlayerStatus.Finished) && 
                !p.IsDead);

            // Cập nhật NetworkVariables cho HUD (Dùng totalAliveCount để HUD không bị tụt số khi có người Win)
            _netAliveCount.Value = totalAliveCount;
            _netTotalParticipants.Value = _participants.Count;

            if (_participants.Count > 0 && competingCount <= 0) EndRound();
        }

        /// <summary>
        /// Kết thúc round, tính độ khó và quay về Voting
        /// </summary>
        private void EndRound()
        {
            if (!IsServer) return;

            int total = _participants.Count;
            int finished = _finishedPlayers.Count;
            float winRate = total > 0 ? (float)finished / total : 0;
            
            // New Difficulty Scale Logic
            float delta;
            if (winRate >= 0.5f)
            {
                // Từ 0.5 (50%) đến 1.0 (100%): Tăng từ +0.2 đến +0.4
                delta = Mathf.Lerp(0.2f, 0.4f, (winRate - 0.5f) / 0.5f);
            }
            else
            {
                // Từ 0 (0%) đến 0.5 (50%): Tăng từ -0.5 đến +0.2
                delta = Mathf.Lerp(-0.5f, 0.2f, winRate / 0.5f);
            }

            float oldDiff = Difficulty.Value;
            Difficulty.Value = Mathf.Clamp(oldDiff + delta, 1.0f, 4.99f);

            _isStarting = false;
            StartVoting();
        }

        /// <summary>
        /// Hết thời gian setup: loại player chưa ready, bắt đầu round cho những ai đã load xong.
        /// </summary>
        private void HandleSetupTimeout()
        {
            if (!IsServer || IsMapMechanicsStartedNet.Value || _playingPhaseStarted) return;

            _setupTimeoutHandled = true;

            var notReady = _participants.Where(id => !_readyPlayers.Contains(id)).ToList();
            foreach (ulong clientId in notReady)
            {
                _participants.Remove(clientId);
                _readyPlayers.Remove(clientId);

                IPlayer player = _activePlayers.FirstOrDefault(p => p is NetworkBehaviour nb && nb.OwnerClientId == clientId);
                player?.SetStatus(PlayerStatus.Lobby);
                NotifyRemovedFromRoundClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
            }

            _netTotalParticipants.Value = _participants.Count;
            _netAliveCount.Value = _participants.Count;
            _netReadyCount.Value = _readyPlayers.Count;

            Debug.LogWarning($"[MultiplayerManager] Setup timeout — {_readyPlayers.Count} ready, {_participants.Count} participants remain.");

            if (_participants.Count == 0 || _readyPlayers.Count == 0)
            {
                AbortRoundSetup(returnToVoting: true);
                return;
            }

            TryStartPlayingWhenAllReady();
        }

        /// <summary>
        /// Hủy setup round đang treo (timeout / không đủ player ready) và quay lại Voting hoặc Intermission.
        /// </summary>
        private void AbortRoundSetup(bool returnToVoting)
        {
            if (!IsServer) return;

            _isStarting = false;
            _playingPhaseStarted = false;
            _setupTimeoutHandled = false;
            IsMapMechanicsStartedNet.Value = false;
            StopRoundCoroutines();

            _readyPlayers.Clear();
            _netReadyCount.Value = 0;

            NotifyRoundSetupCancelledClientRpc();

            if (returnToVoting)
                StartVoting();
            else
                CurrentState.Value = GameState.Intermission;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ForceTimeoutDeathClientRpc()
        {
            // Chỉ những người đang trong map và chưa về đích mới bị xử lý
            if (LocalPlayer != null && LocalPlayer.Status.Value == PlayerStatus.InGame && !LocalPlayer.IsDead)
            {
                LocalPlayer.Die(DeathReason.TimeOut);
            }
        }
    }
}