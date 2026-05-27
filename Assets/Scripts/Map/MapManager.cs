using UnityEngine;
using System.Collections;
using System.Collections.Generic; // Dùng List
using TMPro; // Nếu bạn muốn hiển thị timer lên UI
using Core.Interfaces; // Dùng Interface thay vì class cụ thể
using UnityEngine.Events; // Thêm namespace để dùng UnityEvent
using Core;
using Core.Events;

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
public class MapManager : MonoBehaviour, IMapManager
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

    // MapManager giờ chỉ quản lý timeline nội bộ
    private float _mapLocalTime = 0f; 
    private bool _isMapActive = false;
    private bool _timelinesHalted = false;
    private bool _isPaused = false;
    
    private List<IFloodManager> _floodManagers = new List<IFloodManager>();
    private IButtonSequenceManager _buttonSequenceManager;

    // Registry lưu trữ các object theo ID để truy cập nhanh (O(1))
    private Dictionary<string, List<MonoBehaviour>> _objectRegistry = new Dictionary<string, List<MonoBehaviour>>();

    private void Awake()
    {
        // Singleton Pattern đơn giản
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

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

        // 1. Kiểm tra số lượng nút bấm
        // Lưu ý: Cách đếm này phụ thuộc vào việc ButtonSequenceManager quản lý nút như thế nào.
        // Ở đây ta giả định ButtonSequenceManager quản lý các nút là con của nó hoặc có list.
        // Nếu bạn dùng interface IButtonSequenceManager, bạn có thể cần cast về class cụ thể hoặc thêm property Count vào Interface.
        // Đây là ví dụ đếm số lượng object con có component IButtonTrigger trong manager object
        /*
        int actualButtonCount = _buttonSequenceManagerObject.GetComponentsInChildren<IButtonTrigger>().Length;
        if (actualButtonCount != _mapData.ButtonNumber)
        {
            Debug.LogWarning($"[Map Data Mismatch] MapData khai báo {_mapData.ButtonNumber} nút, nhưng tìm thấy {actualButtonCount} nút trong scene!");
        }
        */

        // 2. Cảnh báo nếu MapData có nhạc nhưng AudioSource không tìm thấy
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

    private void Update()
    {
        // Nếu map chưa active hoặc timeline đã bị Halt thì không chạy tiếp
        if (!_isMapActive || _timelinesHalted || _isPaused) return;

        // MapManager chỉ quan tâm đến timeline sự kiện của Map
        _mapLocalTime += Time.deltaTime;

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
        _isMapActive = true;
        _mapLocalTime = 0f;
        
        // Reset timeline
        foreach (var timedEvent in _mapTimelineEvents)
        {
            timedEvent.HasTriggered = false;
        }

        // CẢI TIẾN: Chỉ bắt đầu dâng nước nếu Timeline không bị tạm dừng (Halt)
        // Điều này ngăn việc Flood tự khởi động nếu người chơi đã bấm Halt trong lúc đếm ngược.
        if (!_timelinesHalted)
        {
            foreach (var flood in _floodManagers)
            {
                flood.StartFlood();
            }
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

    /// <summary>
    /// Trả về nhạc nền được cấu hình cho Map này.
    /// </summary>
    public AudioClip GetMapMusic() => _mapData != null ? _mapData.BackgroundMusic : null;

    public float GetMaxMapTime() => _mapData != null ? _mapData.MapDuration : 300f;

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
        _buttonSequenceManager?.TriggerCurrentButton();
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
    /// Duyệt qua danh sách các sự kiện trong timeline và kích hoạt chúng nếu đến giờ.
    /// </summary>
    private void ProcessTimeline()
    {
        foreach (var timedEvent in _mapTimelineEvents)
        {
            if (!timedEvent.HasTriggered && _mapLocalTime >= timedEvent.TriggerTime)
            {
                Debug.Log($"Map Timeline Event: Kích hoạt '{timedEvent.EventName}' tại {_mapLocalTime:F2}s.");
                timedEvent.OnTimeReached?.Invoke();
                
                // Thực thi danh sách Actions
                foreach (var action in timedEvent.Actions)
                {
                    if (action != null) StartCoroutine(ExecuteDelayedAction(action, this));
                }

                timedEvent.HasTriggered = true;
            }
        }
    }

    private IEnumerator ExecuteDelayedAction(MapAction action, IMapManager manager)
    {
        if (action == null) yield break;
        if (action.Delay > 0) yield return new WaitForSeconds(action.Delay);
        action.Execute(manager);
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
