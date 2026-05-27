using UnityEngine;
using Core;
using Core.Interfaces;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Quản lý giao diện người dùng chính của Home Screen, bao gồm:
/// - Home Screen
/// - Map Selection Screen
/// - Multiplayer Screen
/// - Loading Screen (với thông tin chi tiết về map đang tải)
/// - Các màn hình động như Progress và Character (sinh ra từ Prefab)
/// Cũng chịu trách nhiệm phát âm thanh SFX cho các tương tác UI và cung cấp phương thức để mở Settings Modal.
/// </summary>
namespace UI
{
    public class HomeUIManager : MonoBehaviour, IHomeUIManager
{
    public static HomeUIManager Instance { get; private set; }

    [Header("Screens")]
    [SerializeField] private GameObject _homeScreen;
    [SerializeField] private GameObject _mapSelectionScreen; // Vẫn là màn hình tĩnh
    [SerializeField] private GameObject _loadingScreen; // Panel Loading overlay
    [SerializeField] private GameObject _joiningRoomLoadingScreen; // Panel Loading khi join game (tách biệt để dễ quản lý)

    [Header("Dynamic Screens (Prefabs)")]
    [SerializeField] private GameObject _progressPrefab;
    [SerializeField] private GameObject _characterPrefab;
    [SerializeField] private GameObject _multiplayerPrefab; // Multiplayer giờ là Prefab
    [SerializeField] private Transform _uiCanvasRoot;

        [Header("Global Modals")]
        [SerializeField] private NotificationModalUI _notificationPrefab;
        [SerializeField] private ConfirmationModalUI _confirmationPrefab;
        
        private NotificationModalUI _notificationInstance;
        private ConfirmationModalUI _confirmationInstance;

    [Header("Loading Screen Details")]
    [SerializeField] private Image _loadingPreviewImage;
    [Tooltip("Định dạng: <Tên map> [<Tier>] - <Tác giả>")]
    [SerializeField] private TMP_Text _loadingMapNameAuthorText; 
    [SerializeField] private TMP_Text _loadingStatusText; // Text "Loading..."
    [SerializeField] private TMP_Text _loadingButtonCountText;
    [SerializeField] private TMP_Text _loadingDifficultyText;

    [Header("Settings")]
    [SerializeField] private DifficultyPalette _palette;

    [Header("Audio SFX")]
    [SerializeField] private AudioClip _clickSound;
    [SerializeField] private AudioSource _uiAudioSource;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // FIX: Kiểm tra cờ ngay từ Awake để tránh hiện tượng nháy (flicker) màn hình Home
        // trước khi chuyển sang Map Selection
        if (LevelManager.ReturnToMapSelection)
        {
            ShowMapSelectionScreen();
            LevelManager.ReturnToMapSelection = false; // Reset cờ ngay
        }
        else
        {
            ShowHomeScreen();
        }

        if (_loadingScreen != null) _loadingScreen.SetActive(false);
    }

    private void Start()
    {
        // Lắng nghe sự kiện từ LevelManager trong Core
        LevelManager.OnLevelLoadStarted += HideAllScreens;
    }

    private void OnDestroy()
    {
        // Hủy đăng ký
        LevelManager.OnLevelLoadStarted -= HideAllScreens;
    }

    public void ShowHomeScreen()
    {
        SwitchScreen(_homeScreen);
    }

    public void ShowMapSelectionScreen()
    {
        SwitchScreen(_mapSelectionScreen);
    }

    public void ShowMultiplayerScreen()
    {
        if (_multiplayerPrefab == null) return;

        // 1. Ẩn tất cả các màn hình hiện tại (HomeScreen, v.v.)
        HideAllScreens();

        // 2. Sinh ra Multiplayer Screen
        GameObject multiplayerObj = Instantiate(_multiplayerPrefab, _uiCanvasRoot);
        
        // Đảm bảo UI khớp hoàn toàn với màn hình
        RectTransform rect = multiplayerObj.GetComponent<RectTransform>();
        if (rect != null)
        {
            SetRectTransformToFullScreen(rect);
        }
    }

    /// <summary>
    /// Mở màn hình Progress bằng cách sinh ra Prefab (Destroy on Close).
    /// </summary>
    public void ShowProgressScreen()
    {
        if (_progressPrefab == null) return;

        // 1. Ẩn tất cả các màn hình hiện tại (HomeScreen, v.v.) giống như MapSelection
        HideAllScreens();

        // 2. Sinh ra Progress Screen
        GameObject progressObj = Instantiate(_progressPrefab, _uiCanvasRoot);
        
        SetRectTransformToFullScreen(progressObj.GetComponent<RectTransform>());
    }

