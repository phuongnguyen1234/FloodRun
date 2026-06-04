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
    public NetworkVariable<GameState> CurrentState = new(GameState.Intermission);
    public NetworkVariable<float> NetworkTime { get; } = new NetworkVariable<float>(0f);
    public NetworkVariable<float> Difficulty { get; } = new NetworkVariable<float>(1.0f);
    public NetworkVariable<bool> IsMapMechanicsStartedNet = new(false); // Đổi thành NetworkVariable để Client đồng bộ được Timer

    [Header("HUD Sync")]
    private NetworkVariable<int> _netAliveCount = new(0);
    private NetworkVariable<int> _netTotalParticipants = new(0);
    private NetworkVariable<int> _netReadyCount = new(0); // FIX: Để đồng bộ text "Waiting for players"

    [Header("Player Management")]
    public NetworkList<PlayerNetworkData> PlayerDataList { get; private set; }
    private List<IPlayer> _activePlayers = new();
    public List<IPlayer> AllPlayers => _activePlayers;
    public IPlayer LocalPlayer { get; private set; }
    
    /// <summary>
    /// Trả về IMapManager hiện tại một cách an toàn.
    /// Nếu map đã bị hủy (Destroyed), thuộc tính này sẽ trả về null thay vì gây MissingReferenceException.
    /// </summary>
    public IMapManager CurrentMapManager => (_currentMapManager != null && (_currentMapManager as MonoBehaviour) != null) ? _currentMapManager : null;

    private List<ulong> _participants = new();
    private HashSet<ulong> _readyPlayers = new();
    private HashSet<ulong> _finishedPlayers = new();
    private Dictionary<ulong, float> _playerFinishTimes = new();

    [Header("UI & Scene References")]
    private IMultiplayerUIManager _uiManager;
    [SerializeField] private AudioClip _lobbyMusic;
    [SerializeField] private AudioClip _loadingMusic;

    // Các thuộc tính bắt buộc từ Interface nhưng không thuộc phạm vi Room Management
    public NetworkList<FixedString64Bytes> VotingMapNames { get; private set; }
    public NetworkList<int> MapVotes { get; private set; }
    
    [Header("Settings & Databases")]
    [SerializeField] private MapDatabase _mapDatabase;
    [SerializeField] private DifficultyPalette _palette;
    [SerializeField] private Unity.Cinemachine.CinemachineCamera _vcam;
    [SerializeField] private PlayerSpawn _lobbySpawn;

    public bool IsGameActive => CurrentState.Value == GameState.Playing; 
    public GameState GetCurrentGameState() => CurrentState.Value;

    public bool IsPaused => false;
    public bool IsMultiplayer => true;
    public new bool IsHost => IsServer;
    private IMapManager _currentMapManager;
    private GameObject _currentMapInstance;
    private string _currentRoundMapName;
    private int _roundGeneration;

    private bool _isStarting = false; // Flag đánh dấu đang trong 3s đếm ngược (trước khi Mechanics chạy)
    private bool _playingPhaseStarted; // Chặn StartPlaying / countdown chồng giữa các round

    private Coroutine _setupClientCoroutine;
    private Coroutine _serverCountdownCoroutine;
    private Coroutine _clientCountdownCoroutine;

    [Header("Respawn Settings")]
    [SerializeField] private float _respawnDelay = 3f;
    private Dictionary<ulong, float> _playerRespawnTimers = new();

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

        _uiManager ??= FindObjectsByType<Component>().OfType<IMultiplayerUIManager>().FirstOrDefault();

        // Đăng ký cho các player đã tồn tại trong scene (phòng trường hợp scripts chạy sau player)
        foreach (var existingPlayer in FindObjectsByType<MonoBehaviour>().OfType<IPlayer>())
        {
            OnPlayerJoinedHandler(existingPlayer);
        }
    }

    public override void OnNetworkSpawn()
    {
        CurrentState.OnValueChanged += OnStateChanged;
        Difficulty.OnValueChanged += OnDifficultyChanged;
        IsMapMechanicsStartedNet.OnValueChanged += OnMapMechanicsStartedChanged;
        
        // Đăng ký sự kiện Gameplay toàn cục
        GameplayEvents.OnPlayerJoined += OnPlayerJoinedHandler;
        GameplayEvents.OnPlayerLeft += OnPlayerLeftHandler;
        GameplayEvents.OnLevelCompleted += OnPlayerFinishedHandler;
        GameplayEvents.OnPlayerDied += OnPlayerDiedHandler;
        GameplayEvents.OnLocalPlayerSpawned += OnLocalPlayerSpawnedHandler;

        // FIX Bug 1: Đồng bộ UI "Waiting for players" cho tất cả các Client
        _netReadyCount.OnValueChanged += (old, newVal) => {
            if (CurrentState.Value == GameState.Playing && !IsMapMechanicsStartedNet.Value)
                _uiManager?.SetWaitingForPlayersText($"Waiting for players ({newVal}/{_netTotalParticipants.Value})");
        };
        
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

            // Khởi tạo thông tin bảng Lobby ban đầu (Map trống, độ khó hiện tại)
            _uiManager?.UpdateLobbyWorldMapInfo(null, Difficulty.Value);

            // Đảm bảo tìm thấy LobbySpawn sớm nhất có thể
            if (_lobbySpawn == null) _lobbySpawn = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => s.gameObject.scene.name != "DontDestroyOnLoad");

            // Đăng ký thông tin bản thân lên Server
            string myName = DataManager.Instance != null ? DataManager.Instance.Profile.PlayerName : "Player " + NetworkManager.Singleton.LocalClientId;
            RegisterPlayerServerRpc(NetworkManager.Singleton.LocalClientId, myName, IsServer);
        }

        // Cập nhật UI ban đầu
        UpdateUIForState(CurrentState.Value);
    }

    private void OnDifficultyChanged(float oldVal, float newVal)
    {
        if (IsClient)
        {
            // FIX: Chỉ cập nhật độ khó, giữ nguyên tên và ảnh map của round vừa chơi
            _uiManager?.UpdateLobbyDifficultyOnly(newVal);
        }
    }

    private void OnStateChanged(GameState oldState, GameState newState)
    {
        UpdateUIForState(newState);

        if (IsServer && newState == GameState.Intermission)
        {
            NetworkTime.Value = 0f;
        }

        if (IsClient)
        {
            DismissClientRoundSetup();
            PlayLobbyMusic();

            if (newState == GameState.Intermission)
            {
                _uiManager?.SetWaitingForPlayersText("Waiting for players..."); // Reset khi về nghỉ
                ClearLocalMapReference();
            }
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

    private void OnMapMechanicsStartedChanged(bool oldVal, bool newVal)
    {
        if (!IsClient || CurrentState.Value != GameState.Playing || !newVal) return;
        UpdateHUDAndBoardPlayerCount();
        if (LocalPlayer != null && LocalPlayer.Status.Value != PlayerStatus.Lobby)
            PlayCurrentMapMusic();
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
        if (!string.IsNullOrEmpty(_currentRoundMapName) && _mapDatabase != null)
        {
            MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == _currentRoundMapName);
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
                        if (NetworkTime.Value >= map.GetMaxMapTime()) EndRound();
                    }
                    else if (!_isStarting) // Bắt đầu đếm ngược 20s cho giai đoạn Setup sau khi map được spawn
                    {
                        // Timeout cho giai đoạn Setup (20s)
                        NetworkTime.Value = Mathf.Max(0, NetworkTime.Value - Time.deltaTime);

                        // Chỉ kết thúc nếu chưa thực sự bắt đầu chơi
                        if (NetworkTime.Value <= 0 && !IsMapMechanicsStartedNet.Value) {
                            Debug.LogWarning("[MultiplayerManager] Setup timeout, aborting round setup.");
                            AbortRoundSetup(returnToVoting: true);
                        }
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

    /// <summary>
    /// Tính toán và áp dụng trạng thái khóa Input cho Local Player.
    /// Trạng thái khóa sẽ được kích hoạt nếu: (Đang trong map nhưng chưa bắt đầu) HOẶC (Có bất kỳ Modal UI nào đang mở).
    /// </summary>
    private void UpdateInputLockState()
    {
        if (LocalPlayer == null) return;

        // 1. Khóa do Logic Game: Đang Playing nhưng Mechanics chưa bắt đầu (Loading hoặc Countdown)
        bool logicLock = (CurrentState.Value == GameState.Playing && !IsMapMechanicsStartedNet.Value);

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
        
        float displayTime = mechanicsStarted ? NetworkTime.Value : 0f;

        // 2. Truyền dữ liệu Player Stats khi đang trong map chơi
        if (state == GameState.Playing && LocalPlayer != null && LocalPlayer.Status.Value != PlayerStatus.Lobby)
        {
            // FIX: Nếu đã về đích, hiển thị thời gian đã lưu. Nếu chưa, hiển thị NetworkTime.
            ulong myId = NetworkManager.Singleton.LocalClientId;
            if (_playerFinishTimes.ContainsKey(myId))
            {
                _uiManager.UpdatePersonalRecord(_playerFinishTimes[myId]);
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

    private void ClearLocalMapReference()
    {
        _currentMapManager = null;
    }

    /// <summary>
    /// Hủy map đang spawn trên Server và xóa tham chiếu ở mọi peer.
    /// </summary>
    private void CleanupCurrentMap()
    {
        if (IsServer && _currentMapInstance != null)
        {
            if (_currentMapInstance.TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
                netObj.Despawn(true);
            else
                Destroy(_currentMapInstance);
        }

        _currentMapInstance = null;
        _currentMapManager = null;
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

    /// <summary>
    /// Tìm IMapManager của round hiện tại — khớp tên map, tránh lấy nhầm map round trước còn đang destroy.
    /// </summary>
    private static IMapManager FindMapManagerOn(GameObject root)
    {
        if (root == null) return null;
        return root.GetComponentsInChildren<MonoBehaviour>(true).OfType<IMapManager>().FirstOrDefault();
    }

    private bool TryResolveMapManager(string mapName, ulong mapNetworkObjectId, out IMapManager manager)
    {
        manager = null;
        if (string.IsNullOrEmpty(mapName)) return false;

        MapData expectedData = _mapDatabase?.AllMaps.FirstOrDefault(m => m.Name == mapName);

        // Ưu tiên NetworkObjectId do Server gửi — không phụ thuộc GetMapData() (có thể bị LevelManager.SelectedMap ghi đè)
        if (mapNetworkObjectId != 0 && NetworkManager.Singleton != null)
        {
            var spawnManager = NetworkManager.Singleton.SpawnManager;
            if (spawnManager != null && spawnManager.SpawnedObjects.TryGetValue(mapNetworkObjectId, out NetworkObject netObj))
            {
                manager = FindMapManagerOn(netObj.gameObject);
                if (manager != null) return true;
            }
        }

        var candidates = FindObjectsByType<MonoBehaviour>()
            .OfType<IMapManager>()
            .Where(m => m is MonoBehaviour mb && mb.gameObject.activeInHierarchy)
            .ToList();

        foreach (var candidate in candidates)
        {
            MapData data = candidate.GetMapData();
            if (data == null) continue;

            if (data.Name == mapName || (expectedData != null && data.Name == expectedData.Name))
            {
                manager = candidate;
                return true;
            }

            if (expectedData != null && ReferenceEquals(data, expectedData))
            {
                manager = candidate;
                return true;
            }
        }

        return false;
    }

    private static PlayerSpawn FindPlayerSpawnOnMap(IMapManager map)
    {
        if (map is not MonoBehaviour mapRoot) return null;
        return mapRoot.GetComponentsInChildren<PlayerSpawn>(true).FirstOrDefault(s => s.IsMapSpawn);
    }

    private void TeleportLocalPlayerToMapSpawn(IMapManager map)
    {
        if (LocalPlayer == null || LocalPlayer.IsAFK.Value || map == null) return;

        PlayerSpawn mapSpawn = FindPlayerSpawnOnMap(map);
        Vector3 spawnPos = mapSpawn != null ? mapSpawn.GetRandomSpawnPosition() : map.GetPlayerSpawnPosition();
        LocalPlayer.Teleport(spawnPos);
        if (mapSpawn != null) LocalPlayer.SetFacing(mapSpawn.IsFacingRight);

        if (_vcam == null) _vcam = FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        if (_vcam != null && LocalPlayer is MonoBehaviour playerMono)
            CameraHelper.WarpToTarget(_vcam, playerMono);
    }

    private bool IsRoundGenerationCurrent(int generation) => generation == _roundGeneration;

    /// <summary>
    /// Hủy setup round đang treo (timeout / không đủ player ready) và quay lại Voting hoặc Intermission.
    /// </summary>
    private void AbortRoundSetup(bool returnToVoting)
    {
        if (!IsServer) return;

        _isStarting = false;
        _playingPhaseStarted = false;
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
    private void NotifyRoundSetupCancelledClientRpc()
    {
        DismissClientRoundSetup();
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
        _currentRoundMapName = mapName;
        _playingPhaseStarted = false;
        StopRoundCoroutines();

        // Lấy danh sách người tham gia (Active players)
        _participants.Clear();
        _readyPlayers.Clear();
        _finishedPlayers.Clear();
        
        // Reset trạng thái Server-side trước khi bắt đầu
        _isStarting = false;
        IsMapMechanicsStartedNet.Value = false;
        _netReadyCount.Value = 0;
        _playerRespawnTimers.Clear();

        _playerFinishTimes.Clear();

        // SỬA LỖI: Sử dụng _activePlayers (authoritative) thay vì PlayerDataList (sync UI)
        foreach (var p in _activePlayers)
        {
            if (p != null && p.IsSpawned && !p.IsAFK.Value && p is NetworkBehaviour nb)
            {
                _participants.Add(nb.OwnerClientId);
                // Server Authority: Ép trạng thái InGame cho những người tham gia
                p.Status.Value = PlayerStatus.InGame;
            }
        }

        // FIX Bug 2: Initialize alive/total counts for HUD
        _netTotalParticipants.Value = _participants.Count;
        _netAliveCount.Value = _participants.Count; // Initially all are alive
        _netReadyCount.Value = 0;
        _netReadyCount.Value = 0; // Reset số lượng người sẵn sàng cho round mới
        if (_participants.Count == 0)
        {
            CurrentState.Value = GameState.Intermission;
            Debug.Log("[MultiplayerManager] No active players to start the round. Returning to Intermission.");
            return;
        }

        // Spawn Map trên Server — dọn map cũ trước để không chồng NetworkObject giữa các round
        MapData mapData = _mapDatabase.AllMaps.First(m => m.Name == mapName);
        CleanupCurrentMap();
        _currentRoundMapName = mapName;

        _currentMapInstance = Instantiate(mapData.MapPrefab, new Vector3(1000, 1000, 0), Quaternion.identity);
        var mapNetObj = _currentMapInstance.GetComponent<NetworkObject>();
        mapNetObj.Spawn();
        _currentMapManager = FindMapManagerOn(_currentMapInstance);

        CurrentState.Value = GameState.Playing;
        NetworkTime.Value = 20f; // Bắt đầu đếm ngược timeout 20s cho việc Load map

        UpdateLobbyWorldUIClientRpc(mapName, Difficulty.Value);

        StartRoundClientRpc(mapName, generation, mapNetObj.NetworkObjectId);
    }

    /// <summary>
    /// [Giai đoạn Setup round]
    /// Client nhận lệnh bắt đầu round, đợi map load xong, teleport player vào vị trí spawn và chuẩn bị cho giai đoạn Playing.
    /// </summary>
    /// <param name="mapName"></param>
    [Rpc(SendTo.ClientsAndHost)]
    private void StartRoundClientRpc(string mapName, int roundGeneration, ulong mapNetworkObjectId)
    {
        _roundGeneration = roundGeneration;
        _currentRoundMapName = mapName;

        DismissClientRoundSetup();
        ClearLocalMapReference();
        _uiManager?.ResetGameplayHUD();

        // Chơi nhạc Loading cho những người tham gia round (Setup + Countdown)
        if (LocalPlayer != null && !LocalPlayer.IsAFK.Value)
        {
            BackgroundMusicManager.Instance?.FadeTo(_loadingMusic, 0.25f, true, true);
        }

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
        _uiManager?.SetWaitingForPlayersText($"Waiting for players ({_netReadyCount.Value}/{_netTotalParticipants.Value})");

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
            HandleClientLoadTimeout(roundGeneration);
            yield break;
        }

        if (LocalPlayer != null && !LocalPlayer.IsAFK.Value)
        {
            var currentMap = CurrentMapManager;

            LocalPlayer.PrepareForNewRound();
            LocalPlayer.SetInvincible(true);
            if (LocalPlayer.Status.Value != PlayerStatus.InGame) LocalPlayer.Status.Value = PlayerStatus.InGame;

            PlayerProfile profile = SaveSystem.LoadProfile();
            MapRecord record = profile?.MapRecords.Find(r => r.MapName == mapName);
            _uiManager?.SetRecordTime(record != null ? record.BestTime : -1f, currentMap.GetMaxMapTime());

            TeleportLocalPlayerToMapSpawn(currentMap);

            _uiManager?.UpdateAlivePlayerCount(_netAliveCount.Value, _netTotalParticipants.Value);
            _uiManager?.UpdateButtonProgress(0, currentMap.GetTotalButtonsCount());

            currentMap?.PrepareMapBackgrounds();
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
    /// Cập nhật LobbyInfoBoard trên Client với thông tin map đã chọn và độ khó. Hàm này được gọi từ Server sau khi map đã được chọn và round bắt đầu.
    /// </summary>
    /// <param name="mapName"></param>
    /// <param name="difficulty"></param>
    [Rpc(SendTo.ClientsAndHost)]
    private void UpdateLobbyWorldUIClientRpc(FixedString64Bytes mapName, float difficulty)
    {
        MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == mapName.ToString());
        _uiManager?.UpdateLobbyWorldMapInfo(data, difficulty);
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
            LocalPlayer.Status.Value = PlayerStatus.Lobby;
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

        var map = CurrentMapManager;
        if (map == null && !string.IsNullOrEmpty(_currentRoundMapName) && _currentMapInstance != null)
        {
            ulong mapId = _currentMapInstance.TryGetComponent<NetworkObject>(out var mapNetObj)
                ? mapNetObj.NetworkObjectId
                : 0;
            TryResolveMapManager(_currentRoundMapName, mapId, out map);
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

        if (LocalPlayer != null && !LocalPlayer.IsAFK.Value)
            LocalPlayer.SetInvincible(false);

        PlayCurrentMapMusic();
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
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene("Home");
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

    private void OnPlayerJoinedHandler(IPlayer player)
    {
        if (!_activePlayers.Contains(player))
        {
            _activePlayers.Add(player);
            if (IsServer) SubscribeToPlayerEvents(player);
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

    private void OnPlayerAFKStatusChanged(IPlayer player, bool oldVal, bool newVal)
    {
        if (!IsServer) return;
        
        // Cập nhật PlayerDataList để đồng bộ UI RoomInfo cho tất cả mọi người
        SyncPlayerToDataList(player);

        // Kiểm tra điều kiện bắt đầu Voting bất cứ khi nào trạng thái AFK thay đổi (FIX Bug 4: Only if spawned)
        CheckAutoStart();
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

    private void OnPlayerLeftHandler(IPlayer player)
    {
        _activePlayers.Remove(player);
        if (IsServer) CheckRoundCompletion();
    }

    private void OnPlayerFinishedHandler(IPlayer player)
    {
        // [Giai đoạn Playing - Client Side]
        if (player == LocalPlayer)
        {
            _uiManager?.ShowPlayerFinishFlag(true);
            _uiManager?.ShowFloatNotification($"Completed {_currentRoundMapName}!", Color.green, 2f);
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
                LocalPlayer.Status.Value = PlayerStatus.Lobby; // Quay về Lobby
                PlayLobbyMusic(); // Chuyển về nhạc Lobby ngay khi hồi sinh
                CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);

                _uiManager?.ShowPlayerFinishFlag(false);
            }
        }
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
    /// Quản lý quá trình setup LocalPlayer sau khi spawn: Đợi 1 frame để ổn định, 
    /// tìm điểm spawn lobby, teleport player về lobby, thiết lập camera, và chuẩn bị cho giai đoạn tiếp theo.
    /// </summary>
    /// <param name="localPlayer"></param>
    /// <returns></returns>
    private IEnumerator SetupLocalPlayerRoutine(IPlayer localPlayer)
    {
        yield return null; // Chờ 1 frame để các thành phần Network ổn định

        // Đang trong round / setup map — SetupClientRoutine sẽ teleport, không kéo về lobby
        if (CurrentState.Value == GameState.Playing)
        {
            _uiManager?.ShowJoiningLoadingScreen(false);
            yield break;
        }

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
            
            // Thiết lập hướng mặt dựa trên lobby spawn
            localPlayer.SetFacing(_lobbySpawn.IsFacingRight);

            if (_vcam != null) CameraHelper.WarpToTarget(_vcam, playerMono);
        }
        _uiManager?.ShowJoiningLoadingScreen(false);
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

        int activeInMap = _activePlayers.Count(p => 
            p != null && 
            p.IsSpawned &&
            _participants.Contains(((NetworkBehaviour)p).OwnerClientId) &&
            p.Status.Value == PlayerStatus.InGame && 
            !_finishedPlayers.Contains(((NetworkBehaviour)p).OwnerClientId) && 
            !p.IsDead);

        // Cập nhật NetworkVariables cho HUD
        _netAliveCount.Value = activeInMap;
        _netTotalParticipants.Value = _participants.Count;

        if (_participants.Count > 0 && activeInMap <= 0) EndRound();
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
        StopRoundCoroutines();
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
            // Unsubscribe from player events for all active players
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
