using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using Core;
using Core.Events;
using System.Collections;

/// <summary>
/// Manager Multiplayer tập trung vào quản lý phòng và người chơi.
/// Phiên bản này đã được refactor lại để tối ưu hơn, fix lỗi gameloop và cải thiện trải nghiệm người chơi.
/// </summary>
public class MultiplayerManagerNew : NetworkBehaviour, IMultiplayerManager
{
    public static MultiplayerManagerNew Instance { get; private set; }

    [Header("Room Info")]
    public NetworkVariable<FixedString32Bytes> RoomId { get; } = new NetworkVariable<FixedString32Bytes>("");
    public NetworkVariable<FixedString32Bytes> Passcode { get; } = new NetworkVariable<FixedString32Bytes>("");
    public NetworkVariable<GameState> CurrentState = new NetworkVariable<GameState>(GameState.Intermission);
    public NetworkVariable<float> NetworkTime { get; } = new NetworkVariable<float>(0f);
    public NetworkVariable<float> Difficulty { get; } = new NetworkVariable<float>(1.0f);
    
    [Header("HUD Sync")]
    private NetworkVariable<int> _netAliveCount = new NetworkVariable<int>(0);
    private NetworkVariable<int> _netTotalParticipants = new NetworkVariable<int>(0);

    [Header("Player Management")]
    public NetworkList<PlayerNetworkData> PlayerDataList { get; private set; }
    private List<IPlayer> _activePlayers = new List<IPlayer>();
    public List<IPlayer> AllPlayers => _activePlayers;
    public IPlayer LocalPlayer { get; private set; }
    
    // Tracking cho round hiện tại
    private List<ulong> _participants = new List<ulong>();
    private HashSet<ulong> _readyPlayers = new HashSet<ulong>();
    private HashSet<ulong> _finishedPlayers = new HashSet<ulong>();

    [Header("UI & Scene References")]
    private IMultiplayerUIManager _uiManager;
    [SerializeField] private AudioClip _lobbyMusic;

    // Các thuộc tính bắt buộc từ Interface nhưng không thuộc phạm vi Room Management
    public NetworkList<FixedString64Bytes> VotingMapNames { get; private set; }
    public NetworkList<int> MapVotes { get; private set; }
    
    [Header("Settings & Databases")]
    [SerializeField] private MapDatabase _mapDatabase;
    [SerializeField] private DifficultyPalette _palette;
    [SerializeField] private Unity.Cinemachine.CinemachineCamera _vcam;
    [SerializeField] private PlayerSpawn _lobbySpawn;

    public bool IsGameActive => CurrentState.Value == GameState.Playing; 
    public bool IsPaused => false;
    public bool IsMultiplayer => true;
    public new bool IsHost => IsServer;

    private IMapManager _currentMapManager;
    private GameObject _currentMapInstance;
    private bool _isMapMechanicsStarted = false;

    [Header("Respawn Settings")]
    [SerializeField] private float _respawnDelay = 3f;
    private Dictionary<ulong, float> _playerRespawnTimers = new Dictionary<ulong, float>();

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

        if (_uiManager == null)
        {
            _uiManager = FindObjectsByType<Component>().OfType<IMultiplayerUIManager>().FirstOrDefault();
        }

