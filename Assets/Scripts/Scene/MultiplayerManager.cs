using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using Core;
using Core.Events;
using Unity.Cinemachine;

namespace Multiplayer{
    /// <summary>
    /// Manager Multiplayer tập trung vào quản lý phòng và người chơi.
    /// Phiên bản này đã được refactor lại để tối ưu hơn, fix lỗi gameloop và cải thiện trải nghiệm người chơi.
    /// </summary>
    public partial class MultiplayerManager : NetworkBehaviour, IMultiplayerManager
    {
        public static MultiplayerManager Instance { get; private set; }

        [Header("Room Info")]
        public NetworkVariable<FixedString32Bytes> RoomId { get; } = new NetworkVariable<FixedString32Bytes>("");
        public NetworkVariable<FixedString32Bytes> Passcode { get; } = new NetworkVariable<FixedString32Bytes>("");

        [Header("UI & Scene References")]
        private IMultiplayerUIManager _uiManager;
        [SerializeField] private AudioClip _lobbyMusic;
        [SerializeField] private AudioClip _loadingMusic;

        [SerializeField] private CinemachineCamera _vcam;
        [SerializeField] private PlayerSpawn _lobbySpawn;

        public bool IsGameActive => CurrentState.Value == GameState.Playing; 
        public GameState GetCurrentGameState() => CurrentState.Value;

        public bool IsPaused => false;
        public bool IsMultiplayer => true;
        public new bool IsHost => IsServer;

        private bool _localIsRoundParticipant;

        private Coroutine _setupClientCoroutine;
        private Coroutine _serverCountdownCoroutine;
        private Coroutine _clientCountdownCoroutine;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Khởi tạo các danh sách đồng bộ
            PlayerDataList = new NetworkList<PlayerNetworkData>(
                new List<PlayerNetworkData>(),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

            VotingMapNames = new NetworkList<FixedString64Bytes>();
            MapVotes = new NetworkList<int>();

            // Tìm UI Manager bao gồm cả các object đang bị ẩn trong Scene
            _uiManager ??= FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).OfType<IMultiplayerUIManager>().FirstOrDefault();
            RegisterAllMapNetworkPrefabs();

            // Đăng ký cho các player đã tồn tại trong scene (phòng trường hợp scripts chạy sau player)
            foreach (var existingPlayer in FindObjectsByType<MonoBehaviour>().OfType<IPlayer>())
            {
                OnPlayerJoinedHandler(existingPlayer);
            }
        }

        public override void OnNetworkSpawn()
        {
            RegisterAllMapNetworkPrefabs();
            CurrentState.OnValueChanged += OnStateChanged;
            Difficulty.OnValueChanged += OnDifficultyChanged;
            IsMapMechanicsStartedNet.OnValueChanged += OnMapMechanicsStartedChanged;
            
            // Đăng ký sự kiện Gameplay toàn cục
            GameplayEvents.OnPlayerJoined += OnPlayerJoinedHandler;
            GameplayEvents.OnPlayerLeft += OnPlayerLeftHandler;
            GameplayEvents.OnLevelCompleted += OnPlayerFinishedHandler;
            GameplayEvents.OnPlayerDied += OnPlayerDiedHandler;
            GameplayEvents.OnLocalPlayerSpawned += OnLocalPlayerSpawnedHandler;
            GameplayEvents.OnButtonPressed += OnLocalButtonPressed;

            _netReadyCount.OnValueChanged += (_, _) => UpdateSetupWaitingText();
            _netTotalParticipants.OnValueChanged += (_, _) => UpdateSetupWaitingText();
            
            // FIX: Đồng bộ số lượng player sống sót lên cả HUD màn hình và bảng Lobby trong Scene
            _netAliveCount.OnValueChanged += (old, newVal) => UpdateHUDAndBoardPlayerCount();
            _netTotalParticipants.OnValueChanged += (old, newVal) => UpdateHUDAndBoardPlayerCount();

            if (IsServer)
            {
                // Gán thông tin phòng từ Cache nếu có (Host tạo phòng)
                if (!string.IsNullOrEmpty(MultiplayerRoomInfoCache.PendingRoomId))
                {
                    SetRoomInfo(MultiplayerRoomInfoCache.PendingRoomId, MultiplayerRoomInfoCache.PendingPasscode);
                    MultiplayerRoomInfoCache.PendingRoomId = null;
                    MultiplayerRoomInfoCache.PendingPasscode = null;
                }

                PlayerDataList.OnListChanged += OnPlayerDataListChanged;
                
                if (LANDiscovery.Instance != null)
                    LANDiscovery.Instance.UpdateBroadcastData(PlayerDataList.Count);
            }

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedFromServer;

            if (IsServer)
            {
                // Đăng ký sự kiện cho các player đã join từ trước khi NetworkSpawn (như Host)
                foreach (var p in _activePlayers)
                {
                    SubscribeToPlayerEvents(p);
                }
            }

            // FIX: Nếu Player đã spawn trước khi Manager này OnNetworkSpawn (thường gặp ở Host/LocalClient)
            if (IsClient && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<IPlayer>(out var localPlayer))
                {
                    // Gọi handler để setup reference và camera ngay lập tức
                    OnLocalPlayerSpawnedHandler(localPlayer);
                }
            }

