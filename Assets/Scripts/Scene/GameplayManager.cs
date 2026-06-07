using UnityEngine;
using System.Collections;
using Core.Interfaces;
using Core.Events; // Thêm namespace cho Events
using Core;        // Tham chiếu Core để lấy LevelManager
using UnityEngine.InputSystem; // Sử dụng New Input System API
using System.Linq;
using Unity.Cinemachine; // Thêm namespace cho Cinemachine (nếu dùng bản mới, có thể cần Unity.Cinemachine)
using UnityEngine.EventSystems; // Cần thiết để kiểm tra click trên UI
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Quản lý vòng lặp chính của Gameplay: UI, Spawn Player, Win/Lose Condition.
/// </summary>
public class GameplayManager : MonoBehaviour, IGameplayManager, IGameLoopManager
{
    public static GameplayManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private GameObject _playerPrefab;
    [Tooltip("Object trống dùng để chứa Map sau khi Instantiate để giữ Hierarchy gọn gàng")]
    [SerializeField] private Transform _mapParent;
    [Tooltip("Kéo Virtual Camera từ Scene vào đây")]
    [SerializeField] private CinemachineCamera _vcam; 


    [Header("Game Settings")]
    [SerializeField] private int _startCountdownTime = 3;

    // State
    public bool IsGameActive { get; private set; } = false;
    public bool IsPaused { get; private set; } = false;
    public float CurrentLevelTime { get; private set; } = 0f;
    private float _levelProgressTime = 0f; // Thời gian thực tế của màn chơi (dừng khi Halt Timeline)
    
    // Thay đổi từ một Player duy nhất sang danh sách để quản lý Multiplayer
    public List<IPlayer> AllPlayers { get; private set; } = new List<IPlayer>();
    public IPlayer LocalPlayer { get; private set; }

    // IGameLoopManager implementation
    public bool IsHost => true; // SP luôn là host
    public bool IsMultiplayer => false; // SP không phải MP
    public IMapManager CurrentMapManager => _mapManager;

    private IGameplayUIManager _uiManager;
    private IMapManager _mapManager;

    [Header("Data Settings")]
    [SerializeField] private DifficultyPalette _difficultyPalette;
    [SerializeField] private AudioClip _loadingMusic;
    
    private bool _isInfAirOn = false;
    private bool _isInfJumpOn = false;
    private bool _isTeleportModeOn = false;
    private bool _timelinesHalted = false;
    // Lưu lại Map instance hiện tại để xóa khi restart/về home (nếu cần)
    private GameObject _currentMapInstance; 