    public void ShowCharacterScreen()
    {
        if (_characterPrefab == null) return;

        // 1. Ẩn màn hình chính
        HideAllScreens();

        // 2. Sinh ra Character Screen
        GameObject characterObj = Instantiate(_characterPrefab, _uiCanvasRoot);
        
        SetRectTransformToFullScreen(characterObj.GetComponent<RectTransform>());
    }

    private void SwitchScreen(GameObject targetScreen)
    {
        if (targetScreen == null) return;
        
        HideAllScreens();
        targetScreen.SetActive(true);
    }

    public void ShowLoadingScreen(bool show)
    {
        if (_loadingScreen != null) _loadingScreen.SetActive(show);
    }

    public void ShowJoiningGameLoadingScreen(bool show)
    {
        if (_joiningRoomLoadingScreen != null) _joiningRoomLoadingScreen.SetActive(show);
    }

    public void SetupLoadingScreen(MapData data)
    {
        if (data == null) return;

        if (_loadingPreviewImage != null) _loadingPreviewImage.sprite = data.MapPreviewImage;

        // Lấy màu từ Palette dựa trên độ khó
        Color themeColor = _palette != null ? _palette.GetColorFromRating(data.Difficulty) : Color.white;
        if (_loadingStatusText != null) _loadingStatusText.color = themeColor;

        if (_loadingMapNameAuthorText != null)
        {
            // Lấy tên Tier từ Palette
            string tierName = "Unknown";
            if (_palette != null)
            {
                var tier = _palette.GetTierFromRating(data.Difficulty);
                tierName = (tier == DifficultyPalette.Tier.CrazyPlus) ? "Crazy+" : tier.ToString();
            }

            _loadingMapNameAuthorText.text = $"{data.Name} [{tierName}] - {data.Author}";
            _loadingMapNameAuthorText.color = themeColor; // Đổi màu text info theo độ khó
        }

        if (_loadingButtonCountText != null)
            _loadingButtonCountText.text = data.ButtonNumber.ToString();

        if (_loadingDifficultyText != null)
            _loadingDifficultyText.text = data.Difficulty.ToString("F1");
    }

    public void ShowNotification(string message, Action onClose = null)
    {
        if (_notificationInstance == null)
        {
            _notificationInstance = Instantiate(_notificationPrefab, _uiCanvasRoot);
        }
        _notificationInstance.ShowMessage(message, onClose);
        _notificationInstance.transform.SetAsLastSibling(); // Luôn trên cùng
    }

    public void AskConfirmation(string message, Action onYes)
    {
        if (_confirmationInstance == null)
        {
            _confirmationInstance = Instantiate(_confirmationPrefab, _uiCanvasRoot);
        }
        _confirmationInstance.Setup(message, onYes);
        _confirmationInstance.transform.SetAsLastSibling();
    }

    public void PlayClickSound()
    {
        if (_uiAudioSource != null && _clickSound != null)
        {
            // Lấy âm lượng SFX từ SettingsManager
            float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
            _uiAudioSource.PlayOneShot(_clickSound, volume);
        }
    }

    public void PlayCustomSound(AudioClip clip)
    {
        if (_uiAudioSource != null && clip != null)
        {
            // Lấy âm lượng SFX từ SettingsManager
            float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
            _uiAudioSource.PlayOneShot(clip, volume);
        }
    }

    public void HideAllScreens()
    {
        // Ẩn tất cả để nhường chỗ cho Gameplay HUD
        if (_homeScreen != null) _homeScreen.SetActive(false);
        if (_mapSelectionScreen != null) _mapSelectionScreen.SetActive(false);
        // Không ẩn LoadingScreen ở đây vì nó cần hiển thị trong lúc các cái khác ẩn
        // Hủy các màn hình được sinh ra động
        // Tìm kiếm trong _uiCanvasRoot để đảm bảo chỉ hủy các UI con của nó
        var progressUI = _uiCanvasRoot.GetComponentInChildren<ProgressUI>();
        if (progressUI != null) Destroy(progressUI.gameObject);

        var characterUI = _uiCanvasRoot.GetComponentInChildren<CharacterUI>();
        if (characterUI != null) Destroy(characterUI.gameObject);

        var multiplayerUI = _uiCanvasRoot.GetComponentInChildren<MultiplayerUI>();
        if (multiplayerUI != null) Destroy(multiplayerUI.gameObject);
    }

    // Hàm tiện ích để thiết lập RectTransform cho toàn màn hình
    private void SetRectTransformToFullScreen(RectTransform rect)
    {
        if (rect == null) return;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public void OpenSettings()
    {
        // Gọi ModalController của Settings_Modal ở đây (nếu cần xử lý từ manager)
        // Hoặc gán trực tiếp nút Settings vào ModalController
    }
}

}