            if (IsClient)
            {
                // Bắt đầu nhạc Lobby
                if (CurrentState.Value == GameState.Intermission)
                {
                    BackgroundMusicManager.Instance?.FadeTo(_lobbyMusic, 0f);
                    _uiManager?.SetWaitingForPlayersText("Waiting for players...");
                }

                // Khởi tạo tham chiếu Camera và đăng ký đồng bộ Map Name
                if (_vcam == null) _vcam = FindAnyObjectByType<CinemachineCamera>();
                NetCurrentMapName.OnValueChanged += OnNetMapNameChanged;
                OnNetMapNameChanged("", NetCurrentMapName.Value); // Cập nhật ngay lập tức cho người join mid-game

                // Đảm bảo tìm thấy LobbySpawn sớm nhất có thể
                if (_lobbySpawn == null) 
                    _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => !s.IsMapSpawn);

                // Đăng ký thông tin bản thân lên Server
                string myName = DataManager.Instance != null ? DataManager.Instance.Profile.PlayerName : "Player " + NetworkManager.Singleton.LocalClientId;
                RegisterPlayerServerRpc(NetworkManager.Singleton.LocalClientId, myName, IsServer);

                // FIX: Đồng bộ thủ công các thông số Player Count cho late joiner.
                // Vì các NetworkVariable đã có giá trị trước khi join, OnValueChanged sẽ không nổ ra.
                // Gọi các hàm này giúp cập nhật con số "X/Y Players" lên LobbyInfoBoard ngay khi join.
                UpdateHUDAndBoardPlayerCount();
                UpdateSetupWaitingText();
            }

            // Cập nhật UI ban đầu
            UpdateUIForState(CurrentState.Value);
        }

        /// <summary>
        /// Xử lý khi tên bản đồ hiện tại thay đổi hoặc khi vừa join vào phòng.
        /// </summary>
        private void OnNetMapNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
        {
            string mapNameStr = newVal.ToString();
            
            // Cập nhật thông tin lên bảng Lobby World UI
            if (_uiManager != null && _mapDatabase != null)
            {
                MapData data = string.IsNullOrEmpty(mapNameStr) ? null : _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == mapNameStr);
                _uiManager.UpdateLobbyWorldMapInfo(data, Difficulty.Value);
            }
        }

        private void UpdateHUDAndBoardPlayerCount()
        {
            int alive = _netAliveCount.Value;
            int total = _netTotalParticipants.Value;
            
            // Cập nhật HUD trên màn hình (GameplayHUD)
            _uiManager?.UpdateAlivePlayerCount(alive, total);

            // Cập nhật bảng Lobby trong Scene nếu đang trong quá trình chơi
            if (IsMapMechanicsStartedNet.Value)
            {
                if (CurrentState.Value == GameState.Voting)
                {
                    // Nếu đang voting, hiển thị kết quả cuối cùng của round vừa rồi
                    _uiManager?.SetWaitingForPlayersText($"{_finishedPlayers.Count}/{total} Survived");
                }
                else if (CurrentState.Value == GameState.Playing)
                _uiManager?.SetWaitingForPlayersText($"{alive}/{total} Players");
            }
        }

        private void PlayLobbyMusic()
        {
            // FIX: Chỉ chuyển sang nhạc Lobby nếu Player thực sự đang ở Lobby hoặc phòng đang Intermission.
            // Tránh việc đang ở trong map (vừa win xong hoặc đang đứng nhìn) mà bị đổi nhạc sang Lobby do room chuyển sang Voting.
            bool isAtLobby = LocalPlayer == null || LocalPlayer.Status.Value == PlayerStatus.Lobby;
            bool isIntermission = CurrentState.Value == GameState.Intermission;

            if (_lobbyMusic != null && (isAtLobby || isIntermission))
                BackgroundMusicManager.Instance?.FadeTo(_lobbyMusic, 0.5f, true, false);
        }

        private void PlayCurrentMapMusic()
        {
            if (!IsClient) return;
            if (LocalPlayer != null && LocalPlayer.Status.Value == PlayerStatus.Lobby) return;

            AudioClip clip = null;
            string currentMapName = NetCurrentMapName.Value.ToString();
            if (!string.IsNullOrEmpty(currentMapName) && _mapDatabase != null)
            {
                MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == currentMapName);
                clip = data != null ? data.BackgroundMusic : null;
            }

            clip = clip != null ? clip : CurrentMapManager?.GetMapMusic();
            if (clip != null)
                BackgroundMusicManager.Instance?.FadeTo(clip, 0.5f, true, true); // Luôn restart map music
        }

        /// <summary>
        /// Ẩn loading / countdown khi setup round bị hủy hoặc chuyển sang Voting.
        /// </summary>
        private void DismissClientRoundSetup()
        {
            StopRoundCoroutines();
            _uiManager?.ShowLoadingScreen(false);
            _uiManager?.SetWaitingForPlayersText("");
            _uiManager?.SetCountdownText("");
        }

        private void UpdateSetupWaitingText()
        {
            if (!IsClient || CurrentState.Value != GameState.Playing || IsMapMechanicsStartedNet.Value) return;

            string text = $"Waiting for players ({_netReadyCount.Value}/{_netTotalParticipants.Value})";
            _uiManager?.SetCountdownText(text);
            _uiManager?.SetWaitingForPlayersText(text);
        }

        /// <summary>
        /// Cập nhật trạng thái hiển thị của các thành phần UI dựa trên GameState hiện tại.
        /// </summary>
        /// <param name="state"></param>
        private void UpdateUIForState(GameState state)
        {
            if (_uiManager == null) return;
            _uiManager.SetVotingButtonVisible(state == GameState.Voting);
            // HUD Mode: Nếu đang Playing thì hiện GameplayHUD, ngược lại hiện LobbyHUD
            _uiManager.SetHUDMode(state == GameState.Playing);
        }

        private void Update()
        {
            // [SERVER ONLY] Cập nhật các biến đồng bộ NetworkVariable
            if (IsServer)
            {
                switch (CurrentState.Value)
                {
                    case GameState.Voting:
                        if (NetworkTime.Value > 0)
                        {
                            NetworkTime.Value -= Time.deltaTime;
                            if (NetworkTime.Value <= 0) HandleVotingEnd();
                        }
                        break;
                    case GameState.Playing:
                        // Sử dụng CurrentMapManager để check null an toàn trước khi truy cập GetMaxMapTime
                        var map = CurrentMapManager;
                        if (IsMapMechanicsStartedNet.Value && map != null)
                        {
                            NetworkTime.Value += Time.deltaTime;
                            if (NetworkTime.Value >= map.GetMaxMapTime()) 
                            {
                                // Ép buộc những người chưa về đích phải chết do hết giờ
                                ForceTimeoutDeathClientRpc();
                                EndRound();
                            }
                        }
                        else if (!_isStarting) // Bắt đầu đếm ngược 20s cho giai đoạn Setup sau khi map được spawn
                        {
                            // Timeout cho giai đoạn Setup (20s)
                            NetworkTime.Value = Mathf.Max(0, NetworkTime.Value - Time.deltaTime);

                            // Chỉ kết thúc nếu chưa thực sự bắt đầu chơi
                            if (NetworkTime.Value <= 0 && !IsMapMechanicsStartedNet.Value && !_setupTimeoutHandled)
                                HandleSetupTimeout();
                        }
                        break;
                }
                // FIX: Cho phép hồi sinh ở mọi trạng thái (đặc biệt là khi Reset trong Lobby)
                HandleRespawnTimers();
            }

            // [LOCAL ONLY] Đẩy dữ liệu lên UI (Chạy trên cả Host và Client)
            UpdateLocalUI();

            // Quản lý khóa Input tập trung: Tránh xung đột giữa Load Map và Modals
            UpdateInputLockState();
        }

        /// <summary>
        /// Tính toán và áp dụng trạng thái khóa Input cho Local Player.
        /// Trạng thái khóa sẽ được kích hoạt nếu: (Đang trong map nhưng chưa bắt đầu) HOẶC (Có bất kỳ Modal UI nào đang mở).
        /// </summary>
        private void UpdateInputLockState()
        {
            if (LocalPlayer == null) return;

            // 1. Khóa do Logic Game: Chỉ khóa nếu là người tham gia round (đã teleport vào map) 
            // và Game đang ở trạng thái Playing nhưng Mechanics chưa bắt đầu (Loading hoặc Countdown).
            bool logicLock = _localIsRoundParticipant && 
                            CurrentState.Value == GameState.Playing && 
                            !IsMapMechanicsStartedNet.Value;

            // 2. Khóa do UI: Có bất kỳ Modal nào đang mở (Settings, Room Info, Vote)
            bool uiLock = false;
            if (_uiManager != null) uiLock = _uiManager.IsAnyModalOpen();

            LocalPlayer.SetInputBlocked(logicLock || uiLock);
        }

        /// <summary>
        /// Cập nhật trạng thái hiển thị của các thành phần UI dựa trên GameState hiện tại và dữ liệu map.
        /// </summary>
        private void UpdateLocalUI()
        {
            if (_uiManager == null) return;

            GameState state = CurrentState.Value;
            IMapManager map = CurrentMapManager; // Lấy tham chiếu an toàn
            
            // 1. Quản lý Slider Timer: Luôn hiển thị, tính toán maxTime theo state
            bool isVoting = state == GameState.Voting;
            float currentTime = 0f;
            float maxTime = 1f;
            bool mechanicsStarted = IsMapMechanicsStartedNet.Value;

            if (state == GameState.Voting) { maxTime = 10f; currentTime = NetworkTime.Value; }
            else if (state == GameState.Playing)
            {
                maxTime = map?.GetMaxMapTime() ?? 180f;
                currentTime = mechanicsStarted ? NetworkTime.Value : 0f;
            }
            else { currentTime = 0f; maxTime = 1f; }

            _uiManager.UpdateTimeSlider(currentTime, maxTime, isVoting);

            float displayTime = GetElapsedRoundTime();

            // 2. Truyền dữ liệu Player Stats khi đang trong map chơi
            if (state == GameState.Playing && LocalPlayer != null && LocalPlayer.Status.Value != PlayerStatus.Lobby)
            {
                // FIX: Ưu tiên hiển thị thời gian đã chốt (Personal Finish Time) nếu có, 
                // giúp đồng hồ trên HUD dừng lại ngay khi người chơi về đích.
                if (_localFinishTime > 0)
                {
                    _uiManager.UpdatePersonalRecord(_localFinishTime);
                }
                else
                {
                    _uiManager.UpdatePersonalRecord(displayTime);
                }

                _uiManager.UpdateAirUI(LocalPlayer.CurrentBaseAir, LocalPlayer.CurrentBonusAir, LocalPlayer.CurrentBonusAirMax, LocalPlayer.CurrentAirChangeRate);
                
                // Cập nhật tiến độ nút bấm lên GameplayHUD
                if (map != null && mechanicsStarted) // Chỉ cập nhật tiến độ khi game đã thực sự chạy
                {
                    _uiManager.UpdateButtonProgress(map.GetButtonsActivatedCount(), map.GetTotalButtonsCount());
                }
            }
        }

        private void StopRoundCoroutines()
        {
            if (_setupClientCoroutine != null)
            {
                StopCoroutine(_setupClientCoroutine);
                _setupClientCoroutine = null;
            }
            if (_serverCountdownCoroutine != null)
            {
                StopCoroutine(_serverCountdownCoroutine);
                _serverCountdownCoroutine = null;
            }
            if (_clientCountdownCoroutine != null)
            {
                StopCoroutine(_clientCountdownCoroutine);
                _clientCountdownCoroutine = null;
            }
        }

        private bool IsRoundGenerationCurrent(int generation) => generation == _roundGeneration;

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyRoundSetupCancelledClientRpc()
        {
            DismissClientRoundSetup();
        }

        /// <summary>
        /// Xử lý tình huống khi client không thể load map trong thời gian quy định. 
        /// Client sẽ nhận được thông báo lỗi và được teleport trở lại lobby để tránh bị treo máy.
        /// </summary>
        private void HandleClientLoadTimeout(int roundGeneration)
        {
            if (!IsRoundGenerationCurrent(roundGeneration)) return;

            Debug.LogError("[MultiplayerManager] Client map load timed out! Returning to lobby.");
            _uiManager?.ShowLoadingScreen(false);
            _uiManager?.ShowFloatNotification("Map Load Timeout!", Color.red, 2f);
            ClearLocalMapReference();

            if (LocalPlayer != null)
            {
                if (_lobbySpawn == null) _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => !s.IsMapSpawn);
                if (_lobbySpawn != null) LocalPlayer.Teleport(_lobbySpawn.GetRandomSpawnPosition());
                LocalPlayer.SetStatus(PlayerStatus.Lobby);
                CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);
            }

            ReportClientLoadTimeoutServerRpc(NetworkManager.Singleton.LocalClientId);
        }

        /// <summary>
        /// Server RPC để nhận thông báo từ Client khi họ không thể load map trong thời gian quy định. 
        /// Server sẽ xử lý việc loại bỏ client này khỏi danh sách người tham gia và cập nhật lại số lượng người chơi còn lại.
        /// </summary>
        /// <param name="clientId"></param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportClientLoadTimeoutServerRpc(ulong clientId)
        {
            if (!IsServer || CurrentState.Value != GameState.Playing || IsMapMechanicsStartedNet.Value) return;

            if (!_participants.Remove(clientId)) return;

            _readyPlayers.Remove(clientId);
            _netTotalParticipants.Value = _participants.Count;
            _netAliveCount.Value = _participants.Count;

            Debug.LogWarning($"[MultiplayerManager] Client {clientId} removed from round after load timeout.");

            if (_participants.Count == 0)
            {
                AbortRoundSetup(returnToVoting: true);
                return;
            }

            TryStartPlayingWhenAllReady();
        }

        /// <summary>
        /// Server RPC để nhận thông báo từ Client khi họ đã sẵn sàng sau khi load map xong.
        /// </summary>
        /// <param name="rpc"></param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportReadyServerRpc(RpcParams rpc = default)
        {
            if (!IsServer || CurrentState.Value != GameState.Playing || IsMapMechanicsStartedNet.Value || _playingPhaseStarted)
                return;

            ulong clientId = rpc.Receive.SenderClientId;
            if (!_participants.Contains(clientId)) return;

            _readyPlayers.Add(clientId);
            _netReadyCount.Value = _readyPlayers.Count;

            TryStartPlayingWhenAllReady();
        }

        private void TryStartPlayingWhenAllReady()
        {
            if (_playingPhaseStarted || _participants.Count == 0) return;
            if (_readyPlayers.Count >= _participants.Count)
                StartPlaying();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void NotifyRemovedFromRoundClientRpc(RpcParams rpcParams = default)
        {
            DismissClientRoundSetup();
            ClearLocalMapReference();

            if (LocalPlayer == null) return;

            if (_lobbySpawn == null)
                _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => !s.IsMapSpawn);
            if (_lobbySpawn != null)
                LocalPlayer.Teleport(_lobbySpawn.GetRandomSpawnPosition());

            LocalPlayer.SetStatus(PlayerStatus.Lobby);
            if (_vcam == null) _vcam = FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
            if (_vcam != null && LocalPlayer is MonoBehaviour playerMono)
                CameraHelper.WarpToTarget(_vcam, playerMono);

            PlayLobbyMusic();
        }

        /// <summary>
        /// Yêu cầu rời khỏi phòng. Khi được gọi, sẽ hiển thị hộp thoại xác nhận và nếu được đồng ý, client sẽ ngắt kết nối khỏi Server
        /// </summary>
        public void RequestLeaveRoom()
        {
            _uiManager?.AskConfirmation("Do you want to leave the room?", () => {
                ExecuteLeave();
            });
        }

        /// <summary>
        /// Thực thi việc rời khỏi phòng sau khi đã được xác nhận. Server sẽ ngừng quảng bá nếu có, tất cả client sẽ hiển thị loading screen
        /// </summary>
        private void ExecuteLeave()
        {
            if (IsServer && LANDiscovery.Instance != null)
            {
                LANDiscovery.Instance.StopBroadcasting();
            }

            _uiManager?.ShowBackToMainMenuLoadingScreen();
            
            StopBackgroundMusic();

            // Đảm bảo dọn dẹp kết quả streak khi rời phòng để không bị cộng dồn sang phiên sau
            _localSessionResults?.Clear();

            NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene("Home");
        }

        /// <summary>
        /// Thiết lập thông tin phòng như Room ID và Passcode. 
        /// Chỉ Server mới có quyền thiết lập thông tin này và sẽ được đồng bộ cho tất cả Client thông qua NetworkVariables.
        /// </summary>
        /// <param name="roomId"></param>
        /// <param name="passcode"></param>
        public void SetRoomInfo(string roomId, string passcode)
        {
            if (!IsServer) return;
            RoomId.Value = roomId;
            Passcode.Value = passcode;
        }

        /// <summary>
        /// Dừng phát nhạc nền khi rời phòng hoặc khi kết thúc phiên chơi để tránh bị trùng lặp hoặc lỗi âm thanh khi quay về menu chính hoặc vào phòng khác.
        /// </summary>
        private void StopBackgroundMusic() => BackgroundMusicManager.Instance?.FadeTo(null, 0.5f);

        public override void OnDestroy()
        {
            IsMapMechanicsStartedNet.OnValueChanged -= OnMapMechanicsStartedChanged;
            Difficulty.OnValueChanged -= OnDifficultyChanged;
            NetCurrentMapName.OnValueChanged -= OnNetMapNameChanged;
            StopRoundCoroutines();
            GameplayEvents.OnPlayerJoined -= OnPlayerJoinedHandler;
            GameplayEvents.OnPlayerLeft -= OnPlayerLeftHandler;
            GameplayEvents.OnLevelCompleted -= OnPlayerFinishedHandler;
            GameplayEvents.OnPlayerDied -= OnPlayerDiedHandler;
            GameplayEvents.OnLocalPlayerSpawned -= OnLocalPlayerSpawnedHandler;
            GameplayEvents.OnButtonPressed -= OnLocalButtonPressed;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedFromServer;
            }

            if (IsServer && PlayerDataList != null)
            {
                PlayerDataList.OnListChanged -= OnPlayerDataListChanged;
                // Unsubscribe from player events for all active players
            }
        }

        public void RequestStartGame() { if(IsServer) StartVoting(); }

        public void SendChatMessage(string message) { /* Tương tự bản cũ */ }
    }
}