    private float _maxLevelTime = 180f; // Sẽ lấy từ MapManager

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        // Tự động tìm UI Manager thông qua Interface (Giải quyết vấn đề khác Assembly)
        // Vì Scene.asmdef chỉ tham chiếu Core, nên nó thấy IGameplayUIManager chứ không thấy class GameplayUIManager
        if (_uiManager == null)
        {
            _uiManager = FindObjectsByType<Component>().OfType<IGameplayUIManager>().FirstOrDefault();
        }
    }

    private void OnEnable()
    {
        // Đăng ký lắng nghe sự kiện hoàn thành level
        GameplayEvents.OnLevelCompleted += OnLevelCompletedHandler; // Still handles for SP
        GameplayEvents.OnInfiniteAirToggleRequested += ToggleInfiniteAir;
        GameplayEvents.OnInfiniteJumpToggleRequested += ToggleInfiniteJump;
        GameplayEvents.OnTeleportToNextButtonRequested += TeleportToNextButton;
        GameplayEvents.OnTeleportModeToggleRequested += ToggleTeleportMode;
        GameplayEvents.OnHaltTimelinesRequested += HandleHaltTimelines;

        GameplayEvents.OnPauseRequested += SetPaused;
        GameplayEvents.OnRestartRequested += RestartLevel;
        GameplayEvents.OnBackToMenuRequested += BackToMainMenu;
    }

    private void OnDisable()
    {
        // Hủy đăng ký để tránh memory leak
        GameplayEvents.OnLevelCompleted -= OnLevelCompletedHandler;
        GameplayEvents.OnInfiniteAirToggleRequested -= ToggleInfiniteAir;
        GameplayEvents.OnInfiniteJumpToggleRequested -= ToggleInfiniteJump;
        GameplayEvents.OnTeleportToNextButtonRequested -= TeleportToNextButton;
        GameplayEvents.OnTeleportModeToggleRequested -= ToggleTeleportMode;
        GameplayEvents.OnHaltTimelinesRequested -= HandleHaltTimelines;

        GameplayEvents.OnPauseRequested -= SetPaused;
        GameplayEvents.OnRestartRequested -= RestartLevel;
        GameplayEvents.OnBackToMenuRequested -= BackToMainMenu;
    }

    private void Start()
    {
        // Khi scene Gameplay vừa load xong, bắt đầu khởi tạo level ngay
        StartLevel();
    }

    public void StartLevel()
    {
        Time.timeScale = 1f; // Đảm bảo thời gian chạy khi bắt đầu/restart
        AudioListener.pause = false; // Reset trạng thái âm thanh khi vào map mới
        IsPaused = false;

        // Reset các bộ đếm thời gian khi bắt đầu map mới
        CurrentLevelTime = 0f;
        _levelProgressTime = 0f;

        // Kiểm tra xem có MapData được truyền tới không
        if (LevelManager.SelectedMap != null)
        {
            // Điền thông tin map vào màn hình loading đang hiển thị sẵn
            if (_uiManager != null) _uiManager.SetupMapLoadingScreen(LevelManager.SelectedMap);

            // Tự động sinh Map ra Scene nếu chưa có
            _currentMapInstance = Instantiate(LevelManager.SelectedMap.MapPrefab, Vector3.zero, Quaternion.identity, _mapParent);
        }

        // 1. Tìm lại MapManager mới (vì Map vừa được Instantiate)
        _mapManager = FindObjectsByType<Component>().OfType<IMapManager>().FirstOrDefault();

        if (_mapManager == null)
        {
            Debug.LogError("[GameplayManager] Không tìm thấy MapManager trong Scene!");
            return;
        }

        // Load và hiển thị Best Time từ SaveSystem
        PlayerProfile profile = SaveSystem.LoadProfile();
        MapData currentData = _mapManager.GetMapData();
        if (currentData != null && _uiManager != null)
        {
            MapRecord record = profile.MapRecords.Find(r => r.MapName == currentData.Name);
            float bestTime = record != null ? record.BestTime : -1f;
            _uiManager.SetRecordTime(bestTime, _mapManager.GetMaxMapTime());
        }

        _timelinesHalted = false;
        _uiManager?.UpdateHaltTimelinesStatus(false);

        // Reset trạng thái Dev khi vào map mới
        _isInfAirOn = false;
        _isInfJumpOn = false;
        _isTeleportModeOn = false;

        // 1.5. Hiển thị DevTool nếu map cho phép
        if (_uiManager != null && _mapManager != null)
        {
            _uiManager.ShowDevTools(_mapManager.IsDevToolEnabled());
        }

        // 2. Bắt đầu quy trình vào game
        StartCoroutine(GameStartSequence());
    }

    private void Update()
    {
        // Kiểm tra phím Pause (Esc)
        // Cho phép Pause nếu map đã load (đang countdown hoặc đang chơi)
        if (_currentMapInstance != null && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            GameplayEvents.TriggerPauseRequest(!IsPaused);
        }

        if (!IsGameActive || IsPaused) return;

        // 1. Tính giờ cá nhân (Personal Time) - Luôn chạy để đo tổng thời gian người chơi ở trong map
        CurrentLevelTime += Time.deltaTime;

        // 2. Xử lý logic tiến trình màn chơi (Dừng lại khi Halt Timeline)
        if (!_timelinesHalted)
        {
            _levelProgressTime += Time.deltaTime;

            // Check Thua: Hết giờ dựa trên thời gian thực tế của màn chơi
            if (_levelProgressTime >= _maxLevelTime)
            {
                GameOver("Exceeded Max Time!", DeathReason.TimeOut);
            }
        }

        // 3. Cập nhật UI HUD
        if (_uiManager != null)
        {
            _uiManager.UpdatePersonalRecord(CurrentLevelTime);
            _uiManager.UpdateTimeSlider(_levelProgressTime, _maxLevelTime); 
            UpdateLocalPlayerAirUI();

            // Cập nhật số lượng player còn sống (SP: 1 hoặc 0)
            int aliveCount = (LocalPlayer != null && !LocalPlayer.IsDead) ? 1 : 0;
            _uiManager.UpdateAlivePlayerCount(aliveCount, 1);
            
            UpdateButtonProgressUI(); // Thêm cập nhật số nút
        }

        // 5. Xử lý Teleport theo vị trí chuột (chỉ khi chế độ này đang bật)
        // Sử dụng Mouse.current từ Input System thay vì Input.GetMouseButtonDown
        if (_isTeleportModeOn && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Kiểm tra xem chuột có đang đè lên UI không. Nếu có thì bỏ qua không teleport.
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                HandleManualTeleport();
            }
        }

        // 4. Check Thua: Player chết
        if (LocalPlayer != null && LocalPlayer.IsDead)
        {
            GameOver("You're Drowned!", LocalPlayer.LastDeathReason);
        }

        // 4.5. Check Thua: Player rơi xuống vực (Void Death)
        if (LocalPlayer != null && !LocalPlayer.IsDead && _mapManager != null)
        {
            if (((Component)LocalPlayer).transform.position.y < _mapManager.GetKillYThreshold())
            {   
                GameOver("You fell off the world!", DeathReason.FellOffWorld);
            }
        }
    }
    
    private void ToggleInfiniteAir()
    {
        _isInfAirOn = !_isInfAirOn; // Đảo trạng thái (Toggle)
        
        // Gọi trực tiếp qua Interface IPlayer (nằm trong Core nên Scene assembly có thể thấy)
        LocalPlayer?.SetInfiniteAir(_isInfAirOn);
        
        // Cập nhật lại chữ trên nút (On/Off)
        _uiManager?.UpdateInfiniteAirStatus(_isInfAirOn);
        
        Debug.Log($"DevTool: Infinite Air is now {(_isInfAirOn ? "ON" : "OFF")}");
    }

    private void ToggleInfiniteJump()
    {
        _isInfJumpOn = !_isInfJumpOn;
        
        // Sử dụng phương thức phản chiếu hoặc ép kiểu interface (tương tự SetInfiniteAir)
        LocalPlayer?.SetInfiniteJump(_isInfJumpOn);
        
        _uiManager?.UpdateInfiniteJumpStatus(_isInfJumpOn);
        
        Debug.Log($"DevTool: Infinite Jump is now {(_isInfJumpOn ? "ON" : "OFF")}");
    }

    private void TeleportToNextButton()
    {
        // TODO: Trong Multiplayer, cần kiểm tra IsServer hoặc IsHost trước khi thực hiện
        if (LocalPlayer == null) return;

        // 1. Nếu đang Cling trên tường: Không cho phép làm gì để bảo vệ physics
        if (LocalPlayer.IsClinging)
        {
            Debug.Log("[DevTool] Teleport blocked: Player is currently clinging to a wall.");
            return;
        }

        // 2. Nếu đang Zipline: Chỉ kích hoạt nút, không teleport
        if (LocalPlayer.IsZiplining)
        {
            _mapManager?.TriggerCurrentButton();
            Debug.Log("[DevTool] Player is Ziplining: Triggered button without teleport.");
            return;
        }

        // 3. Trường hợp bình thường: Teleport như cũ
        Transform target = _mapManager?.GetNextButtonTransform();
        if (target != null)
        {
            LocalPlayer.Teleport(target.position);
            CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);
        }
        else Debug.LogWarning("[DevTool] Cannot teleport: No active button found or sequence complete.");
    }