        // Đăng ký cho các player đã tồn tại trong scene (phòng trường hợp scripts chạy sau player)
        foreach (var existingPlayer in FindObjectsByType<MonoBehaviour>().OfType<IPlayer>())
        {
            OnPlayerJoinedHandler(existingPlayer);
        }
    }

    public override void OnNetworkSpawn()
    {
        CurrentState.OnValueChanged += OnStateChanged;
        
        // Đăng ký sự kiện Gameplay toàn cục
        GameplayEvents.OnPlayerJoined += OnPlayerJoinedHandler;
        GameplayEvents.OnPlayerLeft += OnPlayerLeftHandler;
        GameplayEvents.OnLevelCompleted += OnPlayerFinishedHandler;
        GameplayEvents.OnPlayerDied += OnPlayerDiedHandler;
        GameplayEvents.OnLocalPlayerSpawned += OnLocalPlayerSpawnedHandler;

        GameplayEvents.OnButtonPressed += OnLocalButtonPressed;

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
            BackgroundMusicManager.Instance?.FadeTo(_lobbyMusic, 0f);

            // Đảm bảo tìm thấy LobbySpawn sớm nhất có thể
            if (_lobbySpawn == null) _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => s.gameObject.scene.name != "DontDestroyOnLoad");

            // Đăng ký thông tin bản thân lên Server
            string myName = DataManager.Instance != null ? DataManager.Instance.Profile.PlayerName : "Player " + NetworkManager.Singleton.LocalClientId;
            RegisterPlayerServerRpc(NetworkManager.Singleton.LocalClientId, myName, IsServer);
        }

        // Cập nhật UI ban đầu
        UpdateUIForState(CurrentState.Value);
    }

    private void OnStateChanged(GameState oldState, GameState newState)
    {
        UpdateUIForState(newState);

        if (IsServer && newState == GameState.Intermission)
        {
            NetworkTime.Value = 0f;
        }

        if (IsClient && newState == GameState.Intermission)
        {
            BackgroundMusicManager.Instance?.FadeTo(_lobbyMusic, 0.5f);
        }
    }

    private void UpdateUIForState(GameState state)
    {
        if (_uiManager == null) return;
        _uiManager.SetVotingButtonVisible(state == GameState.Voting);
        // HUD Mode: Nếu đang Playing thì hiện GameplayHUD, ngược lại hiện LobbyHUD
        _uiManager.SetHUDMode(state == GameState.Playing);
    }

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
        {
            for (int i = 0; i < PlayerDataList.Count; i++)
            {
                if (PlayerDataList[i].ClientId == clientId)
                {
                    PlayerDataList.RemoveAt(i);
                    break;
                }
            }
            _activePlayers.RemoveAll(p => p is NetworkBehaviour nb && nb.OwnerClientId == clientId);
        }

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            StopBackgroundMusic();
        }
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
                    if (_isMapMechanicsStarted)
                    {
                        NetworkTime.Value += Time.deltaTime;
                        if (NetworkTime.Value >= (_currentMapManager?.GetMaxMapTime() ?? 180f)) EndRound();
                    }
                    else
                    {
                        // Timeout cho giai đoạn Setup (20s)
                        NetworkTime.Value -= Time.deltaTime;
                        if (NetworkTime.Value <= 0) EndRound(); // Hoặc quay về Lobby nếu load lỗi
                    }
                    break;
            }
            // FIX: Cho phép hồi sinh ở mọi trạng thái (đặc biệt là khi Reset trong Lobby)
            HandleRespawnTimers();
        }

        // [LOCAL ONLY] Đẩy dữ liệu lên UI (Chạy trên cả Host và Client)
        UpdateLocalUI();
    }

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

    private void UpdateLocalUI()
    {
        if (_uiManager == null) return;

        GameState state = CurrentState.Value;
        
        // 1. Quản lý Slider Timer: Luôn hiển thị, tính toán maxTime theo state
        bool isVoting = state == GameState.Voting;
        float currentTime = 0f;
        float maxTime = 1f;

        if (state == GameState.Voting) { maxTime = 10f; currentTime = NetworkTime.Value; }
        else if (state == GameState.Playing)
        {
            maxTime = _currentMapManager?.GetMaxMapTime() ?? 180f;
            // Nếu chưa bắt đầu (đang load/countdown), slider giữ ở 0
            currentTime = _isMapMechanicsStarted ? NetworkTime.Value : 0f;
        }
        else { currentTime = 0f; maxTime = 1f; }

        _uiManager.UpdateTimeSlider(currentTime, maxTime, isVoting);

        // 2. Truyền dữ liệu Player Stats khi đang trong map chơi
        if (state == GameState.Playing && LocalPlayer != null && !LocalPlayer.IsInLobby.Value)
        {
            _uiManager.UpdatePersonalRecord(NetworkTime.Value);
            _uiManager.UpdateAirUI(LocalPlayer.CurrentBaseAir, LocalPlayer.CurrentBonusAir, LocalPlayer.CurrentBonusAirMax, LocalPlayer.CurrentAirChangeRate);
            
            // Cập nhật tiến độ nút bấm lên GameplayHUD
            if (_currentMapManager != null)
            {
                _uiManager.UpdateButtonProgress(_currentMapManager.GetButtonsActivatedCount(), _currentMapManager.GetTotalButtonsCount());
            }
        }
    }

    /// <summary>
    /// [Giai đoạn Intermission]
    /// Kiểm tra điều kiện để tự động bắt đầu game (Event Driven)
    /// </summary>
    private void CheckAutoStart()
    {
        if (!IsServer || CurrentState.Value != GameState.Intermission) return;

        // Kiểm tra trực tiếp từ danh sách ActivePlayers để lấy dữ liệu IsAFK thực tế nhất trên Server
        bool hasActivePlayer = _activePlayers.Any(p => p != null && !p.IsAFK.Value);

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

        NetworkTime.Value = 10f; // 10s voting
        
        // Lấy 3 map ngẫu nhiên cùng Tier
        var tier = _palette.GetTierFromRating(Difficulty.Value);
        var maps = _mapDatabase.AllMaps
            .Where(m => _palette.GetTierFromRating(m.Difficulty) == tier)
            .OrderBy(x => Random.value)
            .Take(3).ToList();

        VotingMapNames.Clear();
        MapVotes.Clear();
        foreach (var m in maps)
        {
            VotingMapNames.Add(m.Name);
            MapVotes.Add(0);
        }

        CurrentState.Value = GameState.Voting;
    }

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

    /// <summary>
    /// [Giai đoạn Setup round]
    /// Khởi tạo map, teleport player và chuẩn bị bắt đầu
    /// </summary>
    private void SetupRound(string mapName)
    {
        // Lấy danh sách người tham gia (Active players)
        _participants.Clear();
        _readyPlayers.Clear();
        _finishedPlayers.Clear();
        _isMapMechanicsStarted = false;

        // SỬA LỖI: Sử dụng _activePlayers (authoritative) thay vì PlayerDataList (sync UI)
        foreach (var p in _activePlayers)
        {
            if (p != null && !p.IsAFK.Value && p is NetworkBehaviour nb)
            {
                _participants.Add(nb.OwnerClientId);
            }
        }

        if (_participants.Count == 0)
        {
            CurrentState.Value = GameState.Intermission;
            Debug.Log("[MultiplayerManager] No active players to start the round. Returning to Intermission.");
            return;
        }

        // Spawn Map trên Server
        MapData mapData = _mapDatabase.AllMaps.First(m => m.Name == mapName);
        if (_currentMapInstance != null) _currentMapInstance.GetComponent<NetworkObject>().Despawn();
        
        _currentMapInstance = Instantiate(mapData.MapPrefab, new Vector3(1000, 1000, 0), Quaternion.identity);
        _currentMapInstance.GetComponent<NetworkObject>().Spawn();

        CurrentState.Value = GameState.Playing;
        NetworkTime.Value = 20f; // Bắt đầu đếm ngược timeout 20s cho việc Load map

        StartRoundClientRpc(mapName);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartRoundClientRpc(string mapName)
    {
        StartCoroutine(SetupClientRoutine(mapName));
    }

    private IEnumerator SetupClientRoutine(string mapName)
    {
        _uiManager?.ShowLoadingScreen(true);
        MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == mapName);
        _uiManager?.SetupMapLoadingScreen(data);

        // Đợi map xuất hiện
        float t = 0;
        while (_currentMapManager == null && t < 20f) // Timeout 20s theo design
        {
            _currentMapManager = FindObjectsByType<MonoBehaviour>().OfType<IMapManager>().FirstOrDefault();
            t += Time.deltaTime;
            yield return null;
        }

        if (LocalPlayer != null && !LocalPlayer.IsAFK.Value)
        {
            // Lấy kỷ lục cá nhân từ SaveSystem
            PlayerProfile profile = SaveSystem.LoadProfile();
            MapRecord record = profile?.MapRecords.Find(r => r.MapName == mapName);
            _uiManager?.SetRecordTime(record != null ? record.BestTime : -1f);

            // Tìm đúng PlayerSpawn của Map này (tránh lấy nhầm của Lobby hoặc map cũ)
            var mapSpawn = FindObjectsByType<PlayerSpawn>()
                .FirstOrDefault(s => s.IsMapSpawn && s.gameObject.scene == _currentMapInstance.scene);
            
            Vector3 spawnPos = mapSpawn != null ? mapSpawn.GetRandomSpawnPosition() : _currentMapManager.GetPlayerSpawnPosition();
            
            LocalPlayer.Teleport(spawnPos);
            LocalPlayer.SetInputBlocked(true);
            LocalPlayer.SetInvincible(true);
            CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);
            
            // Reset HUD cho round mới (làm sạch cờ, màu sắc và thông báo cũ)
            _uiManager?.ResetGameplayHUD();
            _uiManager?.UpdateButtonProgress(0, data != null ? data.ButtonNumber : 0);
            _currentMapManager.PrepareMapBackgrounds();
        }

        ReportReadyServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportReadyServerRpc(RpcParams rpc = default)
    {
        _readyPlayers.Add(rpc.Receive.SenderClientId);
        // Nếu tất cả đã ready hoặc những người còn lại không quan trọng
        if (_readyPlayers.Count >= _participants.Count)
        {
            StartPlaying();
        }
    }

    /// <summary>
    /// [Giai đoạn Playing]
    /// Bắt đầu đếm ngược 3s trước khi cho phép di chuyển
    /// </summary>
    private void StartPlaying()
    {
        StartCountdownClientRpc();
        StartCoroutine(ServerCountdownRoutine());
    }

    private IEnumerator ServerCountdownRoutine()
    {
        yield return new WaitForSeconds(3.5f); // Đợi countdown 3s + 0.5s bù trừ lag
        _isMapMechanicsStarted = true;
        NetworkTime.Value = 0f; // Bắt đầu tính giờ gameplay từ 0
        
        if (_currentMapManager != null)
        {
            _currentMapManager.StartMapMechanics();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StartCountdownClientRpc()
    {
        if (LocalPlayer != null && !LocalPlayer.IsAFK.Value)
        {
            LocalPlayer.IsInLobby.Value = false;
        }
        StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        for (int i = 3; i > 0; i--)
        {
            _uiManager?.SetCountdownText($"Get Ready: {i}");
            yield return new WaitForSeconds(1f);
        }
        _uiManager?.SetCountdownText("");
        _uiManager?.ShowLoadingScreen(false);

        if (LocalPlayer != null && !LocalPlayer.IsAFK.Value)
        {
            LocalPlayer.SetInputBlocked(false);
            LocalPlayer.SetInvincible(false);
        }
    }

    public GameState GetCurrentGameState() => CurrentState.Value;

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

    private void ExecuteLeave()
    {
        if (IsServer && LANDiscovery.Instance != null)
        {
            LANDiscovery.Instance.StopBroadcasting();
        }

        _uiManager?.ShowBackToMainMenuLoadingScreen();
        
        StopBackgroundMusic();
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene("Home");
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

    private void OnPlayerJoinedHandler(IPlayer player)
    {
        if (!_activePlayers.Contains(player))
        {
            _activePlayers.Add(player);
            if (IsServer) SubscribeToPlayerEvents(player);
        }
    }

    private void SubscribeToPlayerEvents(IPlayer player)
    {
        if (!IsServer) return;
        
        // Đăng ký lắng nghe các thay đổi trạng thái và truyền chính xác Player vào callback
        player.IsAFK.OnValueChanged += (oldVal, newVal) => OnPlayerAFKStatusChanged(player, oldVal, newVal);
        player.IsInLobby.OnValueChanged += OnPlayerLobbyStatusChangedServer;
        player.NetworkIsDead.OnValueChanged += OnPlayerDeathStatusChangedServer;
        
        // Cập nhật PlayerDataList ngay khi có người mới vào (để UI RoomInfo thấy luôn)
        SyncPlayerToDataList(player);

        if (CurrentState.Value == GameState.Intermission)
        {
            CheckAutoStart();
        }
    }

    private void OnPlayerLobbyStatusChangedServer(bool oldVal, bool newVal)
    {
        if (IsServer && newVal) CheckRoundCompletion();
    }

    private void OnPlayerDeathStatusChangedServer(bool oldVal, bool newVal)
    {
        // Nếu Player chuyển sang trạng thái chết, kiểm tra xem round có kết thúc không
        if (IsServer && newVal) CheckRoundCompletion();
    }

    private void OnPlayerAFKStatusChanged(IPlayer player, bool oldVal, bool newVal)
    {
        if (!IsServer) return;
        
        // Cập nhật PlayerDataList để đồng bộ UI RoomInfo cho tất cả mọi người
        SyncPlayerToDataList(player);

        // Kiểm tra điều kiện bắt đầu Voting bất cứ khi nào trạng thái AFK thay đổi
        CheckAutoStart();
    }

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

        // Nếu không tìm thấy (thường xảy ra với Host lúc khởi tạo), tạo mới entry
        // FIX: Mở khóa logic thêm Host vào danh sách đồng bộ
        // PlayerDataList.Add(new PlayerNetworkData
        // {
        //     ClientId = clientId,
        //     PlayerName = player is PlayerController pc ? pc.NetworkPlayerName.Value : (FixedString32Bytes)("Player " + clientId),
        //     IsHost = clientId == NetworkManager.Singleton.LocalClientId,
        //     IsAFK = player.IsAFK.Value
        // });
    }

    private void OnLocalButtonPressed()
    {
        // Khi LocalPlayer ấn nút, gửi thông báo cho Server để đồng bộ cho các Client khác
        if (IsClient && !IsServer)
        {
            NotifyButtonTriggerServerRpc();
        }
        else if (IsServer)
        {
            // Nếu là Host ấn, Server gửi thẳng lệnh đồng bộ cho các Client
            SyncButtonTriggerClientRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void NotifyButtonTriggerServerRpc(RpcParams rpcParams = default)
    {
        // Server nhận tin, yêu cầu tất cả Client (ngoại trừ người vừa ấn) kích hoạt nút tương ứng
        SyncButtonTriggerClientRpc(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncButtonTriggerClientRpc(ulong originalSenderId)
    {
        // Nếu là người đã ấn rồi thì không trigger lại trên MapManager để tránh double count
        if (NetworkManager.Singleton.LocalClientId == originalSenderId) return;

        if (_currentMapManager != null)
        {
            _currentMapManager.TriggerCurrentButton();
        }
    }
    
    private void OnPlayerLeftHandler(IPlayer player)
    {
        if (IsServer)
        {
            player.IsInLobby.OnValueChanged -= OnPlayerLobbyStatusChangedServer;
            player.NetworkIsDead.OnValueChanged -= OnPlayerDeathStatusChangedServer;
        }
        _activePlayers.Remove(player);
        if (IsServer) CheckRoundCompletion();
    }

    private void OnPlayerFinishedHandler(IPlayer player)
    {
        // [Giai đoạn Playing - Client Side]
        // Nếu chính là LocalPlayer về đích, hiển thị UI giống Singleplayer
        // Chỉ hiện thông báo Float, không hiện EndgameModal theo yêu cầu
        if (player == LocalPlayer)
        {
            _uiManager?.ShowPlayerFinishFlag(true);
            _uiManager?.ShowFloatNotification("Map Completed!", Color.green, 2f);
            LocalPlayer?.SetInvincible(true);
        }

        // [Giai đoạn Playing - Server Side]
        if (IsServer && player is NetworkBehaviour nb)
        {
            _finishedPlayers.Add(nb.OwnerClientId);
            CheckRoundCompletion();
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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void OnPlayerDeadServerRpc(ulong clientId)
    {
        if (!_playerRespawnTimers.ContainsKey(clientId))
        {
            _playerRespawnTimers[clientId] = _respawnDelay;
        }
        CheckRoundCompletion();
    }

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
                LocalPlayer.IsInLobby.Value = true;
                CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);

                _uiManager?.ShowPlayerFinishFlag(false);
            }
        }
    }

    private void OnLocalPlayerSpawnedHandler(IPlayer localPlayer)
    {
        LocalPlayer = localPlayer;
        localPlayer.IsAFK.Value = true; // Mặc định vào phòng là AFK

        // Đăng ký listener để UI tự động cập nhật khi LocalPlayer thay đổi trạng thái
        localPlayer.IsAFK.OnValueChanged += (oldVal, newVal) => _uiManager?.UpdatePlayStatus(newVal);
        localPlayer.IsSpectating.OnValueChanged += (oldVal, newVal) => _uiManager?.UpdateSpectateStatus(newVal);
        localPlayer.IsInLobby.OnValueChanged += (oldVal, newVal) => _uiManager?.SetHUDMode(CurrentState.Value == GameState.Playing);

        // Đồng bộ số lượng player tham gia lên HUD
        _netAliveCount.OnValueChanged += (old, newVal) => _uiManager?.UpdateAlivePlayerCount(newVal, _netTotalParticipants.Value);

        // Cập nhật UI ngay lập tức
        _uiManager?.UpdatePlayStatus(localPlayer.IsAFK.Value);
        _uiManager?.UpdateSpectateStatus(localPlayer.IsSpectating.Value);
        _uiManager?.SetHUDMode(CurrentState.Value == GameState.Playing);
        
        // Đồng bộ trạng thái nút Vote ngay khi spawn (cho trường hợp join giữa chừng phase Voting)
        _uiManager?.SetVotingButtonVisible(CurrentState.Value == GameState.Voting);

        StartCoroutine(SetupLocalPlayerRoutine(localPlayer));
    }

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
            Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
            localPlayer.Teleport(spawnPos);
            
            if (_vcam != null) CameraHelper.WarpToTarget(_vcam, playerMono);
        }
        _uiManager?.ShowJoiningLoadingScreen(false);
    }

    /// <summary>
    /// Kiểm tra điều kiện kết thúc round
    /// </summary>
    private void CheckRoundCompletion()
    {
        if (CurrentState.Value != GameState.Playing) return;
        
        int activeInMap = 0;
        foreach(var id in _participants)
        {
            // NetworkList không hỗ trợ LINQ FirstOrDefault, dùng vòng lặp thường
            IPlayer p = null;
            foreach (var ap in _activePlayers)
            {
                if (ap is NetworkBehaviour nb && nb.OwnerClientId == id)
                {
                    p = ap;
                    break;
                }
            }

            // Một player được coi là "đang thi đấu" nếu: 
            // Chưa thoát về Lobby, chưa về đích và CHƯA CHẾT
            if (p != null && !p.IsInLobby.Value && !_finishedPlayers.Contains(id) && !p.IsDead)
            {
                activeInMap++;
            }
        }

        // Cập nhật NetworkVariables cho HUD
        if (CurrentState.Value == GameState.Playing)
        {
            _netAliveCount.Value = activeInMap;
            _netTotalParticipants.Value = _participants.Count;
        }

        if (activeInMap <= 0) EndRound();
    }

    /// <summary>
    /// Kết thúc round, tính độ khó và quay về Voting
    /// </summary>
    private void EndRound()
    {
        // Tính toán độ khó
        float winRate = _participants.Count > 0 ? (float)_finishedPlayers.Count / _participants.Count : 0;
        float delta = Mathf.Lerp(-0.5f, 0.4f, winRate);
        Difficulty.Value = Mathf.Clamp(Difficulty.Value + delta, 1.0f, 4.99f);

        StartVoting();
    }

    public void SetRoomInfo(string roomId, string passcode)
    {
        if (!IsServer) return;
        RoomId.Value = roomId;
        Passcode.Value = passcode;
    }

    private void StopBackgroundMusic() => BackgroundMusicManager.Instance?.FadeTo(null, 0.5f);

    public override void OnDestroy()
    {
        GameplayEvents.OnPlayerJoined -= OnPlayerJoinedHandler;
        GameplayEvents.OnPlayerLeft -= OnPlayerLeftHandler;
        GameplayEvents.OnLevelCompleted -= OnPlayerFinishedHandler;
        GameplayEvents.OnPlayerDied -= OnPlayerDiedHandler;
        GameplayEvents.OnLocalPlayerSpawned -= OnLocalPlayerSpawnedHandler;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedFromServer;
        }

        if (IsServer && PlayerDataList != null)
        {
            PlayerDataList.OnListChanged -= OnPlayerDataListChanged;
        }
    }

    public void RequestStartGame() { if(IsServer) StartVoting(); }

    public void SendChatMessage(string message) { /* Tương tự bản cũ */ }

    public void SubmitVote(int mapIndex)
    {
        SubmitVoteServerRpc(mapIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitVoteServerRpc(int mapIndex)
    {
        if (mapIndex >= 0 && mapIndex < MapVotes.Count) MapVotes[mapIndex]++;
    }
}
