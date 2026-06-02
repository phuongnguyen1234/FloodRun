using UnityEngine;
using System.Collections;
using System.Collections.Generic; // Dùng List
using TMPro; // Nếu bạn muốn hiển thị timer lên UI
using Core.Interfaces; // Dùng Interface thay vì class cụ thể
using UnityEngine.Events; // Thêm namespace để dùng UnityEvent
using Core;
using Core.Events;
using Unity.Netcode;
using System.Linq;

/// <summary>
/// Định nghĩa một sự kiện sẽ được kích hoạt tại một thời điểm cụ thể trong màn chơi.
/// </summary>
[System.Serializable]
public class TimedMapEvent
{
    [Tooltip("Tên của sự kiện để dễ nhận biết trong Inspector.")]
    public string EventName = "New Timed Event";
    [Tooltip("Thời gian (tính bằng giây) kể từ khi game bắt đầu để kích hoạt sự kiện này.")]
    public float TriggerTime;
    [Tooltip("Các hành động sẽ được thực thi khi đến thời điểm kích hoạt.")]
    public UnityEvent OnTimeReached;
    [HideInInspector] public bool HasTriggered = false; // Cờ để đảm bảo sự kiện chỉ chạy 1 lần

    [Header("Actions List")]
    [SerializeReference] public List<MapAction> Actions = new List<MapAction>();
}

/// <summary>
/// MapManager là trung tâm quản lý tất cả các cơ chế liên quan đến bản đồ, bao gồm:
/// - Quản lý timeline sự kiện của map (TimedMapEvent)
/// - Quản lý trạng thái mở cửa (IsExitUnlocked)
/// - Cung cấp thông tin về map (MapData, nhạc nền, thời gian tối đa, v.v.)
/// - Cung cấp vị trí spawn cho GameplayManager
/// - Quản lý các Flood thông qua IFloodManager
/// - Quản lý chuỗi nút bấm thông qua IButtonSequenceManager
/// - Cung cấp các phương thức để tương tác với map (TriggerCurrentButton, GetNextButtonTransform, v.v.)
/// Khi cần sử dụng, truy cập qua giao diện IMapManager (thuộc Core)
/// </summary>
public class MapManager : NetworkBehaviour, IMapManager
{
    public static MapManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Kéo file MapData (ScriptableObject) tương ứng của level này vào đây. Nó sẽ ghi đè các cài đặt mặc định bên dưới.")]
    [SerializeField] private MapData _mapData;
    [SerializeField] private PlayerSpawn _playerSpawn;

    [Tooltip("Danh sách các Flood sẽ kích hoạt NGAY LẬP TỨC khi game bắt đầu. Nếu muốn Flood chạy nối tiếp, hãy dùng Event trong FloodController.")]
    [SerializeField] private List<GameObject> _floodManagerObjects;
    [Tooltip("Kéo GameObject chứa script implement IButtonSequenceManager (ButtonSequenceManager)")]
    [SerializeField] private GameObject _buttonSequenceManagerObject;
    
    [Tooltip("Độ cao (Y) thấp nhất cho phép. Nếu vật thể rơi thấp hơn mức này sẽ bị hủy/xử lý.")]
    [SerializeField] private float _killYThreshold = -30f;

    [Header("Map Timeline")]
    [Tooltip("Danh sách các sự kiện sẽ được kích hoạt tự động theo thời gian của màn chơi.")]
    [SerializeField] private List<TimedMapEvent> _mapTimelineEvents = new List<TimedMapEvent>();

    // State Variables
    public bool IsExitUnlocked { get; private set; } = false;

    [Header("Network Sync")]
    private NetworkVariable<double> _netStartTime = new NetworkVariable<double>(0);
    private NetworkVariable<bool> _netIsMapActive = new NetworkVariable<bool>(false);
    private NetworkVariable<int> _netButtonProgress = new NetworkVariable<int>(0);
    private double _localStartTime; 

    private bool _isMapActive = false;
    private bool _timelinesHalted = false;
    private bool _isPaused = false;
    