/// <summary>
    /// Dịch chuyển camera tức thời đến vị trí của Local Player.
    /// </summary>
    private void WarpCamera()
    {
        if (_vcam != null && LocalPlayer is MonoBehaviour playerMono)
        {
            Vector3 targetPos = playerMono.transform.position;
            targetPos.z = _vcam.transform.position.z; // Giữ nguyên độ sâu Z của camera
            _vcam.ForceCameraPosition(targetPos, _vcam.transform.rotation);
        }
    }
    public void SetPaused(bool paused)
    {
        // Chỉ cho phép pause khi đã vào màn chơi (tránh pause lúc đang loading)
        if (_currentMapInstance == null) return;

        IsPaused = paused;
        // Dừng hoặc tiếp tục thời gian vật lý
        Time.timeScale = paused ? 0f : 1f;

        // Tạm dừng/Tiếp tục toàn bộ âm thanh gameplay (SFX)
        AudioListener.pause = paused;

        // Gọi trực tiếp qua interface IGameplayUIManager
        _uiManager?.ShowPauseMenu(paused);
    }

    public void RestartLevel()
    {
        StartCoroutine(RestartLevelRoutine());
    }

    private IEnumerator RestartLevelRoutine()
    {
        // Phát nhạc Loading khi Restart với fade nhanh
        if (BackgroundMusicManager.Instance != null)
            BackgroundMusicManager.Instance.FadeTo(_loadingMusic, 0.25f);

        // 1. Hiển thị Loading UI ngay tại scene hiện tại trước khi reload
        if (_uiManager != null && LevelManager.SelectedMap != null)
        {
            _uiManager.ShowLoadingScreen(true);
            _uiManager.SetupMapLoadingScreen(LevelManager.SelectedMap);
        }

        Time.timeScale = 1f;
        yield return new WaitForEndOfFrame(); // Đảm bảo UI kịp render một frame

        // 2. Load lại Scene hiện tại không đồng bộ
        AsyncOperation op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().name);
        while (!op.isDone) yield return null;
    }

    public void BackToMainMenu()
    {
        // Reset các trạng thái toàn cục trước khi rời khỏi Gameplay
        AudioListener.pause = false;
        Time.timeScale = 1f;

        StartCoroutine(BackToMainMenuRoutine());
    }

    private IEnumerator BackToMainMenuRoutine()
    {
        // Phát nhạc Loading khi quay về Home
        if (BackgroundMusicManager.Instance != null)
            BackgroundMusicManager.Instance.FadeTo(_loadingMusic, 0.25f);
        
        // 1. Hiển thị Loading Screen của Gameplay HUD trước khi thoát
        _uiManager?.ShowBackToMainMenuLoadingScreen();

        Time.timeScale = 1f; // Reset thời gian để quá trình load không bị đứng (nếu đang pause)
        LevelManager.ReturnToMapSelection = true;
        yield return new WaitForEndOfFrame(); // Đợi UI render xong Loading Screen

        // 2. Load Home scene không đồng bộ
        AsyncOperation op = SceneManager.LoadSceneAsync("Home");
        while (!op.isDone) yield return null;
    }

    private void ToggleTeleportMode()
    {
        _isTeleportModeOn = !_isTeleportModeOn;
        _uiManager?.UpdateTeleportModeStatus(_isTeleportModeOn);
        Debug.Log($"DevTool: Teleport Mode is now {(_isTeleportModeOn ? "ON" : "OFF")}");
    }

    private void HandleHaltTimelines()
    {
        if (_timelinesHalted) return; // Đảm bảo tính 1 chiều: Nếu đã dừng thì không xử lý lại

        _timelinesHalted = true;
        _uiManager?.UpdateHaltTimelinesStatus(true);
    }

    private void HandleManualTeleport()
    {
        // Chỉ cho phép Local Player (hoặc Admin) Teleport chính mình
        if (LocalPlayer == null || LocalPlayer.IsClinging || LocalPlayer.IsZiplining || LocalPlayer.IsClimbing)
        {
            if (LocalPlayer != null && (LocalPlayer.IsClinging || LocalPlayer.IsZiplining || LocalPlayer.IsClimbing))
                Debug.Log("[DevTool] Teleport blocked: Player is currently clinging or ziplining.");
            return;
        }

        // Lấy vị trí chuột từ Input System
        Vector2 mousePos = Mouse.current.position.ReadValue();
        // Chuyển từ tọa độ Screen sang World. Z là khoảng cách từ Camera đến mặt phẳng 2D (thường là 10)
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, Mathf.Abs(Camera.main.transform.position.z)));
        worldPos.z = 0; // Đảm bảo luôn nằm trên mặt phẳng 2D

        // CẢI TIẾN: Thay vì OverlapCircle tại 1 điểm, ta kiểm tra một vùng diện tích 
        // tương đương với kích thước Player (khoảng 0.6x1.5).
        // Việc này giúp ngăn chặn triệt để việc teleport vào giữa các khối sử dụng CompositeCollider2D,
        // vì nó kiểm tra xem "thân hình" Player có bị đè lên vật cản nào không.
        Vector2 checkSize = new Vector2(0.6f, 1.5f);
        Collider2D hit = Physics2D.OverlapBox(worldPos, checkSize, 0f, LayerMask.GetMask("Ground", "Default"));

        if (hit != null && !hit.isTrigger)
        {
            Debug.LogWarning("[DevTool] Teleport blocked: Target area overlaps with a solid object.");
            return;
        }

        LocalPlayer.Teleport(worldPos);
        CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);
    }

    private IEnumerator GameStartSequence()
    {
        // Bước 1: Spawn Player tại vị trí do MapManager cung cấp
        SpawnPlayer();

        // Đợi thêm 1 frame để đảm bảo hệ thống vật lý và camera đã ổn định
        yield return new WaitForEndOfFrame();
        CameraHelper.WarpToTarget(_vcam, LocalPlayer as MonoBehaviour);

        if (_uiManager != null)
        {
            _uiManager.ShowLoadingScreen(false);
        }

        // Bước 2: Khóa điều khiển
        foreach (var p in AllPlayers)
        {
            p.DisableAbility();
        }

        // Reset Parallax mốc (0,0,0) cho Map vừa Instantiate
        // Gọi tại đây để Camera ổn định vị trí trước khi tính Delta Parallax
        if (_mapManager != null) _mapManager.PrepareMapBackgrounds();

        // Bước 3: Đếm ngược
        float timeLeft = _startCountdownTime;
        while (timeLeft > 0)
        {
            if (_uiManager != null) _uiManager.SetCountdownText($"Get ready: {timeLeft:F0}");
            yield return new WaitForSeconds(1f);
            timeLeft--;
        }

        // Xóa text đếm ngược ngay lập tức thay vì hiện chữ GO!
        if (_uiManager != null) _uiManager.SetCountdownText("");

        // Bước 4: Bắt đầu game
        IsGameActive = true;
        
        foreach (var p in AllPlayers)
        {
            p.EnableAbility();
            p.SetStatus(PlayerStatus.InGame);
        }
        
        // QUẢN LÝ NHẠC: Chuyển từ nhạc Loading sang nhạc Map khi game bắt đầu
        if (BackgroundMusicManager.Instance != null)
        {
            AudioClip mapMusic = _mapManager?.GetMapMusic();
            BackgroundMusicManager.Instance.FadeTo(mapMusic, 0.5f);
        }

        // Báo cho MapManager biết để kích hoạt nhạc, nước, event
        if (_mapManager != null) _mapManager.StartMapMechanics();
    }

    private void SpawnPlayer()
    {
        if (_playerPrefab != null && _mapManager != null)
        {
            // 1. Lấy vị trí sàn từ Map (điểm ngẫu nhiên trên line spawn)
            Vector3 spawnPos = _mapManager.GetPlayerSpawnPosition();

            // 2. Tính toán offset để chân chạm đất (thay vì Pivot nằm giữa sàn)
            float footOffset = GetPivotToFeetOffset(_playerPrefab);
            Vector3 adjustedSpawnPos = spawnPos + new Vector3(0, footOffset, 0);

            GameObject playerObj = Instantiate(_playerPrefab, adjustedSpawnPos, Quaternion.identity);
            {
                IPlayer player = playerObj.GetComponent<IPlayer>();
                
                // Thiết lập hướng mặt ban đầu dựa trên cấu hình của PlayerSpawn
                var spawnPoint = FindObjectsByType<PlayerSpawn>().FirstOrDefault(s => s.IsMapSpawn);
                if (spawnPoint != null)
                {
                    player.SetFacing(spawnPoint.IsFacingRight);
                }

                // Trong Multiplayer, mỗi Client chỉ gán LocalPlayer cho Object mà họ sở hữu
                LocalPlayer = player; 
                AllPlayers.Add(player);

                // TRONG MULTIPLAYER: Chỉ gán camera cho Local Player của máy này
                // Hiện tại logic đang mặc định player vừa spawn là LocalPlayer
                if (_vcam != null && LocalPlayer == player)
                {
                    // Đặt Priority mặc định cho Player Camera
                    _vcam.Priority = 10;
                    _vcam.Follow = playerObj.transform;
                    _vcam.LookAt = playerObj.transform;
                }
                else Debug.LogWarning("[GameplayManager] Chưa gán Virtual Camera vào Inspector!");
                // Thông báo cho toàn hệ thống biết Local Player đã sẵn sàng
                GameplayEvents.TriggerLocalPlayerSpawned(player);
            }
        }
        else
        {
            Debug.LogError("[GameplayManager] Missing Player Prefab or MapManager!");
        }
    }

    /// <summary>
    /// Tính toán khoảng cách từ Pivot (tâm) của Prefab đến điểm thấp nhất của Collider.
    /// Giúp Player spawn 'đứng trên mặt đất' thay vì bị lún Pivot xuống sàn.
    /// </summary>
    private float GetPivotToFeetOffset(GameObject prefab)
    {
        // Lấy tất cả collider trên prefab (bao gồm cả các object con như chân, tay...)
        Collider2D[] colliders = prefab.GetComponentsInChildren<Collider2D>();
        float minLocalY = 0f;
        bool foundValidCollider = false;

        foreach (var col in colliders)
        {
            // Bỏ qua các trigger (như vùng check bơi, vùng tương tác) vì chúng không đại diện cho "chân"
            if (col.isTrigger) continue;

            float localYOffset = col.transform.localPosition.y;
            float currentBottom = 0f;

            if (col is BoxCollider2D box)
                currentBottom = localYOffset + box.offset.y - (box.size.y / 2f);
            else if (col is CircleCollider2D circle)
                currentBottom = localYOffset + circle.offset.y - circle.radius;
            else if (col is CapsuleCollider2D capsule)
                currentBottom = localYOffset + capsule.offset.y - (capsule.size.y / 2f);
            else continue;

            if (!foundValidCollider || currentBottom < minLocalY)
            {
                minLocalY = currentBottom;
                foundValidCollider = true;
            }
        }

        // Trả về giá trị dương để đẩy Player lên (ví dụ đáy ở -1.2 thì đẩy lên 1.2)
        return -minLocalY;
    }

    private void UpdateLocalPlayerAirUI()
    {
        // The CurrentPlayer (IPlayer) already has the required properties.
        if (LocalPlayer != null)
        {
            _uiManager.UpdateAirUI(
                LocalPlayer.CurrentBaseAir,
                LocalPlayer.CurrentBonusAir,
                LocalPlayer.CurrentBonusAirMax,
                LocalPlayer.CurrentAirChangeRate
            );
        }
    }

    private void UpdateButtonProgressUI()
    {
        if (_mapManager != null)
        {
            // Yêu cầu MapManager cung cấp thông tin về nút bấm
            int current = _mapManager.GetButtonsActivatedCount();
            int total = _mapManager.GetTotalButtonsCount();
            _uiManager.UpdateButtonProgress(current, total);
        }
    }

    private void OnLevelCompletedHandler(IPlayer playerWhoCompleted)
    {
        if (!IsGameActive) return;
        IsGameActive = false;
        Time.timeScale = 1f; // Tránh treo game ở màn hình Victory

        _uiManager?.ShowPlayerFinishFlag(true);
        _uiManager?.SetPersonalTimeHighlight(true);

        foreach (var p in AllPlayers)
        {
            p.SetInvincible(true);
        }

        StartCoroutine(LevelCompletedRoutine());
    }

    private IEnumerator LevelCompletedRoutine()
    {
        // 1. Thu thập dữ liệu ngay tại thời điểm thắng
        MapData data = _mapManager.GetMapData(); // Lấy MapData từ MapManager
        float finalTime = CurrentLevelTime;
        int currentButtons = _mapManager != null ? _mapManager.GetButtonsActivatedCount() : 0;
        int totalButtons = _mapManager != null ? _mapManager.GetTotalButtonsCount() : 0;

        // 2. Logic kiểm tra New Best Time (Sử dụng PlayerPrefs để lưu trữ đơn giản)
        // Thay đổi sang dùng PlayerProfile
        bool isNewBest = false;
        bool isFirstWin = false;
        int earnedCoins = 0;

        if (data != null)
        {
            PlayerProfile profile = DataManager.Instance.Profile;
            MapRecord record = profile.MapRecords.Find(r => r.MapName == data.Name);

            if (record == null)
            {
                isFirstWin = true;
                record = new MapRecord { MapName = data.Name, BestTime = finalTime, WinCount = 1 };
                profile.MapRecords.Add(record);
                isNewBest = true;
            }
            else
            {
                record.WinCount++;
                if (finalTime < record.BestTime)
                {
                    record.BestTime = finalTime;
                    isNewBest = true;

                    // Nếu phá kỷ lục trên một map đã từng thắng (không phải lần đầu)
                    if (!isFirstWin)
                    {
                        profile.TotalRecordsBroken++;
                    }
                }
            }

            // Cập nhật thống kê độ khó
            // Sử dụng Palette để xác định Tier chính xác (Easy, Normal, Hard, Insane, Crazy, CrazyPlus)
            if (_difficultyPalette != null)
            {
                DifficultyPalette.Tier tier = _difficultyPalette.GetTierFromRating(data.Difficulty);
                
                // Tính tiền xu: Lần đầu 100%, các lần sau 50%
                int baseReward = _difficultyPalette.GetRewardForTier(tier);
                earnedCoins = isFirstWin ? baseReward : Mathf.FloorToInt(baseReward * 0.5f);
                
                profile.IncrementWin(tier, isFirstWin, earnedCoins);
            }

            DataManager.Instance.SaveData();
        }

        // Hiển thị thông báo Win màu xanh lá
        _uiManager?.ShowFloatNotification($"Completed {data.Name}!", Color.green, 2f);

        // 3. Đợi 1 giây trước khi hiện bảng
        yield return new WaitForSeconds(1f);

        // 4. Hiển thị UI EndGame với trạng thái thắng
        if (_uiManager != null)
        {
            _uiManager.ShowEndGame(true, "Map Completed", data, finalTime, currentButtons, isNewBest, earnedCoins);
        }
    }

    public void GameOver(string reason, DeathReason deathReason)
    {
        if (!IsGameActive) return;
        IsGameActive = false;
        Time.timeScale = 1f; // Tránh treo game ở màn hình Game Over

        Debug.Log($"GAME OVER: {reason}");

        // Kill player visuals
        foreach (var p in AllPlayers)
        {
            if (!p.IsDead) p.Die(deathReason); // Ensure all players are marked as dead with the correct reason
        }

        StartCoroutine(GameOverRoutine(reason, deathReason));
    }

    private IEnumerator GameOverRoutine(string reason, DeathReason deathReason)
    {
        // 1. Thu thập dữ liệu ngay tại thời điểm thua
        MapData data = _mapManager != null ? _mapManager.GetMapData() : null;
        float finalTime = CurrentLevelTime;
        int currentButtons = _mapManager != null ? _mapManager.GetButtonsActivatedCount() : 0;

        // Determine notification color and if it should be shown
        Color notificationColor = Color.red; // Default to red
        bool showNotification = true;

        if (deathReason == DeathReason.Explosion)
        {
            showNotification = false; // No notification for explosion
        }
        else if (deathReason == DeathReason.TimeOut)
        {
            notificationColor = Color.yellow;
        }

        // 2. Đợi 2 giây trước khi xử lý tiếp
        // Hiển thị thông báo Lose màu đỏ
        if (showNotification)
        {
            _uiManager?.ShowFloatNotification(reason, notificationColor, 2f);
        }

        yield return new WaitForSeconds(2f);

        // 3. Hiện UI hoặc Auto Restart
        if (_uiManager != null) 
        {
            // Kiểm tra tính năng Auto Restart
            if (SettingsManager.Instance != null && SettingsManager.Instance.AutoRestart)
            {
                RestartLevel();
            }
            else
            {
                _uiManager.ShowEndGame(false, reason, data, finalTime, currentButtons, false, 0); // No coins for losing
            }
        }
    }
}
