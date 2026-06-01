using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;
using System.Collections.Generic;
using System.Collections;
using Core; // For MapData and LevelManager
using Unity.Cinemachine; // For CinemachineCamera
using Core.Events; // For GameplayEvents
using Unity.Collections; // For FixedString
using UnityEngine.SceneManagement; // For SceneManager.LoadScene
using System.Linq; // For FindObjectsByType with LINQ

    /// <summary>
    /// Quản lý vòng lặp Game trong môi trường Multiplayer.
    /// Điều phối trạng thái: Intermission -> Voting -> Playing -> Intermission.
    /// </summary>
    public class MultiplayerManager : NetworkBehaviour, IMultiplayerManager
    {
        public static MultiplayerManager Instance { get; private set; }

        [Header("Sync Variables")]
        public NetworkVariable<Core.Interfaces.GameState> CurrentState = new NetworkVariable<Core.Interfaces.GameState>(GameState.Intermission);
        
        // Đồng bộ thông tin phòng
        public NetworkVariable<FixedString32Bytes> RoomId { get; } = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<FixedString32Bytes> Passcode { get; } = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
        [Header("Voting System")]
        private NetworkVariable<float> _difficulty = new NetworkVariable<float>(1.0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<bool> _hasActivePlayers = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> Difficulty => _difficulty;

        // ĐỒNG BỘ UI: Dùng NetworkVariable để Client không phải tính toán for-loop
        private NetworkVariable<int> _netAliveCount = new NetworkVariable<int>(0);
        private NetworkVariable<int> _netTotalParticipants = new NetworkVariable<int>(0);

        // Chứa tên của 3 bản đồ được chọn để vote
        public NetworkList<FixedString64Bytes> VotingMapNames { get; private set; }
        // Chứa số lượng vote cho từng bản đồ (index 0, 1, 2)
        public NetworkList<int> MapVotes { get; private set; }
        private const float VOTE_DURATION = 10f;

        // Danh sách người chơi đồng bộ
        public NetworkList<PlayerNetworkData> PlayerDataList { get; private set; }
        
        // Triển khai từ IMultiplayerManager
        private NetworkVariable<float> _networkTime = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> NetworkTime => _networkTime;

        public Core.Interfaces.GameState GetCurrentGameState() => CurrentState.Value;

        public bool IsGameActive => CurrentState.Value == GameState.Playing;

        // IGameLoopManager implementation
        public IPlayer LocalPlayer { get; private set; }
        private List<IPlayer> _activePlayers = new List<IPlayer>();
        public List<IPlayer> AllPlayers => _activePlayers;         
        public new bool IsHost => IsServer;
        public bool IsMultiplayer => true;
        public bool IsPaused => false; // TODO: Implement pause logic cho MP nếu cần

        // --- New fields similar to GameplayManager ---
        [Header("Multiplayer Scene References")]
        [Tooltip("Kéo Virtual Camera từ Scene vào đây")]
        [SerializeField] private CinemachineCamera _vcam; 

        [SerializeField] private MapDatabase _mapDatabase;
        [SerializeField] private DifficultyPalette _palette;
        [Tooltip("Kéo object LobbySpawn trong scene vào đây")]
        [SerializeField] private PlayerSpawn _lobbySpawn;
        [Tooltip("Nhạc nền riêng khi ở Lobby Multiplayer")]
        [SerializeField] private AudioClip _lobbyMusic;
        
        private GameObject _currentMapInstance; 
        private IMapManager _mapManager; // Reference to the instantiated map's manager

        private IMultiplayerUIManager _uiManager;

        // Respawn tracking
        [SerializeField] private float _respawnDelay = 3f;
        private Dictionary<ulong, float> _playerRespawnTimers = new Dictionary<ulong, float>();

        private List<ulong> _playersInCurrentRound = new List<ulong>();
        private List<ulong> _finishedPlayers = new List<ulong>();
        private Dictionary<ulong, float> _playerFinishTimes = new Dictionary<ulong, float>();
        private bool _mapMechanicsConfirmedStarted = false;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Ensure there's a MultiplayerDisconnectHandler in the scene to handle client-side disconnect UI
            if (FindAnyObjectByType<MultiplayerDisconnectHandler>() == null)
            {
                var go = new GameObject("MultiplayerDisconnectHandler");
                go.AddComponent<MultiplayerDisconnectHandler>();
            }

            // Khởi tạo NetworkList với quyền mặc định
            PlayerDataList = new NetworkList<PlayerNetworkData>(
                new List<PlayerNetworkData>(), 
                NetworkVariableReadPermission.Everyone, 
                NetworkVariableWritePermission.Server
            );

            VotingMapNames = new NetworkList<FixedString64Bytes>(
                new List<FixedString64Bytes>(),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

            MapVotes = new NetworkList<int>(
                new List<int> { 0, 0, 0 },
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server
            );

            // Đăng ký ở Awake để đảm bảo bắt được sự kiện ngay cả khi Player spawn cực sớm
            GameplayEvents.OnLocalPlayerSpawned += OnLocalPlayerSpawnedHandler;
            GameplayEvents.OnPlayerJoined += OnPlayerJoinedHandler;
            GameplayEvents.OnPlayerLeft += OnPlayerLeftHandler;
            GameplayEvents.OnLevelCompleted += OnLevelCompletedHandler;
            GameplayEvents.OnPlayerDied += OnPlayerDiedHandler;
            // FIX #7: Subscribe to button press to update button progress in real-time
            GameplayEvents.OnButtonPressed += OnButtonPressedHandler;

            // Tìm UI Manager thông qua Interface (Pattern tương tự GameplayManager)
            if (_uiManager == null)
            {
                _uiManager = FindObjectsByType<Component>().OfType<IMultiplayerUIManager>().FirstOrDefault();
            }

            // Nếu player đã spawn trước khi Manager Awake, đăng ký lại để theo dõi trạng thái AFK và tránh stale state.
            foreach (var existingPlayer in FindObjectsByType<MonoBehaviour>().OfType<IPlayer>())
            {
                OnPlayerJoinedHandler(existingPlayer);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {   
                CurrentState.Value = GameState.Intermission;
            }

            // Lắng nghe thay đổi trạng thái game để chuyển đổi HUD tự động
            CurrentState.OnValueChanged += (oldVal, newVal) => 
            {
                if (_uiManager != null)
                {
                    // Khi bắt đầu phase Voting, hiện nút Vote. Khi kết thúc (Playing), ẩn nút.
                    _uiManager.SetVotingButtonVisible(newVal == GameState.Voting);

                    // Chỉ ép chuyển đổi HUD khi thực sự vào trận hoặc kết thúc hẳn về Intermission.
                    // Giai đoạn Voting sẽ "kệ" người chơi, giữ nguyên HUD hiện tại của họ.
                    if (newVal == GameState.Playing || newVal == GameState.Intermission)
                        _uiManager.SetHUDMode(newVal == GameState.Playing);
                    
                    // CHỈ Server mới có quyền reset NetworkVariable thời gian
                    if (IsServer)
                        _networkTime.Value = 0f;
                }
                
                // Xử lý chuyển nhạc dựa trên trạng thái Game
                if (IsClient)
                {
                    if (newVal == GameState.Intermission)
                    {
                        BackgroundMusicManager.Instance.FadeTo(_lobbyMusic, 0f); // Không fade nhạc lobby
                    }
                }
            };


            if (_uiManager != null) 
            {
                _uiManager.SetHUDMode(CurrentState.Value == GameState.Playing);
                _uiManager.SetVotingButtonVisible(CurrentState.Value == GameState.Voting);
            }

            // Host gán thông tin phòng từ dữ liệu tạm
            if (IsServer)
            {
                if (!string.IsNullOrEmpty(MultiplayerRoomInfoCache.PendingRoomId))
                {
                    SetRoomInfo(MultiplayerRoomInfoCache.PendingRoomId, MultiplayerRoomInfoCache.PendingPasscode);
                    MultiplayerRoomInfoCache.PendingRoomId = null;
                    MultiplayerRoomInfoCache.PendingPasscode = null;
                }

                // Đăng ký sự kiện thay đổi danh sách để cập nhật LAN Discovery
                PlayerDataList.OnListChanged += OnPlayerDataListChanged;
                LANDiscovery.Instance.UpdateBroadcastData(PlayerDataList.Count);
            }

            // CẢI TIẾN: Cả Server và Client đều cần lắng nghe sự kiện disconnect để cleanup/dừng nhạc.
            // Nếu để trong khối IsServer, các Client sẽ không bao giờ chạy logic StopBackgroundMusic khi rớt mạng.
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedFromServer;

            // FIX: Đối với Host/Local Client, Player thường spawn trước khi Manager này kịp lắng nghe sự kiện.
            // Kiểm tra nếu PlayerObject đã tồn tại thì thực hiện setup ngay.
            if (IsClient && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<IPlayer>(out var localPlayer))
                {
                    OnPlayerJoinedHandler(localPlayer);
                    OnLocalPlayerSpawnedHandler(localPlayer);
                    // Cập nhật UI ngay lập tức với trạng thái hiện tại của LocalPlayer
                    if (_uiManager != null) {
                        _uiManager.UpdatePlayStatus(localPlayer.IsAFK.Value);
                        _uiManager.UpdateSpectateStatus(localPlayer.IsSpectating.Value);
                    }
                }
            }

            // Mỗi Client khi spawn sẽ gửi thông tin tên của mình lên cho Server
            if (IsClient)
            {
                // Bắt đầu nhạc Lobby ngay khi vào phòng
                if (CurrentState.Value == GameState.Intermission)
                {
                    BackgroundMusicManager.Instance.FadeTo(_lobbyMusic, 0f);
                }

                string myName = DataManager.Instance != null ? DataManager.Instance.Profile.PlayerName : "Player " + NetworkManager.Singleton.LocalClientId;
                RegisterPlayerServerRpc(NetworkManager.Singleton.LocalClientId, myName, IsServer);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RegisterPlayerServerRpc(ulong clientId, string playerName, bool isHost)
        {
            // Kiểm tra trùng lặp bằng loop truyền thống
            for (int i = 0; i < PlayerDataList.Count; i++)
            {
                if (PlayerDataList[i].ClientId == clientId) return;
            }

            PlayerDataList.Add(new PlayerNetworkData
            {
                ClientId = clientId,
                PlayerName = playerName,
                IsHost = isHost,
                IsAFK = true // Mặc định AFK: Chờ người chơi nhấn "Play" để kích hoạt Auto Start
            });

            // Cập nhật trạng thái phòng ngay khi có người mới vào
            RefreshActivePlayerStatus();
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
            // Nếu là server, xóa player khỏi danh sách data
            if (IsServer)
            {
                for (int i = 0; i < PlayerDataList.Count; i++)
                {
                    if (PlayerDataList[i].ClientId == clientId)
                    {
                        PlayerDataList.RemoveAt(i);
                        break;
                    }
                }

                // CẢI TIẾN: Xóa sạch cả reference trong _activePlayers nếu event Despawn chưa kịp chạy
                _activePlayers.RemoveAll(p => p is NetworkBehaviour nb && nb.OwnerClientId == clientId);

                // CỰC KỲ QUAN TRỌNG: Cập nhật lại trạng thái phòng khi có người disconnect
                // Nếu người vừa out là người active cuối cùng, Host cần biết để hiện thông báo
                RefreshActivePlayerStatus();
            }

            // NOTE: MultiplayerDisconnectHandler đảm nhận xử lý UI + scene load khi local client disconnect
            // Chúng ta chỉ cần cleanup data ở đây
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                StopBackgroundMusic();
                Debug.Log("[MultiplayerManager] Local client disconnected, data cleaned up. Waiting for DisconnectHandler...");
            }
        }

        private void OnLevelCompletedHandler(IPlayer playerWhoCompleted)
        {
            if (playerWhoCompleted == LocalPlayer)
            {
                _uiManager?.ShowPlayerFinishFlag(true);

                // Theo thiết kế: Hiển thị thông báo float khi về đích
                _uiManager?.ShowNotification("Map Completed!", Color.green, 2f);
                
                // CẢI TIẾN: Khi về đích, cho người chơi bất tử và khóa input nhẹ 
                // để họ có thể đứng yên quan sát những người khác đang bơi lên.
                LocalPlayer.SetInvincible(true);
                // Bạn có thể cân nhắc khóa di chuyển nhưng cho phép nhảy ăn mừng:
                // LocalPlayer.SetInputBlocked(true); 
            }

            // Server ghi nhận người chơi đã về đích
            if (IsServer && playerWhoCompleted is NetworkBehaviour nb)
            {
                if (!_finishedPlayers.Contains(nb.OwnerClientId))
                {
                    Debug.Log($"[MultiplayerManager] Server: Player {nb.OwnerClientId} completed the map. Adding to finished players.");
                    _finishedPlayers.Add(nb.OwnerClientId);
                    
                    // FIX #3: Lưu thời gian hoàn thành của player để không update personal time nữa
                    if (!_playerFinishTimes.ContainsKey(nb.OwnerClientId))
                    {
                        _playerFinishTimes[nb.OwnerClientId] = _networkTime.Value;
                        Debug.Log($"[MultiplayerManager] Player {nb.OwnerClientId} finish time recorded: {_networkTime.Value:F2}s");
                    }
                    
                    UpdateAliveCountServer(); // Cập nhật số lượng hiển thị trên HUD
                    // FIX #8: Immediately check round completion when player wins
                    Debug.Log($"[MultiplayerManager] Player {nb.OwnerClientId} finished, checking round completion...");
                    CheckRoundCompletion();   // Kiểm tra kết thúc round
                    Debug.Log($"[MultiplayerManager] Player {nb.OwnerClientId} finished the map.");
                }
            }
        }

        public override void OnDestroy()
        {
            GameplayEvents.OnLocalPlayerSpawned -= OnLocalPlayerSpawnedHandler;
            GameplayEvents.OnPlayerJoined -= OnPlayerJoinedHandler;
            GameplayEvents.OnPlayerLeft -= OnPlayerLeftHandler;
            GameplayEvents.OnLevelCompleted -= OnLevelCompletedHandler;
            GameplayEvents.OnPlayerDied -= OnPlayerDiedHandler;
            GameplayEvents.OnButtonPressed -= OnButtonPressedHandler;

            // Unsubscribe callback bất kể IsServer hay IsClient
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedFromServer;
            }

            // Server-specific cleanup
            if (IsServer && NetworkManager.Singleton != null)
            {
                if (PlayerDataList != null) PlayerDataList.OnListChanged -= OnPlayerDataListChanged;
            }

            // Dừng broadcasting khi host thoát
            if (IsServer && LANDiscovery.Instance != null)
            {
                LANDiscovery.Instance.StopBroadcasting();
            }
        }

        /// <summary>
        /// Host gọi hàm này để thiết lập thông tin phòng hiển thị cho mọi người.
        /// </summary>
        public void SetRoomInfo(string roomId, string passcode)
        {
            if (!IsServer) return;
            RoomId.Value = roomId;
            Passcode.Value = passcode;
            Debug.Log($"[MultiplayerManager] Room Info Synced: {roomId} | {passcode}");
        }

        private void Update()
        {
            // Logic chạy trên mọi Client: Cập nhật thông số cá nhân lên HUD
            if (CurrentState.Value == GameState.Playing)
            {
                UpdateLocalPlayerStats();
            }

            if (!IsServer) return;

            if (CurrentState.Value == GameState.Voting)
            {
                _networkTime.Value += Time.deltaTime;
                if (_networkTime.Value >= VOTE_DURATION)
                {
                    FinishVoting();
                }
            }
            else if (CurrentState.Value == GameState.Playing)
            {
                // CHỈ tăng thời gian khi map đã thực sự bắt đầu (sau Countdown)
                // Chúng ta sẽ dùng chính _mapManager để biết map đã chạy hay chưa
                if (_mapManager != null && _mapManager.IsMapMechanicsStarted())
                {
                    _networkTime.Value += Time.deltaTime;
                    
                    // Server: Chỉ check Timeout trong Update vì thời gian là liên tục
                    if (IsServer && _networkTime.Value >= _mapManager.GetMaxMapTime())
                    {
                        EndRound();
                    }
                }
            }

            // Xử lý respawn timers
            HandleRespawnTimers();
        }

        private void CheckRoundCompletion()
        {
            if (!IsServer) return;

            Debug.Log($"[MultiplayerManager] CheckRoundCompletion called. MapMechanicsConfirmed: {_mapMechanicsConfirmedStarted}, NetworkTime: {_networkTime.Value:F2}s, MapManager: {(_mapManager != null ? "OK" : "NULL")}");

            // FIX #8: Allow checking round completion even in first 2 seconds if players are dead/finished
            // But skip if map mechanics haven't started yet
            if (!_mapMechanicsConfirmedStarted) 
            {
                Debug.Log("[MultiplayerManager] Map mechanics not started yet, skipping round completion check");
                return;
            }

            int totalInRound = _playersInCurrentRound.Count;
            if (totalInRound <= 0)
            {
                Debug.LogWarning("[MultiplayerManager] No players in current round!");
                return;
            }

            int doneCount = 0;
            foreach (var clientId in _playersInCurrentRound)
            {
                // Tìm player object tương ứng
                var pObj = _activePlayers.FirstOrDefault(p => p is NetworkBehaviour nb && nb.OwnerClientId == clientId);
                
                // FIX #8: Nếu player đã hoàn thành, hoặc đã về lobby (chết/thoát), hoặc đang spectate, thì coi là "done"
                bool isFinished = _finishedPlayers.Contains(clientId);
                bool isDeadOrInLobby = pObj == null || pObj.IsInLobby.Value;
                bool isSpectating = pObj != null && pObj.IsSpectating.Value;
                bool isDone = isFinished || isDeadOrInLobby || isSpectating;
                
                if (isDone)
                {
                    doneCount++;
                }
                Debug.Log($"[MultiplayerManager] Player {clientId}: Finished={isFinished}, Dead/InLobby={isDeadOrInLobby}, Spectating={isSpectating}, IsDone={isDone}");
            }
            Debug.Log($"[MultiplayerManager] Round completion check: {doneCount}/{totalInRound} players done");

            // 3. Kết thúc round nếu tất cả người tham gia đã hoàn thành (thắng hoặc thua)
            if (doneCount >= totalInRound)
            {
                Debug.Log("[MultiplayerManager] All players done! Ending round...");
                EndRound();
            }
        }

        private void UpdateAliveCountServer()
        {
            if (!IsServer) return;
            int alive = 0;
            foreach (var clientId in _playersInCurrentRound)
            {
                var p = _activePlayers.FirstOrDefault(x => x is NetworkBehaviour nb && nb.OwnerClientId == clientId);
                // Người còn "sống" là người đang ở trong map và chưa về đích
                if (p != null && !p.IsInLobby.Value && !_finishedPlayers.Contains(clientId)) alive++;
            }
            _netAliveCount.Value = alive;
        }

        private void EndRound()
        {
            if (!IsServer) return;
            
            Debug.Log("[MultiplayerManager] Round ended. Evaluating results and transitioning to Voting.");

            // Cập nhật độ khó dựa trên tỷ lệ sống sót của round vừa rồi
            UpdateDifficultyStats();

            // Sau khi kết thúc, lập tức quay trở lại giai đoạn Vote Map cho vòng tiếp theo
            RequestStartGame();
        }

        private void UpdateDifficultyStats()
        {
            int winners = 0;
            foreach (var clientId in _playersInCurrentRound)
            {
                if (_finishedPlayers.Contains(clientId)) winners++;
            }

            if (_playersInCurrentRound.Count > 0)
            {
                float winRatio = (float)winners / _playersInCurrentRound.Count;
                // 100% win -> +0.4, 0% win -> -0.5
                float diffChange = Mathf.Lerp(-0.5f, 0.4f, winRatio);
                _difficulty.Value = Mathf.Clamp(_difficulty.Value + diffChange, 1.0f, 4.99f);
                Debug.Log($"[MultiplayerManager] Win Ratio: {winRatio:P0}, Difficulty updated to: {_difficulty.Value:F2}");
            }
        }

        private void UpdateLocalPlayerStats()
        {
            if (LocalPlayer == null || _uiManager == null) return;

            // FIX #3: Dừng update personal time khi player đã hoàn thành
            if (LocalPlayer is NetworkBehaviour nb && _playerFinishTimes.ContainsKey(nb.OwnerClientId))
            {
                // Hiển thị finish time không đổi
                _uiManager.UpdatePersonalTime(_playerFinishTimes[nb.OwnerClientId]);
            }
            else
            {
                // Cập nhật thời gian và thanh trượt (dùng NetworkTime để đồng bộ)
                _uiManager.UpdatePersonalTime(_networkTime.Value);
            }
            
            // FIX #6: Thêm null check trước khi gọi GetMaxMapTime
            if (_mapManager != null)
            {
                try
                {
                    _uiManager.UpdateTimeSlider(_networkTime.Value, _mapManager.GetMaxMapTime());
                }
                catch (MissingReferenceException ex)
                {
                    Debug.LogWarning($"[MultiplayerManager] MapManager was destroyed during time slider update: {ex.Message}");
                }
            }

            // 2. Cập nhật Air UI (Oxy)
            _uiManager.UpdateAirUI(
                LocalPlayer.CurrentBaseAir,
                LocalPlayer.CurrentBonusAir,
                LocalPlayer.CurrentBonusAirMax,
                LocalPlayer.CurrentAirChangeRate
            );
        }

        private void HandleRespawnTimers()
        {
            if (_playerRespawnTimers.Count == 0) return;

            // Sử dụng danh sách tạm để tránh lỗi "Collection was modified"
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

        public void RequestStartGame() 
        { 
            if (!IsServer) return;
            
            // Đảm bảo Difficulty luôn nằm trong khoảng [1.0, 4.99]
            _difficulty.Value = Mathf.Clamp(_difficulty.Value, 1.0f, 4.99f);

            // Chọn 3 map ngẫu nhiên dựa trên tier độ khó hiện tại
            if (_mapDatabase != null && _palette != null)
            {
                var tier = _palette.GetTierFromRating(_difficulty.Value);
                var eligibleMaps = _mapDatabase.AllMaps
                    .Where(m => _palette.GetTierFromRating(m.Difficulty) == tier)
                    .OrderBy(x => Random.value)
                    .Take(3)
                    .ToList();

                VotingMapNames.Clear();
                for (int i = 0; i < 3; i++)
                {
                    MapVotes[i] = 0;
                    if (i < eligibleMaps.Count) VotingMapNames.Add(eligibleMaps[i].Name);
                }
            }

            _networkTime.Value = 0f; // Bắt đầu đếm từ 0
            CurrentState.Value = GameState.Voting; // Chuyển trạng thái sang Voting
        }

        private void FinishVoting()
        {
            if (!IsServer) return;
            
            // Reset respawn timers for new round
            _playerRespawnTimers.Clear();
            
            // Reset và snapshot danh sách người tham gia thực tế khi bắt đầu round
            _playersInCurrentRound.Clear();
            _finishedPlayers.Clear();
            foreach (var data in PlayerDataList)
            {
                if (!data.IsAFK) _playersInCurrentRound.Add(data.ClientId);
            }

            // Khởi tạo con số ban đầu cho ván đấu
            _netTotalParticipants.Value = _playersInCurrentRound.Count;
            // FIX #7: Initially set alive count to 0 (will update as players enter map)
            _netAliveCount.Value = 0;
            Debug.Log($"[MultiplayerManager] New round started with {_playersInCurrentRound.Count} participating players");

            // 0. Kiểm tra lại lần nữa xem có player nào thực sự active (không AFK) không
            RefreshActivePlayerStatus();
            if (!_hasActivePlayers.Value)
            {
                Debug.Log("[MultiplayerManager] No active players found. Cancelling round and returning to Intermission.");
                CurrentState.Value = GameState.Intermission;
                return;
            }

            // 1. Tìm số vote cao nhất (Sử dụng vòng lặp thay cho LINQ .Max() để tránh lỗi NetworkList)
            int maxVotes = 0;
            for (int i = 0; i < MapVotes.Count; i++)
            {
                if (MapVotes[i] > maxVotes) maxVotes = MapVotes[i];
            }
            
            // 2. Lấy danh sách index các map đạt số vote cao nhất (xử lý hòa)
            List<int> tiedIndices = new List<int>();
            for (int i = 0; i < MapVotes.Count; i++)
            {
                if (MapVotes[i] == maxVotes) tiedIndices.Add(i);
            }

            // 3. Chọn ngẫu nhiên nếu hòa, lấy map thắng
            int winnerIndex = tiedIndices[Random.Range(0, tiedIndices.Count)];
            FixedString64Bytes winnerMapName = VotingMapNames[winnerIndex];

            // Đảm bảo tất cả người tham gia đều được hồi sinh (Reset IsDead) trước khi vào map
            foreach (var clientId in _playersInCurrentRound)
            {
                var player = _activePlayers.FirstOrDefault(p => p is NetworkBehaviour nb && nb.OwnerClientId == clientId);
                player?.Revive();
            }

            // Chuyển trạng thái và reset thời gian trước khi gọi RPC
            _networkTime.Value = 0f;
            CurrentState.Value = GameState.Playing;

            // 1. Tìm MapData
            MapData winnerMapData = _mapDatabase?.AllMaps.FirstOrDefault(m => m.Name == winnerMapName.ToString());
            if (winnerMapData == null) return;

            // 1. Dọn dẹp map cũ
            if (_currentMapInstance != null)
            {
                var oldNetObj = _currentMapInstance.GetComponent<NetworkObject>();
                if (oldNetObj != null && oldNetObj.IsSpawned) oldNetObj.Despawn();
                else Destroy(_currentMapInstance);
            }

            // 2. Server sinh Map tại tọa độ cố định (1000, 1000)
            Vector3 spawnPos = new Vector3(1000, 1000, 0);
            _currentMapInstance = Instantiate(winnerMapData.MapPrefab, spawnPos, Quaternion.identity);
            
            // Quan trọng: Gọi Spawn để các Client tự động sinh map này
            var networkObj = _currentMapInstance.GetComponent<NetworkObject>();
            networkObj.Spawn();

            // Kích hoạt chuỗi khởi đầu game cho tất cả client
            StartGameSequenceClientRpc(winnerMapName);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StartGameSequenceClientRpc(FixedString64Bytes mapName)
        {
            _mapManager = null; // Reset reference cho ván mới
            MapData mapData = _mapDatabase?.AllMaps.FirstOrDefault(m => m.Name == mapName.ToString());
            if (mapData != null)
            {
                StartCoroutine(MultiplayerGameStartSequence(mapData));
            }
        }

        private IEnumerator MultiplayerGameStartSequence(MapData mapData)
        {
            // KHỞI TẠO: Reset trạng thái cho round mới
            _mapMechanicsConfirmedStarted = false;
            _playerFinishTimes.Clear();
            
            _uiManager?.SetupLoadingScreen(mapData);

            // Chỉ những người chơi không AFK mới tham gia vào ván đấu
            bool isParticipating = LocalPlayer != null && !LocalPlayer.IsAFK.Value;

            // FIX #3 & #4: Ẩn lá cờ hoàn thành và reset button progress cho round mới
            _uiManager?.ShowPlayerFinishFlag(false);
            if (mapData != null)
            {
                _uiManager?.UpdateButtonProgress(0, mapData.ButtonNumber);
            }

            // 1. Hiển thị Loading Screen và khóa điều khiển để chuẩn bị teleport
            _uiManager?.ShowLoadingScreen(true);

            // Đợi map object xuất hiện trên máy khách. 
            // FIX: Tìm IMapManager trong toàn bộ Scene vì NGO sẽ unparent Map ra root.
            float timeout = 5f;
            while (_mapManager == null && timeout > 0)
            {
                _mapManager = FindObjectsByType<MonoBehaviour>().OfType<IMapManager>().FirstOrDefault();
                if (_mapManager == null)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
            }

            // FIX #4: Thêm null check với timeout warning
            if (_mapManager == null)
            {
                Debug.LogError("[MultiplayerManager] MapManager could not be found after 5 seconds! Aborting sequence.");
                _uiManager?.ShowLoadingScreen(false);
                yield break;
            }

            if (_mapManager != null)
            {
                _uiManager?.SetMaxTime(_mapManager.GetMaxMapTime());
                
                // FIX #1: Hiển thị best time giống như singleplayer
                MapData currentMapData = _mapManager.GetMapData();
                if (currentMapData != null)
                {
                    PlayerProfile profile = SaveSystem.LoadProfile();
                    MapRecord record = profile?.MapRecords.Find(r => r.MapName == currentMapData.Name);
                    _uiManager?.SetRecordTime(record != null ? record.BestTime : -1f);
                    Debug.Log($"[MultiplayerManager] Best time loaded for {currentMapData.Name}: {(record != null ? record.BestTime : -1f)}s");
                }
            }

            yield return new WaitForSeconds(0.5f);

            // 2. Teleport người chơi vào vị trí Spawn của Map
            if (isParticipating && _mapManager != null)
            {
                // FIX #6: Thêm null check trước khi gọi GetPlayerSpawnPosition để tránh MissingReferenceException
                Vector3 spawnPos;
                try
                {
                    spawnPos = _mapManager.GetPlayerSpawnPosition();
                }
                catch (MissingReferenceException ex)
                {
                    Debug.LogError($"[MultiplayerManager] MapManager was destroyed before getting spawn position: {ex.Message}. Aborting sequence.");
                    _uiManager?.ShowLoadingScreen(false);
                    yield break;
                }
                
                LocalPlayer.Teleport(spawnPos);
                LocalPlayer.IsInLobby.Value = false; // Vào Map
                Debug.Log($"[MultiplayerManager] Local player entered map, IsInLobby set to false");
                CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);

                // Theo thiết kế: Set bất tử trong lúc chờ đếm ngược
                LocalPlayer.SetInvincible(true);

                // Khóa input nhưng giữ nguyên các hệ thống khác (Air/Flood check)
                LocalPlayer.SetInputBlocked(true);

                // Đợi camera Cinemachine ổn định vị trí tại Map mới (1000, 1000)
                yield return new WaitForSeconds(0.2f);
                
                // FIX #4: Thêm null check trước khi gọi PrepareMapBackgrounds
                if (_mapManager != null)
                {
                    try
                    {
                        _mapManager.PrepareMapBackgrounds();
                    }
                    catch (MissingReferenceException ex)
                    {
                        Debug.LogWarning($"[MultiplayerManager] MapManager was destroyed before PrepareMapBackgrounds: {ex.Message}");
                    }
                }
            }
            else if (isParticipating)
            {
                Debug.LogWarning("[MultiplayerManager] Player should participate but MapManager is null!");
            }

            // Tắt loading screen để người chơi thấy cảnh vật
            _uiManager?.ShowLoadingScreen(false);

            // 3. Thực hiện đếm ngược bắt đầu ván đấu (Cục bộ trên mỗi Client)
            float timeLeft = 3f;
            while (timeLeft > 0)
            {
                _uiManager?.SetCountdownText($"Get ready: {timeLeft:F0}");
                yield return new WaitForSeconds(1f);
                timeLeft--;
            }
            _uiManager?.SetCountdownText("");

            // 4. Cho phép người chơi điều khiển
            if (isParticipating) 
            {
                LocalPlayer.SetInvincible(false); // Hết đếm ngược -> Hết bất tử
                LocalPlayer.SetInputBlocked(false);
                LocalPlayer.EnableAbility();
            }

            // CẢI TIẾN: Phát nhạc Map tại đây sau khi chắc chắn MapManager đã được tìm thấy
            var mapMusic = _mapManager?.GetMapMusic();
            if (mapMusic != null) BackgroundMusicManager.Instance.FadeTo(mapMusic, 0.5f);

            // Server ra lệnh cho MapManager kích hoạt Flood và các sự kiện theo timeline
            if (IsServer && _mapManager != null) 
            {
                // Reset lại NetworkTime về 0 ngay thời điểm map THỰC SỰ bắt đầu (sau 3.5s đếm ngược)
                // Điều này giúp thanh tiến trình UI (time slider) đồng bộ tuyệt đối với timeline của MapManager
                _networkTime.Value = 0f;
                _mapManager.StartMapMechanics();
                _mapMechanicsConfirmedStarted = true; // Đánh dấu map đã bắt đầu
                Debug.Log("[MultiplayerManager] Map mechanics started, round completion checks enabled.");
            }
        }

        public void SubmitVote(int mapIndex)
        {
            // Client gọi method này, method này sẽ gửi RPC lên server
            SubmitVoteServerRpc(mapIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SubmitVoteServerRpc(int mapIndex)
        {
            if (mapIndex >= 0 && mapIndex < MapVotes.Count)
            {
                MapVotes[mapIndex]++;
            }
        }

        public void SendChatMessage(string message) { /* Logic gửi chat qua RPC */ }

        public void RequestKickPlayer(ulong clientId)
        {
            if (!IsServer) return;
            _uiManager?.AskConfirmation("Are you sure you want to kick this player?", () => {
                Debug.Log($"[MultiplayerManager] Kicking client {clientId}");
                NetworkManager.Singleton.DisconnectClient(clientId);
            });
        }

        public void RequestLeaveRoom()
        {
            _uiManager?.AskConfirmation("Do you want to leave the room?", () => {
                ExecuteLeave();
            });
        }

        public void RequestResetPlayer()
        {
            ResetPlayerServerRpc(NetworkManager.Singleton.LocalClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ResetPlayerServerRpc(ulong clientId)
        {
            if (!IsServer) return;
            Debug.Log($"[MultiplayerManager] Requesting character reset for client {clientId}");
            
            // Server yêu cầu Client tự gọi hàm Die() để đảm bảo đúng quyền Owner
            ForceDieClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ForceDieClientRpc(RpcParams rpcParams)
        {
            // Chạy trên Client sở hữu Player
            LocalPlayer?.Die(DeathReason.Reset);
        }

        private void ExecuteLeave()
        {
            if (IsServer)
            {
                LANDiscovery.Instance.StopBroadcasting();
            }

            _uiManager?.ShowBackToMainMenuLoadingScreen();
            
            StopBackgroundMusic();
            NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene("Home");
        }

        private void StopBackgroundMusic()
        {
            BackgroundMusicManager.Instance?.FadeTo(null, 0.5f);
        }

        private void OnPlayerJoinedHandler(IPlayer player)
        {
            if (!_activePlayers.Contains(player))
            {
                _activePlayers.Add(player);

                // FIX: Sử dụng NetworkManager.Singleton.IsServer thay vì IsServer 
                // vì IsServer của NetworkBehaviour có thể chưa sẵn sàng tại Awake/Start
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    // Unsubscribe first to prevent duplicate listeners
                    player.IsAFK.OnValueChanged -= OnPlayerAFKStatusChanged;
                    player.IsAFK.OnValueChanged += OnPlayerAFKStatusChanged;
                    
                    // Theo dõi Lobby status để trigger CheckRoundCompletion và UI
                    player.IsInLobby.OnValueChanged -= OnPlayerLobbyStatusChangedServer;
                    player.IsInLobby.OnValueChanged += OnPlayerLobbyStatusChangedServer;

                    Debug.Log($"[AFK Listener Registered] for player");
                    RefreshActivePlayerStatus();
                }
            }
        }

        private void OnPlayerAFKStatusChanged(bool previousValue, bool newValue)
        {
            RefreshActivePlayerStatus();
        }

        private void OnPlayerLobbyStatusChangedServer(bool previousValue, bool newValue)
        {
            if (!IsServer) return;
            
            // FIX #8: Handle both entering lobby (death/exit) and leaving lobby (enter map)
            if (CurrentState.Value == GameState.Playing)
            {
                Debug.Log($"[MultiplayerManager] Player lobby status changed: {previousValue} -> {newValue}");
                UpdateAliveCountServer();
                
                // Check round completion when player dies (enters lobby) or any status change
                if (newValue) // Player entered lobby (died/exited)
                {
                    Debug.Log($"[MultiplayerManager] Player entered lobby during playing, checking round completion");
                    CheckRoundCompletion();
                }
            }
        }
        
        private void OnButtonPressedHandler()
        {
            // FIX #7: Update button progress UI when button is pressed
            if (_mapManager == null || _uiManager == null) return;
            
            int currentCount = _mapManager.GetButtonsActivatedCount();
            int totalCount = _mapManager.GetTotalButtonsCount();
            
            Debug.Log($"[MultiplayerManager] Button pressed: {currentCount}/{totalCount}");
            _uiManager.UpdateButtonProgress(currentCount, totalCount);
        }

        private void RefreshActivePlayerStatus()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            
            bool currentAnyActive = false;
            for (int i = 0; i < PlayerDataList.Count; i++)
            {
                var data = PlayerDataList[i];
                var pObj = _activePlayers.FirstOrDefault(p => p is NetworkBehaviour nb && nb.OwnerClientId == data.ClientId);
                
                if (pObj != null)
                {
                    if (data.IsAFK != pObj.IsAFK.Value)
                    {
                        data.IsAFK = pObj.IsAFK.Value;
                        PlayerDataList[i] = data;
                    }
                    if (!pObj.IsAFK.Value) currentAnyActive = true;
                }
            }
            
            _hasActivePlayers.Value = currentAnyActive;

            // TỰ ĐỘNG BẮT ĐẦU (AUTO START):
            if (currentAnyActive && CurrentState.Value == GameState.Intermission)
            {
                RequestStartGame();
            }
        }

        private void OnPlayerLeftHandler(IPlayer player)
        {
            if (_activePlayers.Contains(player))
            {
                if (IsServer)
                {
                    player.IsAFK.OnValueChanged -= OnPlayerAFKStatusChanged;
                    player.IsInLobby.OnValueChanged -= OnPlayerLobbyStatusChangedServer;
                }

                _activePlayers.Remove(player);

                if (IsServer) 
                {
                    RefreshActivePlayerStatus();
                    UpdateAliveCountServer();
                    CheckRoundCompletion();
                }
            }
        }

        private void OnLocalPlayerSpawnedHandler(IPlayer localPlayer)
        {
            // Lưu reference tới local player để các systems có thể truy cập qua IGameLoopManager
            LocalPlayer = localPlayer;

            // Mặc định AFK khi vào phòng
            localPlayer.IsAFK.Value = true;
            
            // Lắng nghe sự thay đổi trạng thái AFK/Spectating của LocalPlayer để cập nhật UI
            LocalPlayer.IsAFK.OnValueChanged += (oldVal, newVal) => _uiManager?.UpdatePlayStatus(newVal);
            LocalPlayer.IsSpectating.OnValueChanged += (oldVal, newVal) => _uiManager?.UpdateSpectateStatus(newVal);
            LocalPlayer.IsInLobby.OnValueChanged += (oldVal, newVal) => _uiManager?.SetHUDMode(CurrentState.Value == GameState.Playing);

            _netAliveCount.OnValueChanged += (old, newVal) => _uiManager?.UpdateAlivePlayerCount(newVal, _netTotalParticipants.Value);
            _uiManager?.UpdatePlayStatus(localPlayer.IsAFK.Value); // Cập nhật UI ban đầu
            _uiManager?.UpdateSpectateStatus(localPlayer.IsSpectating.Value); // Cập nhật UI ban đầu
            _uiManager?.SetHUDMode(CurrentState.Value == GameState.Playing); // Cập nhật HUD ban đầu khi vừa spawn

            // Sử dụng Coroutine để đợi các Component mạng (NetworkTransform) ổn định trước khi xử lý
            StartCoroutine(SetupLocalPlayerRoutine(localPlayer));
        }

        private IEnumerator SetupLocalPlayerRoutine(IPlayer localPlayer)
        {
            // Chờ 1 frame để đảm bảo Ownership và Physics của Player đã được Netcode xác nhận
            yield return null;

            // 1. Xử lý Spawn Position
            // Fallback: Nếu quên chưa kéo LobbySpawn vào Inspector, tự động tìm trong Scene
            if (_lobbySpawn == null) _lobbySpawn = FindAnyObjectByType<PlayerSpawn>();

            if (_lobbySpawn != null)
            {
                Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
                localPlayer.Teleport(spawnPos);
                Debug.Log($"[MultiplayerManager] Local player teleported to LobbySpawn: {spawnPos}");
            }
            else Debug.LogWarning("[MultiplayerManager] LobbySpawn not found! Player will stay at default position.");

            // 2. Xử lý Camera Follow
            // 1. Xử lý Camera Target (QUAN TRỌNG: Cần gán target TRƯỚC khi teleport để snap hoạt động)
            // Fallback: Tìm Virtual Camera trong scene nếu chưa gán
            if (_vcam == null) _vcam = FindAnyObjectByType<CinemachineCamera>();

            if (_vcam != null && localPlayer is MonoBehaviour playerMono)
            {
                Debug.Log($"[MultiplayerManager] Setting camera follow for: {playerMono.name}");
                _vcam.Priority = 10;

                // Ưu tiên sử dụng CinemachineTargetSetter nếu có gắn trên Camera
                var targetSetter = _vcam.GetComponent<CinemachineTargetSetter>();
                if (targetSetter != null)
                {
                    targetSetter.SetCameraTarget(playerMono.transform);
                }
                else
                {
                    _vcam.Follow = playerMono.transform;
                    _vcam.LookAt = playerMono.transform;
                }
            }
            else Debug.LogWarning("[MultiplayerManager] Virtual Camera or Local Player reference is missing.");

            // 2. Xử lý Spawn Position & Camera Snap
            // Fallback: Nếu quên chưa kéo LobbySpawn vào Inspector, tự động tìm trong Scene
            if (_lobbySpawn == null) _lobbySpawn = FindAnyObjectByType<PlayerSpawn>();

            if (_lobbySpawn != null)
            {
                Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
                localPlayer.Teleport(spawnPos);
                // Gọi Helper để snap camera ngay lập tức sau khi gán Follow ở trên
                CameraHelper.WarpToTarget(_vcam, localPlayer as MonoBehaviour);
                Debug.Log($"[MultiplayerManager] Local player teleported & camera snapped to LobbySpawn: {spawnPos}");
            }
            else Debug.LogWarning("[MultiplayerManager] LobbySpawn not found! Player will stay at default position.");

            // Tắt màn hình joining loading sau khi đã setup xong xuôi vị trí và camera
            _uiManager?.ShowJoiningLoadingScreen(false);
        }

        private void OnPlayerDiedHandler()
        {
            // Gửi yêu cầu respawn lên server cho local player
            if (LocalPlayer is MonoBehaviour playerMono && playerMono.TryGetComponent<NetworkObject>(out var netObj))
            {
                OnPlayerDeadServerRpc(netObj.OwnerClientId);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void OnPlayerDeadServerRpc(ulong clientId)
        {
            // Server bắt đầu respawn timer cho player này
            if (!_playerRespawnTimers.ContainsKey(clientId))
            {
                _playerRespawnTimers[clientId] = _respawnDelay;
                Debug.Log($"[MultiplayerManager] Player {clientId} will respawn in {_respawnDelay}s");
                
                // FIX #5: Kiểm tra xem round có kết thúc không (nếu player là người duy nhất và chết)
                if (CurrentState.Value == GameState.Playing)
                {
                    Debug.Log($"[MultiplayerManager] Player {clientId} died during playing, updating alive count and checking round completion...");
                    UpdateAliveCountServer();
                    CheckRoundCompletion();
                }
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void RespawnPlayerClientRpc(RpcParams rpcParams)
        {
            // Chạy trên Client cần được hồi sinh
            if (LocalPlayer != null)
            {
                if (_lobbySpawn == null) _lobbySpawn = FindAnyObjectByType<PlayerSpawn>();

                if (_lobbySpawn != null)
                {
                    Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
                    LocalPlayer.Teleport(spawnPos);
                    LocalPlayer.Revive();
                    LocalPlayer.IsInLobby.Value = true; // Quay về Lobby
                    CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);

                    // Reset UI hoàn thành khi hồi sinh
                    _uiManager?.ShowPlayerFinishFlag(false);
                    
                    // TODO: Hiện SummaryModal theo MultiplayerDesign.md
                    Debug.Log("[MultiplayerManager] Local player respawned and teleported.");
                }
            }
        }
    }