    private List<IFloodManager> _floodManagers = new List<IFloodManager>();
    private IButtonSequenceManager _buttonSequenceManager;

    // Registry lưu trữ các object theo ID để truy cập nhanh (O(1))
    private Dictionary<string, List<MonoBehaviour>> _objectRegistry = new Dictionary<string, List<MonoBehaviour>>();

     /// <summary>
    /// Trả về true nếu các cơ chế của map (timeline, flood, v.v.) đã bắt đầu.
    /// </summary>
    public bool IsMapMechanicsStarted() => _isMapActive;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) 
        {
            // Trong Multiplayer, khi Instantiate Map mới, Map cũ có thể chưa kịp huỷ (Destroy chạy cuối frame)
            // Việc Destroy Map mới sẽ làm hỏng toàn bộ logic. Ta cần ghi đè Instance!
            Instance = this;
        }

        // Đảm bảo MapManager luôn dùng đúng dữ liệu đã được chọn từ LevelManager
        if (LevelManager.SelectedMap != null)
        {
            _mapData = LevelManager.SelectedMap;
        }

        // Lấy component thông qua interface từ các GameObject đã gán
        if (_floodManagerObjects != null)
        {
            foreach (var obj in _floodManagerObjects)
            {
                if (obj != null && obj.TryGetComponent(out IFloodManager flood))
                    _floodManagers.Add(flood);
            }
        }

        if (_buttonSequenceManagerObject != null)
            _buttonSequenceManager = _buttonSequenceManagerObject.GetComponent<IButtonSequenceManager>();
            
        // VALIDATION: Kiểm tra xem thông tin trong MapData có khớp với map thực tế không
        ValidateMapIntegrity();
    }

    private void OnEnable()
    {
        GameplayEvents.OnPauseRequested += HandlePause;
        GameplayEvents.OnHaltTimelinesRequested += HaltMapTimelines;
    }

    private void OnDisable()
    {
        GameplayEvents.OnPauseRequested -= HandlePause;
        GameplayEvents.OnHaltTimelinesRequested -= HaltMapTimelines;
    }

    private void ValidateMapIntegrity()
    {
        if (_mapData == null) return;

        if (_mapData.BackgroundMusic != null && BackgroundMusicManager.Instance == null)
        {
            Debug.LogWarning("[Audio Missing] MapData có nhạc nền nhưng không tìm thấy BackgroundMusicManager trong Scene!");
        }
    }

    private void Start()
    {
        // 1. Lắng nghe sự kiện hoàn thành chuỗi nút
        if (_buttonSequenceManager != null && _buttonSequenceManager.OnSequenceComplete != null)
        {
            _buttonSequenceManager.OnSequenceComplete.AddListener(OnAllButtonsActivated);
        }
        else
        {
            // Nếu map không có nút nào, mặc định mở cửa luôn
            IsExitUnlocked = true;
        }
        // MapManager không tự StartCoroutine GameStartSequence nữa
        // Việc đó do GameplayManager gọi
    }

    public override void OnNetworkSpawn()
    {
        // 1. Lắng nghe trạng thái Map để kích hoạt cho Client
        _netIsMapActive.OnValueChanged += (oldVal, newVal) => {
            if (newVal) StartLocalMechanics();
        };

        if (_netIsMapActive.Value)
        {
            StartLocalMechanics();
        }

        // 2. Đồng bộ tiến độ nút cho Late Joiner
        if (_netButtonProgress.Value > 0)
        {
            SyncButtonProgressToCurrent(_netButtonProgress.Value);
        }

        // 3. Lắng nghe thay đổi tiến độ từ Server
        _netButtonProgress.OnValueChanged += (oldVal, newVal) => {
            if (!IsServer) SyncButtonProgressToCurrent(newVal);
            if (newVal > oldVal) { // Chỉ khi tiến độ tăng lên
                var uiManager = FindObjectsByType<MonoBehaviour>().OfType<IMultiplayerUIManager>().FirstOrDefault();
                uiManager?.ShowFloatNotification($"Pressed Button {newVal}", new Color(1f, 1f, 0.7f));
            }
        };
    }

    public override void OnNetworkDespawn()
    {
        _netIsMapActive.OnValueChanged -= (oldVal, newVal) => {};
        _netButtonProgress.OnValueChanged -= (oldVal, newVal) => {};
    }

    private void Update()
    {
        // Nếu map chưa active hoặc timeline đã bị Halt thì không chạy tiếp
        if (!_isMapActive || _timelinesHalted || _isPaused) return;

        // Nếu là NetworkObject nhưng chưa Spawn (chưa vào trận MP) thì không chạy Update này
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsSpawned) return;

        // Xử lý các sự kiện theo timeline của map
        ProcessTimeline();

        // Kiểm tra các vật thể rơi khỏi bản đồ
        CheckObjectsOutOfBound();
    }

    private void HandlePause(bool paused)
    {
        _isPaused = paused;
    }

    

    /// <summary>
    /// Gọi từ GameplayManager khi đếm ngược xong
    /// </summary>
    public void StartMapMechanics()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!IsServer) return;
            _netStartTime.Value = NetworkManager.Singleton.ServerTime.Time;
            _netIsMapActive.Value = true;
            
            // CẢI TIẾN: Host gọi ngay để tránh độ trễ 1 network tick (tránh DOTween tính sai elapsedTime)
            StartLocalMechanics();
        }
        else
        {
            _localStartTime = Time.timeAsDouble;
            StartLocalMechanics();
        }
    }

    /// <summary>
    /// Logic thực thi thực tế (Visual/Flood/Timeline) chạy trên cả SP và MP
    /// </summary>
    private void StartLocalMechanics()
    {
        if (_isMapActive) return; // Chống chạy 2 lần do OnValueChanged của NetworkVariable
        _isMapActive = true;

        // Reset trạng thái sự kiện
        foreach (var timedEvent in _mapTimelineEvents)
        {
            timedEvent.HasTriggered = false;
        }

        // KÍCH HOẠT FLOOD: Đưa logic này từ PrepareMapBackgrounds về đây
        // để đảm bảo nước chỉ dâng SAU khi đếm ngược kết thúc.
        if (!_timelinesHalted)
        {
            foreach (var flood in _floodManagers)
            {
                flood.StartFlood();
                
                // Đồng bộ thời gian cho người vào sau (Multiplayer)
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    flood.SyncToMapTime();
            }
        }
    }

    public void PrepareMapBackgrounds()
    {
        foreach (var p in GetComponentsInChildren<ParallaxEffect>())
        {
            p.ResetOrigin();
        }
    }

    /// <summary>
    /// Cung cấp vị trí spawn cho GameplayManager
    /// </summary>
    public Vector3 GetPlayerSpawnPosition()
    {
        if (_playerSpawn != null)
        {
            // Gọi hàm lấy vị trí ngẫu nhiên đã được định nghĩa trong PlayerSpawn
            return _playerSpawn.GetRandomSpawnPosition();
        }
        return transform.position; // Fallback về tâm MapManager
    }

    /// <summary>
    /// Trả về vị trí tâm của vùng spawn (không ngẫu nhiên).
    /// Dùng cho các công cụ Editor như Tool chụp ảnh preview.
    /// </summary>
    public Vector3 GetPlayerSpawnCenter()
    {
        if (_playerSpawn != null)
        {
            return _playerSpawn.transform.position;
        }
        return transform.position;
    }

    /// <summary>
    /// Trả về MapData của màn chơi hiện tại.
    /// </summary>
    public MapData GetMapData() => _mapData;

    public double GetMapStartTime()
    {
        // Bỏ check IsSpawned vì nếu MapManager nằm trên child object hoặc chưa kịp đăng ký, nó sẽ fallback về localTime gây lỗi siêu to
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return _netStartTime.Value;
        }
        return _localStartTime;
    }

    /// <summary>
    /// Trả về nhạc nền được cấu hình cho Map này.
    /// </summary>
    public AudioClip GetMapMusic() => _mapData != null ? _mapData.BackgroundMusic : null;

    public float GetMaxMapTime() => _mapData != null ? _mapData.MapDuration : 180f;

    public float GetKillYThreshold() => _killYThreshold;

    public bool IsDevToolEnabled() => _mapData != null && _mapData.EnableDevTools;

    /// <summary>
    /// Lấy số lượng nút đã kích hoạt (Dùng cho UI)
    /// </summary>
    public int GetButtonsActivatedCount()
    {
        // Ép kiểu về class cụ thể để lấy property, hoặc thêm property vào Interface IButtonSequenceManager
        if (_buttonSequenceManager is IButtonSequenceManager btnManager)
        {
            return btnManager.CurrentIndex;
        }
        return 0;
    }

    public int GetTotalButtonsCount()
    {
        if (_buttonSequenceManager is IButtonSequenceManager btnManager)
        {
            return btnManager.TotalButtons;
        }
        return 0;
    }

    public Transform GetNextButtonTransform()
    {
        return _buttonSequenceManager?.GetCurrentButtonTransform();
    }

    public List<Transform> GetRemainingButtonTransforms()
    {
        return _buttonSequenceManager?.GetRemainingButtonTransforms() ?? new List<Transform>();
    }

    public Transform GetNearestExitTransform(Vector3 playerPosition)
    {
        ExitRegion[] exits = Object.FindObjectsByType<ExitRegion>();
        if (exits.Length == 0) return null;

        Transform closest = null;
        float minDistance = float.MaxValue;

        foreach (var exit in exits)
        {
            float dist = Vector2.Distance(playerPosition, exit.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = exit.transform;
            }
        }
        return closest;
    }

    public void TriggerCurrentButton()
    {
        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            TriggerButtonServerRpc();
        }
        else
        {
            // Singleplayer
            _buttonSequenceManager?.TriggerCurrentButton();
        }
    }

    [Rpc(SendTo.Server)]
    private void TriggerButtonServerRpc()
    {
        if (_buttonSequenceManager != null)
        {
            _buttonSequenceManager.TriggerCurrentButton();
            _netButtonProgress.Value = _buttonSequenceManager.CurrentIndex;
        }
    }

    private void SyncButtonProgressToCurrent(int targetIndex)
    {
        if (_buttonSequenceManager == null) return;
        
        // Đồng bộ hóa sequence cục bộ cho đến khi khớp với Server
        while (_buttonSequenceManager.CurrentIndex < targetIndex)
        {
            _buttonSequenceManager.TriggerCurrentButton();
        }
    }

    public void HaltMapTimelines()
    {
        _timelinesHalted = true;

        // Dừng tất cả các bộ quản lý Flood
        foreach (var flood in _floodManagers)
        {
            flood.StopFlood(); // Đảm bảo IFloodManager có phương thức Stop hoặc Pause
        }

        Debug.Log("[DevTool] All Map Timelines have been halted.");
    }

    /// <summary>
    /// Tạm dừng tất cả các nguồn nước trong map (Dùng cho Events/Buttons)
    /// </summary>
    public void PauseAllFloods(float duration)
    {
        foreach (var flood in _floodManagers)
        {
            flood.PauseFlood(duration);
        }
    }

    /// <summary>
    /// Điều chỉnh vị trí của tất cả nguồn nước dọc theo quỹ đạo của chúng.
    /// </summary>
    public void AdjustAllFloods(float offset)
    {
        foreach (var flood in _floodManagers)
        {
            flood.AdjustFloodPosition(offset);
        }
    }

    /// <summary>
    /// Được gọi từ ButtonSequenceManager thông qua UnityEvent
    /// </summary>
    private void OnAllButtonsActivated()
    {
        IsExitUnlocked = true;
        Debug.Log("Exit Region Unlocked!");
        // Có thể thêm âm thanh hoặc hiệu ứng thông báo mở cửa tại đây
    }

    /// <summary>
    /// Kiểm tra các sự kiện đã bị lỡ (dành cho người join muộn)
    /// </summary>
    private void CheckForMissedEvents()
    {
        if (NetworkManager.Singleton == null) return;

        double currentTime = NetworkManager.Singleton.ServerTime.Time - _netStartTime.Value;
        
        // Sắp xếp timeline theo thời gian để kích hoạt đúng thứ tự
        var sortedEvents = _mapTimelineEvents.OrderBy(e => e.TriggerTime).ToList();

        foreach (var timedEvent in sortedEvents)
        {
            // Nếu thời gian đã trôi qua điểm trigger, kích hoạt ngay lập tức (với bù trừ delay)
            if (!timedEvent.HasTriggered && currentTime >= timedEvent.TriggerTime)
            {
                ExecuteEvent(timedEvent, (float)currentTime);
                timedEvent.HasTriggered = true;
            }
        }
    }

    /// <summary>
    /// Duyệt qua timeline dựa trên thời gian thực tế của Server.
    /// </summary>
    private void ProcessTimeline()
    {
        double currentTime;
        // Bỏ check IsSpawned để đảm bảo luôn dùng ServerTime trong môi trường Multiplayer
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            currentTime = NetworkManager.Singleton.ServerTime.Time - _netStartTime.Value;
        }
        else
        {
            currentTime = Time.timeAsDouble - _localStartTime;
        }

        foreach (var timedEvent in _mapTimelineEvents)
        {
            if (!timedEvent.HasTriggered && currentTime >= timedEvent.TriggerTime)
            {
                ExecuteEvent(timedEvent, (float)currentTime);
                timedEvent.HasTriggered = true;
            }
        }
    }

    private void ExecuteEvent(TimedMapEvent timedEvent, float currentTime)
    {
        Debug.Log($"Map Timeline Event: Kích hoạt '{timedEvent.EventName}' tại {currentTime:F2}s.");
        timedEvent.OnTimeReached?.Invoke();

        foreach (var action in timedEvent.Actions)
        {
            if (action != null)
            {
                // Tính toán lại delay: Nếu join muộn, trừ bớt thời gian đã trôi qua
                float timeSinceTrigger = currentTime - timedEvent.TriggerTime;
                StartCoroutine(action.ExecuteRoutine(this, timeSinceTrigger));
            }
        }
    }

    private void CheckObjectsOutOfBound()
    {
        // Duyệt qua toàn bộ registry để tìm các object rơi quá sâu
        foreach (var registryList in _objectRegistry.Values)
        {
            for (int i = registryList.Count - 1; i >= 0; i--)
            {
                var obj = registryList[i];
                if (obj != null && obj.transform.position.y < _killYThreshold)
                {
                    // Chỉ hủy các object môi trường, không hủy Player ở đây (Player do GameplayManager quản lý)
                    if (!obj.CompareTag("Player")) Destroy(obj.gameObject);
                }
            }
        }

        // Nếu join muộn, kiểm tra ngay các sự kiện đã trôi qua
        if (IsSpawned && !IsServer) CheckForMissedEvents();
    }

    #region Object Registry System
    public void RegisterMapObject(string id, MonoBehaviour obj)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!_objectRegistry.ContainsKey(id)) _objectRegistry[id] = new List<MonoBehaviour>();
        if (!_objectRegistry[id].Contains(obj)) _objectRegistry[id].Add(obj);
    }

    public void UnregisterMapObject(string id, MonoBehaviour obj)
    {
        if (_objectRegistry.ContainsKey(id)) _objectRegistry[id].Remove(obj);
    }

    public List<T> GetMapObjectsByID<T>(string id) where T : class
    {
        if (!_objectRegistry.ContainsKey(id)) return new List<T>();
        
        // Lọc các object có chứa Component/Interface loại T
        List<T> results = new List<T>();
        foreach (var item in _objectRegistry[id])
        {
            if (item is T typedItem) results.Add(typedItem);
            else
            {
                var comp = item.GetComponent<T>();
                if (comp != null) results.Add(comp);
            }
        }
        return results;
    }
    #endregion
}
