using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.UI;
using Core.Interfaces;
using Core;
using Core.Events;
using DG.Tweening;

/// <summary>
/// GameplayUIManager chịu trách nhiệm quản lý tất cả các yếu tố giao diện người dùng liên quan đến gameplay, bao gồm:
/// - Cập nhật thời gian cá nhân, thời gian kỷ lục, và đếm ngược
/// - Cập nhật lượng Air còn lại, Bonus Air, và Drain Rate
/// - Cập nhật tiến độ bấm nút và hiển thị cờ hoàn thành
/// - Hiển thị modal kết thúc game (thắng/thua) với thông tin chi tiết về map và thành tích
/// - Hiển thị menu tạm dừng và các tùy chọn liên quan
/// </summary>
public class GameplayUIManager : MonoBehaviour, IGameplayUIManager
{
    public static GameplayUIManager Instance { get; private set; }

    [Header("HUD References")]
    [SerializeField] private TMP_Text _personalTimeText;
    [Tooltip("Icon đồng hồ hiển thị bên cạnh thời gian cá nhân")]
    [SerializeField] private Image _personalTimeIcon;
    [SerializeField] private TMP_Text _floatNotificationText;
    [SerializeField] private TMP_Text _airText;
    [SerializeField] private TMP_Text _airRateText; // Text riêng cho drain rate
    [Tooltip("Text hiển thị số thứ tự nút cần bấm (Ví dụ: 1)")]
    [SerializeField] private TMP_Text _buttonStepText;
    [SerializeField] private Image _buttonIcon;
    [SerializeField] private GameObject _buttonFinishFlag;
    [Space]
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private Image _playerCountIcon;
    [SerializeField] private GameObject _playerFinishFlag;
    [Header("Stat Pulse Settings")]
    [SerializeField] private float _pulseDuration = 0.3f;
    [SerializeField] private TMP_Text _recordTimeText; // Thêm text kỷ lục

    [Header("Sliders")]
    [Tooltip("Slider hiển thị lượng Air còn lại")]
    [SerializeField] private Slider _airSlider;
    [Tooltip("Ảnh Fill của lớp Bubble Air (nên đặt đè lên trên Fill gốc)")]
    [SerializeField] private Image _bonusAirFill;
    [Tooltip("Slider hiển thị tiến độ thời gian (trôi từ 0 đến Max)")]
    [SerializeField] private Slider _timeSlider;

    [Header("Panels")]
    [Header("End Game Modal (Victory/GameOver)")]
    [SerializeField] private GameObject _endGamePanel;
    [SerializeField] private TMP_Text _endGameTitleText; // Hiển thị: "Map Completed", "You're Drowned!", v.v.
    [SerializeField] private TMP_Text _mapInfoText;      // Tên map [Tier]
    [SerializeField] private TMP_Text _statsTimeText;    // Thời gian hoàn thành
    [SerializeField] private TMP_Text _statsButtonText;  // Số nút đã ấn
    [SerializeField] private TMP_Text _earnedCoinsText;  // Hiển thị số xu nhận được
    [Tooltip("Object chứa cả icon và text tiền xu để ẩn/hiện đồng bộ (Nếu để trống sẽ dùng chính Text object)")]
    [SerializeField] private GameObject _earnedCoinsContainer;
    [SerializeField] private TMP_Text _restartBtnLabel;  // "Retry" hoặc "Play Again"
    [SerializeField] private GameObject _newBestTimeObject;

    [SerializeField] private GameObject _pausePanel;
    [SerializeField] private GameObject _loadingScreen; // Panel Loading cho Gameplay scene
    [SerializeField] private GameObject _backToHomeLoadingPanel; // Panel loading riêng khi về Home
    [Tooltip("Kéo Object chứa giao diện DevTool vào đây")]
    [SerializeField] private GameObject _devToolPanel;
    [SerializeField] private TMP_Text _infAirStatusText;
    [SerializeField] private TMP_Text _infJumpStatusText;
    [SerializeField] private TMP_Text _teleportModeStatusText;
    [SerializeField] private TMP_Text _haltTimelinesStatusText;

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

