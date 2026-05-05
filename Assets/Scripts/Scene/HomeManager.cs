using UnityEngine;
using System.Collections;
using System; // Cần thêm để dùng Action
using Core; // Tham chiếu đến namespace Core để dùng MapData
using Core.Interfaces;
using System.Linq;
using UnityEngine.SceneManagement; // Thêm namespace để chuyển scene

/// <summary>
/// HomeManager chịu trách nhiệm quản lý tất cả logic liên quan đến scene Home, bao gồm:
/// - Phát nhạc nền Menu và Loading
/// - Xử lý sự kiện khi người chơi chọn map (LoadLevel)
/// - Giao tiếp với UI Manager để hiển thị Loading Screen và thông tin map
/// </summary>
public class HomeManager : MonoBehaviour, ILevelLoader
{
    public static HomeManager Instance { get; private set; }

    [Header("Audio")]
    [SerializeField] private AudioClip _menuMusic;
    [SerializeField] private AudioClip _mapSelectSound;
    [SerializeField] private AudioClip _loadingMusic;

    private IHomeUIManager _uiManager;

    // Cache WaitForSeconds để tối ưu hiệu suất (UNT0038)
    private readonly WaitForSeconds _loadDelay = new WaitForSeconds(0.5f);

    // Event bắn ra khi bắt đầu load level. UI sẽ lắng nghe cái này.
    public event Action OnLevelLoadStarted;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // Reset trạng thái âm thanh và thời gian khi quay về Home (đề phòng thoát khi đang Pause)
        AudioListener.pause = false;
        Time.timeScale = 1f;

        // Tìm UI Manager thông qua Interface để tránh lỗi khác Assembly
        if (_uiManager == null)
        {
            _uiManager = FindObjectsByType<Component>(FindObjectsSortMode.None).OfType<IHomeUIManager>().FirstOrDefault();
        }

        PlayMenuMusic();
    }

    private void PlayMenuMusic()
    {
        // Gọi BackgroundMusicManager từ Core để phát nhạc Menu
        if (BackgroundMusicManager.Instance != null && _menuMusic != null)
        {
            // Đảm bảo không còn theo dõi player nào khi ở Menu
            BackgroundMusicManager.Instance.SetPlayer(null);
            
            AudioSource source = BackgroundMusicManager.Instance.GetAudioSource();
            source.clip = _menuMusic;
            source.loop = true;
            source.Play();
        }
    }

    public void LoadLevel(MapData mapData)
    {
        if (mapData == null || mapData.MapPrefab == null)
        {
            Debug.LogError("Map Data hoặc Prefab bị thiếu!");
            return;
        }

        // 1. Phát âm thanh chọn map thông qua UI Manager
        // Sử dụng PlayCustomSound để đảm bảo SFX phát trên kênh UI, không bị ảnh hưởng khi dừng nhạc nền
        if (_uiManager != null && _mapSelectSound != null)
        {
            _uiManager.PlayCustomSound(_mapSelectSound);
        }

        // 2. Chuyển sang nhạc Loading
        if (BackgroundMusicManager.Instance != null && _loadingMusic != null)
        {
            AudioSource source = BackgroundMusicManager.Instance.GetAudioSource();
            source.clip = _loadingMusic;
            source.loop = true;
            source.Play();
        }

        StartCoroutine(LoadLevelRoutine(mapData));
    }

    private IEnumerator LoadLevelRoutine(MapData mapData)
    {
        // 1. Kích hoạt Loading UI tại Scene Home
        OnLevelLoadStarted?.Invoke();
        
        if (_uiManager != null)
        {
            _uiManager.ShowLoadingScreen(true);
            _uiManager.SetupLoadingScreen(mapData); // Thiết lập thông tin ngay tại scene Home
        }
        
        // Đợi 1-2 frame để UI chắc chắn đã hiển thị trên màn hình trước khi CPU bị chiếm dụng
        yield return new WaitForEndOfFrame();

        // 2. Lưu map dữ liệu
        LevelManager.SelectedMap = mapData;

        // 3. Nạp Scene không đồng bộ (Async)
        AsyncOperation operation = SceneManager.LoadSceneAsync("Gameplay");
        
        while (!operation.isDone)
        {
            yield return null;
        }
    }
}