    private int _prevActivatedCount = -1;
    private Color _originalButtonTextColor;
    private Color _originalButtonIconColor;
    private Color _originalPersonalTimeColor;
    private Color _originalPersonalTimeIconColor;
    private int _prevAliveCount = -1;
    private Color _originalPlayerCountColor;
    private Color _originalPlayerIconColor;
    private Vector2 _originalCountdownPos;
    private Coroutine _notificationCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        if (_buttonStepText != null) _originalButtonTextColor = _buttonStepText.color;
        if (_buttonIcon != null) _originalButtonIconColor = _buttonIcon.color;
        if (_playerCountText != null) _originalPlayerCountColor = _playerCountText.color;
        if (_playerCountIcon != null) _originalPlayerIconColor = _playerCountIcon.color;
        if (_personalTimeText != null) _originalPersonalTimeColor = _personalTimeText.color;
        if (_personalTimeIcon != null) _originalPersonalTimeIconColor = _personalTimeIcon.color;
        if (_floatNotificationText != null) 
        {
            _originalCountdownPos = _floatNotificationText.rectTransform.anchoredPosition;
        }
        _prevActivatedCount = 0;
        _prevAliveCount = 0;

        // Ẩn các panel khi bắt đầu
        if (_endGamePanel != null) _endGamePanel.SetActive(false);
        if (_loadingScreen != null) _loadingScreen.SetActive(false);
        if (_backToHomeLoadingPanel != null) _backToHomeLoadingPanel.SetActive(false);

        // Đảm bảo âm thanh UI vẫn phát được khi game pause (AudioListener.pause = true)
        if (_uiAudioSource != null) _uiAudioSource.ignoreListenerPause = true;
    }
    
    /// <summary>
    /// Cập nhật text hiển thị thời gian cá nhân trên HUD.
    /// </summary>
    public void UpdatePersonalRecord(float time)
    {
        if (_personalTimeText != null) _personalTimeText.text = FormatTime(time);
    }

    /// <summary>
    /// Cập nhật giá trị slider tiến độ thời gian của màn chơi.
    /// </summary>
    public void UpdateTimeSlider(float currentTime, float maxTime, bool isVotePhase = false)
    {
        if (_timeSlider != null) _timeSlider.value = currentTime;
        if (_timeSlider != null) _timeSlider.maxValue = maxTime; // Ensure max value is set
    }

    public void SetRecordTime(float bestTime, float maxMapTime)
    {
        if (_personalTimeText != null) _personalTimeText.color = _originalPersonalTimeColor;
        if (_personalTimeIcon != null) _personalTimeIcon.color = _originalPersonalTimeIconColor;

        if (_recordTimeText != null)
            _recordTimeText.text = FormatTime(bestTime > 0f ? bestTime : maxMapTime);
    }

    public void SetPersonalTimeHighlight(bool highlightAsVictory)
    {
        Color c = highlightAsVictory ? Color.green : _originalPersonalTimeColor;
        if (_personalTimeText != null) _personalTimeText.color = c;
        if (_personalTimeIcon != null) _personalTimeIcon.color = highlightAsVictory ? Color.green : _originalPersonalTimeIconColor;
    }

    public void ShowPlayerFinishFlag(bool show)
    {
        if (_playerFinishFlag != null) _playerFinishFlag.SetActive(show);

        // Khi cờ hoàn thành hiện lên, xóa text số lượng người chơi (giữ icon)
        if (_playerCountText != null && show)
        {
            _playerCountText.text = "";
        }

        // Đảm bảo trạng thái ban đầu khi cờ ẩn
        if (!show && _playerCountText != null && _prevAliveCount != -1)
            UpdateAlivePlayerCount(_prevAliveCount, 1); // Cập nhật lại để hiện số đúng
    }

    public void SetCountdownText(string text)
    {
        if (_floatNotificationText != null)
        {
            // Reset lại trạng thái của Text nếu GameplayManager gọi (tránh bị ảnh hưởng bởi tween notification)
            _floatNotificationText.DOKill();
            _floatNotificationText.color = Color.white;
            _floatNotificationText.alpha = 1f; // Sử dụng TMP alpha thay vì canvasRenderer
            _floatNotificationText.rectTransform.anchoredPosition = _originalCountdownPos;
            _floatNotificationText.text = text;
        }
    }

    public void SetMaxAir(float maxAir)
    {
        if (_airSlider != null) _airSlider.maxValue = maxAir;
    }

    public void UpdateAirUI(float currentAir, float bonusAir, float bonusMax, float rate)
    {
        // Trong Multiplayer, hàm này PHẢI được gọi từ dữ liệu của Local Player (GameplayManager lo việc này)
        float totalAir = currentAir + bonusAir;
        
        // 1. Cập nhật Text Air
        if (_airText != null) _airText.text = totalAir.ToString("F0");

        // 2. Cập nhật Text Drain Rate với màu sắc
        if (_airRateText != null)
        {
            UpdateAirRateUI(rate);
        }

        // 3. Cập nhật Slider Air gốc
        if (_airSlider != null)
        {
            // Slider gốc chỉ thể hiện lượng Oxy nội tại của Player
            _airSlider.value = Mathf.Clamp(currentAir, 0, _airSlider.maxValue);
            
            // 4. Cập nhật lớp Bubble Air (Bonus)
            if (_bonusAirFill != null)
            {
                bool hasBonus = bonusAir > 0.1f; // Sử dụng ngưỡng nhỏ để tránh nháy UI do sai số float
                _bonusAirFill.gameObject.SetActive(hasBonus);

                if (hasBonus && bonusMax > 0)
                {
                    // Khi dùng 9-slice (Sliced), ta không dùng fillAmount.
                    // Thay vào đó, ta điều chỉnh anchorMax.x của RectTransform để co giãn thanh.
                    float ratio = Mathf.Clamp01(bonusAir / bonusMax);
                    Vector2 anchorMax = _bonusAirFill.rectTransform.anchorMax;
                    anchorMax.x = ratio;
                    _bonusAirFill.rectTransform.anchorMax = anchorMax;
                }
            }
        }
    }

    private void UpdateAirRateUI(float rate)
    {
        if (rate > 0.1f)
        {
            _airRateText.text = $"+{rate:F0}/s";
            _airRateText.color = Color.green;
        }
        else if (rate < -0.1f)
        {
            _airRateText.text = $"{rate:F0}/s";
            _airRateText.color = Color.red;
        }
        else
        {
            _airRateText.text = "";
            _airRateText.color = Color.gray;
        }
    }

    public void UpdateButtonProgress(int current, int total)
    {
        if (_buttonStepText == null) return;

        // 1. Hiệu ứng Pulse khi số lượng nút tăng lên
        if (current > _prevActivatedCount && _prevActivatedCount != -1)
        {
            StopCoroutine(nameof(PulseButtonRoutine));
            StartCoroutine(PulseButtonRoutine());
            
            // Tự động thông báo khi bấm nút
            ShowFloatNotification($"Pressed Button {current}", new Color(1f, 1f, 0.7f)); // Màu vàng nhạt
        }
        _prevActivatedCount = current;

        // 2. Kiểm tra trạng thái hoàn thành
        bool isSequenceFinished = total > 0 && current >= total;

        if (isSequenceFinished)
        {
            _buttonStepText.text = "";
            if (_buttonFinishFlag != null) ShowButtonFinishFlag(true);
        }
        else
        {
            _buttonStepText.text = (current + 1).ToString();
            if (_buttonFinishFlag != null) ShowButtonFinishFlag(false);
        }
    }

    public void ShowButtonFinishFlag(bool show)
    {
        if (_buttonFinishFlag != null) _buttonFinishFlag.SetActive(show);
    }

    public void UpdateAlivePlayerCount(int current, int total)
    {
        if (_playerCountText == null) return;

        // Nếu đang hiển thị cờ hoàn thành cá nhân, không cập nhật text số người chơi
        // vì icon lá cờ đang đè lên vị trí này
        if (_playerFinishFlag != null && _playerFinishFlag.activeSelf) return;

        if (current != _prevAliveCount && _prevAliveCount != -1)
        {
            StopCoroutine(nameof(PulsePlayerCountRoutine));
            StartCoroutine(PulsePlayerCountRoutine());
        }
        _prevAliveCount = current;
        _playerCountText.text = current.ToString();
    }

    public void UpdateLevelProgress(float time)
    {
        if (_timeSlider != null) _timeSlider.value = time;
    }

    private IEnumerator PulseButtonRoutine()
    {
        float elapsed = 0f;
        Color activeColor = Color.green;

        // 1. Chuyển sang trạng thái Active ngay lập tức (Pulse Up)
        if (_buttonIcon != null) 
            _buttonIcon.color = activeColor;
            
        if (_buttonStepText != null)
            _buttonStepText.color = activeColor;

        // 2. Hiệu ứng Fade Out quay về trạng thái bình thường
        while (elapsed < _pulseDuration)
        {
            elapsed += Time.unscaledDeltaTime; // Sử dụng unscaled để hiệu ứng mượt kể cả khi Time.timeScale thấp
            float t = elapsed / _pulseDuration;

            if (_buttonStepText != null)
                _buttonStepText.color = Color.Lerp(activeColor, _originalButtonTextColor, t);

            if (_buttonIcon != null)
                _buttonIcon.color = Color.Lerp(activeColor, _originalButtonIconColor, t);

            yield return null;
        }

        // 3. Đảm bảo trả về đúng trạng thái gốc sau khi kết thúc loop
        if (_buttonStepText != null)
            _buttonStepText.color = _originalButtonTextColor;

        if (_buttonIcon != null) 
            _buttonIcon.color = _originalButtonIconColor;
    }

    private IEnumerator PulsePlayerCountRoutine()
    {
        float elapsed = 0f;
        Color activeColor = Color.red;

        if (_playerCountIcon != null) _playerCountIcon.color = activeColor;
        if (_playerCountText != null) _playerCountText.color = activeColor;

        while (elapsed < _pulseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / _pulseDuration;

            if (_playerCountText != null)
                _playerCountText.color = Color.Lerp(activeColor, _originalPlayerCountColor, t);

            if (_playerCountIcon != null)
                _playerCountIcon.color = Color.Lerp(activeColor, _originalPlayerIconColor, t);

            yield return null;
        }

        if (_playerCountText != null)
            _playerCountText.color = _originalPlayerCountColor;

        if (_playerCountIcon != null) 
            _playerCountIcon.color = _originalPlayerIconColor;
    }

    public void ShowEndGame(bool isVictory, string title, MapData data, float time, int buttons, bool isNewBest, int earnedCoins = 0)
    {
        if (_endGamePanel == null) return;
        _endGamePanel.SetActive(true);

        // 1. Cập nhật Header (Sử dụng tiêu đề làm lý do duy nhất)
        if (_endGameTitleText != null) 
            _endGameTitleText.text = title;

        // 2. Cập nhật Stats (Tên map, Time, Buttons)
        if (_mapInfoText != null && data != null)
        {
            string tierName = _palette != null ? _palette.GetTierFromRating(data.Difficulty).ToString() : "Unknown";
            if (tierName == "CrazyPlus") tierName = "Crazy+";
            _mapInfoText.text = $"{data.Name} [{tierName}]";
            _mapInfoText.color = _palette != null ? _palette.GetColorFromRating(data.Difficulty) : Color.white;
        }

        if (_statsTimeText != null) _statsTimeText.text = $"Personal Time: {FormatTime(time)}";
        if (_statsButtonText != null) _statsButtonText.text = $"Button Pressed: {buttons}";

        // Hiển thị xu nhận được
        bool shouldShowCoins = isVictory && earnedCoins > 0;

        // Ưu tiên ẩn/hiện container nếu có, nếu không thì tác động trực tiếp lên text object
        if (_earnedCoinsContainer != null) _earnedCoinsContainer.SetActive(shouldShowCoins);
        else if (_earnedCoinsText != null) _earnedCoinsText.gameObject.SetActive(shouldShowCoins);

        if (_earnedCoinsText != null)
        {
            _earnedCoinsText.text = $"+{earnedCoins}";
            // Có thể thêm màu vàng cho tiền
            _earnedCoinsText.color = new Color(1f, 0.84f, 0f); 
        }

        // 3. Logic Victory-only (Kỷ lục và nhãn nút)
        if (_newBestTimeObject != null) 
            _newBestTimeObject.SetActive(isVictory && isNewBest);

        // Đổi màu text thời gian ở HUD sang xanh lá nếu thắng
        if (isVictory && _personalTimeText != null)
            _personalTimeText.color = Color.green;

        // Đổi màu icon đồng hồ sang xanh lá nếu thắng
        if (isVictory && _personalTimeIcon != null)
            _personalTimeIcon.color = Color.green;

        if (_restartBtnLabel != null)
            _restartBtnLabel.text = isVictory ? "Play Again" : "Retry";
    }

    public void ShowPauseMenu(bool show)
    {
        if (_pausePanel != null) _pausePanel.SetActive(show);
    }

    // --- Button Handlers ---

    public void OnPauseClick()
    {
        PlayClickSound();
        GameplayEvents.TriggerPauseRequest(true);
    }

    public void OnResumeClick()
    {
        PlayClickSound();
        GameplayEvents.TriggerPauseRequest(false);
    }

    public void OnRestartClick()
    {
        PlayClickSound();
        GameplayEvents.TriggerRestartRequested();
    }

    public void OnBackToMenuClick()
    {
        PlayClickSound();
        GameplayEvents.TriggerBackToMenuRequested();
    }

    /// <summary>
    /// Phát một âm thanh SFX bất kỳ thông qua UI AudioSource
    /// </summary>
    public void PlaySound(AudioClip clip)
    {
        if (_uiAudioSource != null && clip != null)
        {
            float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
            _uiAudioSource.PlayOneShot(clip, volume);
        }
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

    public void ShowLoadingScreen(bool show)
    {
        if (_loadingScreen != null) _loadingScreen.SetActive(show);
    }

    public void ShowBackToMainMenuLoadingScreen()
    {
        if (_backToHomeLoadingPanel != null) _backToHomeLoadingPanel.SetActive(true);
    }

    public void ShowDevTools(bool show)
    {
        if (_devToolPanel != null) _devToolPanel.SetActive(show);
    }

    public void UpdateInfiniteAirStatus(bool isOn)
    {
        if (_infAirStatusText != null)
            _infAirStatusText.text = $"Inf. Air: {(isOn ? "On" : "Off")}";
    }

    public void UpdateInfiniteJumpStatus(bool isOn)
    {
        if (_infJumpStatusText != null)
            _infJumpStatusText.text = $"Inf. Jump: {(isOn ? "On" : "Off")}";
    }

    public void UpdateTeleportModeStatus(bool isOn)
    {
        if (_teleportModeStatusText != null)
            _teleportModeStatusText.text = $"Teleport: {(isOn ? "On" : "Off")}";
    }

    public void UpdateHaltTimelinesStatus(bool isHalted)
    {
        if (_haltTimelinesStatusText != null)
            _haltTimelinesStatusText.text = isHalted ? "Timelines: Halted" : "Halt Timelines";
    }

    public void OnToggleInfiniteAirClick()
    {
        // Bắn sự kiện yêu cầu đảo trạng thái khí
        GameplayEvents.TriggerInfiniteAirToggle();
    }

    public void OnToggleTeleportModeClick()
    {
        GameplayEvents.TriggerTeleportModeToggle();
    }

    public void OnHaltTimelinesClick()
    {
        GameplayEvents.TriggerHaltTimelines();
    }

    public void OnToggleInfiniteJumpClick()
    {
        GameplayEvents.TriggerInfiniteJumpToggle();
    }

    public void OnTeleportToNextButtonDevClick()
    {
        GameplayEvents.TriggerTeleportToNextButton();
    }

    public void SetupMapLoadingScreen(MapData data)
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

    private string FormatTime(float timeInSeconds)
    {
        System.TimeSpan t = System.TimeSpan.FromSeconds(timeInSeconds);
        return string.Format("{0:0}:{1:00}.{2:000}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
    }

    public void ShowFloatNotification(string message, Color color, float duration = 1f)
    {
        if (_floatNotificationText == null) return;

        if (_notificationCoroutine != null) StopCoroutine(_notificationCoroutine);
        _notificationCoroutine = StartCoroutine(NotificationRoutine(message, color, duration));
    }

    private IEnumerator NotificationRoutine(string message, Color color, float duration)
    {
        float fadeTime = 0.2f;
        float moveOffset = 20f;

        // 1. Reset trạng thái ban đầu (Ở trên cao và mờ)
        _floatNotificationText.DOKill();
        _floatNotificationText.text = message;
        _floatNotificationText.color = color;
        _floatNotificationText.alpha = 0f; // Bắt đầu từ hoàn toàn trong suốt
        _floatNotificationText.rectTransform.anchoredPosition = _originalCountdownPos + new Vector2(0, moveOffset);

        // 2. Fade In từ trên xuống
        _floatNotificationText.DOFade(1f, fadeTime).SetUpdate(true);
        _floatNotificationText.rectTransform.DOAnchorPos(_originalCountdownPos, fadeTime).SetEase(Ease.Linear).SetUpdate(true);
        
        yield return new WaitForSecondsRealtime(duration);

        // 3. Fade Out từ dưới lên (tiếp tục đi lên trên)
        _floatNotificationText.DOFade(0f, fadeTime).SetUpdate(true);
        _floatNotificationText.rectTransform.DOAnchorPos(_originalCountdownPos + new Vector2(0, moveOffset), fadeTime).SetEase(Ease.Linear).SetUpdate(true);

        yield return new WaitForSecondsRealtime(fadeTime);

        _floatNotificationText.text = "";
        _notificationCoroutine = null;
    }
}
