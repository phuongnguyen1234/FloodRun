using System;
using UnityEngine;
using TMPro;
using Core.Interfaces;
using System.Collections;
using UnityEngine.UI;
using Core;
using System.Linq;
using UI;
using DG.Tweening;

namespace UI.Multiplayer
{
    /// <summary>
    /// Quản lý giao diện HUD trong Multiplayer.
    /// Tích hợp Chat, Voting UI và thông tin phòng.
    /// </summary>
    public class MultiplayerUIManager : MonoBehaviour, IMultiplayerUIManager
    {
        public static MultiplayerUIManager Instance { get; private set; }

        [Header("HUD Management")]
        [SerializeField] private GameObject _lobbyHUD;
        [SerializeField] private GameObject _gameplayHUD;

        [Header("Lobby HUD & Player Status")]
        [Space]
        [SerializeField] private TMP_Text _playStatusText;
        [SerializeField] private Image _playStatusIcon;
        [SerializeField] private Sprite _playSprite;
        [SerializeField] private Sprite _pauseSprite;
        [Space]
        [SerializeField] private TMP_Text _spectateStatusText;
        [SerializeField] private Image _spectateIcon;
        [SerializeField] private Sprite _spectateSprite;
        [SerializeField] private Sprite _stopSpectateSprite;
        [SerializeField] private GameObject _spectateControls;

        [Space]
        [SerializeField] private Color _playActiveColor = Color.white;
        [SerializeField] private Color _playAfkColor = Color.yellow;
        [SerializeField] private Color _spectateNormalColor = Color.white;
        [SerializeField] private Color _spectateActiveColor = Color.red;

        [Header("Gameplay HUD (Common with SP)")]
        [SerializeField] private TMP_Text _personalTimeText;
        [SerializeField] private TMP_Text _bestRecordTimeText;
        [SerializeField] private TMP_Text _floatNotificationText;
        [Tooltip("Cờ hoàn thành map cho người chơi cục bộ")]
        [SerializeField] private GameObject _playerFinishFlag;
        [Space]
        [SerializeField] private TMP_Text _playerCountText;
        [SerializeField] private Image _playerCountIcon; // Biểu thị số player còn sống
        [SerializeField] private float _pulseDuration = 0.3f;

        [Header("Air UI")]
        [SerializeField] private Slider _airSlider;
        [Tooltip("Ảnh Fill của lớp Bubble Air (nên đặt đè lên trên Fill gốc)")]
        [SerializeField] private Image _bonusAirFill;
        [SerializeField] private TMP_Text _airText;
        [SerializeField] private TMP_Text _airRateText;

        [Header("Button Progress")]
        [SerializeField] private TMP_Text _buttonStepText;
        [SerializeField] private Image _buttonIcon;
        [SerializeField] private GameObject _finishFlag;

        [Header("Sliders")]
        [SerializeField] private Slider _timeSlider;

        [Header("Multiplayer Systems")]
        [SerializeField] private GameObject _votingPanel;
        [SerializeField] private VoteMapModal _voteMapModal;
        [SerializeField] private Button _openVoteModalButton;
        [SerializeField] private GameObject _chatPanel;
        [Space]
        [SerializeField] private GameObject _chatLinePrefab;
        [SerializeField] private Transform _chatContent;
        [SerializeField] private int _maxChatLines = 50;

        [Header("Modals & Modifiers")]
        [SerializeField] private RoomInfoModalUI _roomInfoModal; 
        [SerializeField] private GameObject _settingsModal;
        [SerializeField] private NotificationModalUI _notificationPrefab;
        [SerializeField] private ConfirmationModalUI _confirmationPrefab;

        [Header("Loading Screens")]
        [SerializeField] private GameObject _mapLoadingPanel;
        [SerializeField] private GameObject _joiningRoomLoadingPanel;
        [SerializeField] private GameObject _backToMainMenuLoadingPanel;

        [Header("Map Loading Details")]
        [SerializeField] private Image _loadingPreviewImage;
        [Tooltip("Định dạng: <Tên map> [<Tier>] - <Tác giả>")]
        [SerializeField] private TMP_Text _loadingMapNameAuthorText; 
        [SerializeField] private TMP_Text _loadingStatusText;
        [SerializeField] private TMP_Text _loadingButtonCountText;
        [SerializeField] private TMP_Text _loadingDifficultyText;

        [Header("Global Settings & Audio")]
        [SerializeField] private DifficultyPalette _palette;
        [SerializeField] private AudioClip _clickSound;
        [SerializeField] private AudioSource _uiAudioSource;
        [SerializeField] private Color _timerNormalColor = Color.white;
        [SerializeField] private Color _timerVoteColor = Color.yellow;

        private NotificationModalUI _notificationInstance;
        private ConfirmationModalUI _confirmationInstance;
        private IMultiplayerManager _logicManager;
        private Vector2 _originalCountdownPos; // For notification animation
        private Coroutine _notificationCoroutine; // For notification animation

        private int _prevActivatedCount = -1;
        private Color _originalButtonTextColor;
        private Color _originalButtonIconColor;
        private int _prevAliveCount = -1;
        private Color _originalPlayerCountColor;
        private Color _originalPlayerIconColor;


        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }

            if (_uiAudioSource != null) _uiAudioSource.ignoreListenerPause = true;

            // Tối ưu: Tìm Logic Manager một lần duy nhất và gán cho các Modal con
            // Việc gán này vẫn hoạt động ngay cả khi Modal đang SetActive(false)
            _logicManager = FindObjectsByType<MonoBehaviour>().OfType<IMultiplayerManager>().FirstOrDefault();
            if (_logicManager != null)
            {
                if (_roomInfoModal != null) _roomInfoModal.SetManager(_logicManager);
                if (_voteMapModal != null) 
                {
                    _voteMapModal.SetManager(_logicManager);
                    _voteMapModal.gameObject.SetActive(false);
                }
                // Bạn có thể gán cho các modal khác ở đây (ví dụ: ChatModal, VoteModal...)
                // if (_chatModal != null) _chatModal.SetManager(logicManager);
            }

            if (_floatNotificationText != null)
            {
                _originalCountdownPos = _floatNotificationText.rectTransform.anchoredPosition;
            }

            if (_buttonStepText != null) _originalButtonTextColor = _buttonStepText.color;
            if (_buttonIcon != null) _originalButtonIconColor = _buttonIcon.color;
            if (_playerCountText != null) _originalPlayerCountColor = _playerCountText.color;
            if (_playerCountIcon != null) _originalPlayerIconColor = _playerCountIcon.color;
            
            _prevActivatedCount = 0;
            _prevAliveCount = 0;

            if (_openVoteModalButton != null)
            {
                _openVoteModalButton.onClick.AddListener(OpenVotingModal);
            }
        }

        private void Update()
        {
            if (_logicManager == null) return;

            // GLOBAL TIMER SLIDER: Cập nhật slider thời gian dựa trên trạng thái game (Global)
            var state = _logicManager.GetCurrentGameState();
            if (state == GameState.Voting)
            {
                UpdateTimeSlider(_logicManager.NetworkTime.Value, 10f, true);
            }
        }

        public void OpenVotingModal()
        {
            if (_voteMapModal == null) return;
            _voteMapModal.gameObject.SetActive(true);
            _voteMapModal.Setup();
        }

        public void SetVotingButtonVisible(bool visible)
        {
            if (_openVoteModalButton != null)
                _openVoteModalButton.gameObject.SetActive(visible);

            // FIX: Kích hoạt panel Voting để Slider Global (nếu nằm trong đây) có thể hiển thị và chạy
            if (_votingPanel != null)
                _votingPanel.SetActive(visible);

            // Nếu bắt đầu đợt vote mới (visible = true), reset trạng thái vote của local player
            if (visible && _voteMapModal != null)
            {
                _voteMapModal.ResetVoteStatus();
            }

            // Đảm bảo Modal luôn đóng khi trạng thái Voting thay đổi (bắt đầu hoặc kết thúc)
            // Người chơi sẽ phải click nút Vote thủ công để mở lại.
            if (_voteMapModal != null) _voteMapModal.gameObject.SetActive(false);
        }


        #region ICommonUIManager Implementation

        /// <summary>
        /// Hiển thị thông báo tạm thời (floating text).
        /// TODO: Implement với floating text system (color, duration)
        /// </summary>
        public void ShowNotification(string message, Color color = default, float duration = 2f)
        {
            if (_floatNotificationText == null) return;
            if (_notificationCoroutine != null) StopCoroutine(_notificationCoroutine);
            _notificationCoroutine = StartCoroutine(NotificationRoutine(message, color, duration));
        }

        /// <summary>
        /// Hiển thị một NotificationModal chặn tương tác với thông báo.
        /// </summary>
        public void ShowNotificationModal(string message, Action onClose = null)
        {
            if (_notificationInstance == null && _notificationPrefab != null)
                _notificationInstance = Instantiate(_notificationPrefab, transform);
            if (_notificationInstance != null) _notificationInstance.ShowMessage(message, onClose);
        }

        #endregion

        #region IGameplayHUDUI Implementation

        public void UpdatePersonalTime(float time)
        {
            if (_personalTimeText != null)
                _personalTimeText.text = FormatTime(time);
        }

        public void SetMaxTime(float time)
        {
            if (_bestRecordTimeText != null)
                _bestRecordTimeText.text = $"{FormatTime(time)}";
            
            if (_timeSlider != null) 
            {
                _timeSlider.maxValue = time;
                // Lưu ý: value của slider sẽ được cập nhật liên tục trong hàm Update() thông qua NetworkTime
            }
        }

        /// <summary>
        /// Hiển thị personal record (best time) giống như singleplayer.
        /// </summary>
        public void SetRecordTime(float time)
        {
            if (_bestRecordTimeText != null)
            {
                // Nếu time <= 0 (chưa có record), hiển thị "-"
                if (time <= 0f)
                {
                    _bestRecordTimeText.text = "-";
                }
                else
                {
                    _bestRecordTimeText.text = FormatTime(time);
                }
            }
        }

        public void ShowPlayerFinishFlag(bool show)
        {
            if (_playerFinishFlag != null) _playerFinishFlag.SetActive(show);

            // Khi cờ hoàn thành cá nhân hiện lên, xóa text số lượng player để nhường chỗ cho icon cờ (giữ icon player)
            if (_playerCountText != null && show)
            {
                _playerCountText.text = "";
            }

            // Đảm bảo hiện lại số lượng người chơi chính xác khi cờ ẩn đi (ví dụ khi hồi sinh)
            if (!show && _playerCountText != null && _prevAliveCount != -1)
                UpdateAlivePlayerCount(_prevAliveCount, 0); 
        }

        public void UpdateAirUI(float currentAir, float bonusAir, float bonusMax, float rate)
        {
            if (_airSlider != null)
                _airSlider.value = Mathf.Clamp(currentAir, 0, _airSlider.maxValue);

            // Cập nhật Text Drain Rate với màu sắc
            if (_airRateText != null)
            {
                UpdateAirRateUI(rate);
            }

            // Cập nhật lớp Bubble Air (Bonus) giống GameplayUIManager
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

            if (_airText != null)
            {
                float totalAir = currentAir + bonusAir;
                _airText.text = totalAir.ToString("F0");
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

            // 1. Hiệu ứng Pulse khi số lượng nút tăng lên (giống SP)
            if (current > _prevActivatedCount && _prevActivatedCount != -1)
            {
                StopCoroutine(nameof(PulseButtonRoutine));
                StartCoroutine(PulseButtonRoutine());
            }
            _prevActivatedCount = current;

            // 2. Kiểm tra trạng thái hoàn thành: Hiện lá cờ nếu đã đủ nút
            bool isFinished = (total > 0 && current >= total);

            if (isFinished)
            {
                _buttonStepText.text = "";
                if (_finishFlag != null) _finishFlag.SetActive(true);
                if (_buttonIcon != null) _buttonIcon.gameObject.SetActive(false);
            }
            else
            {
                _buttonStepText.text = (current + 1).ToString();
                if (_finishFlag != null) _finishFlag.SetActive(false);
                if (_buttonIcon != null) _buttonIcon.gameObject.SetActive(true);
            }
        }

        public void SetCountdownText(string text)
        {
            if (_floatNotificationText != null)
            {
                // Reset lại trạng thái của Text (tránh bị ảnh hưởng bởi tween notification)
                _floatNotificationText.DOKill();
                _floatNotificationText.color = Color.white;
                _floatNotificationText.alpha = 1f;
                _floatNotificationText.rectTransform.anchoredPosition = _originalCountdownPos;
                _floatNotificationText.text = text;
            }
        }

        public void UpdateTimeSlider(float currentTime, float maxTime, bool isVotePhase = false)
        {
            if (_timeSlider != null)
            {
                _timeSlider.maxValue = maxTime;
                _timeSlider.value = currentTime;

                if (_timeSlider.fillRect != null)
                {
                    var img = _timeSlider.fillRect.GetComponent<Image>();
                    if (img != null) img.color = isVotePhase ? _timerVoteColor : _timerNormalColor;
                }
            }
        }

        private IEnumerator PulseButtonRoutine()
        {
            float elapsed = 0f;
            Color activeColor = Color.green;

            // 1. Chuyển sang trạng thái Active ngay lập tức
            if (_buttonIcon != null) _buttonIcon.color = activeColor;
            if (_buttonStepText != null) _buttonStepText.color = activeColor;

            // 2. Hiệu ứng Fade Out quay về trạng thái bình thường
            while (elapsed < _pulseDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / _pulseDuration;

                if (_buttonStepText != null)
                    _buttonStepText.color = Color.Lerp(activeColor, _originalButtonTextColor, t);

                if (_buttonIcon != null)
                    _buttonIcon.color = Color.Lerp(activeColor, _originalButtonIconColor, t);

                yield return null;
            }

            if (_buttonStepText != null)
                _buttonStepText.color = _originalButtonTextColor;

            if (_buttonIcon != null) 
                _buttonIcon.color = _originalButtonIconColor;
        }

        private IEnumerator PulsePlayerCountRoutine()
        {
            float elapsed = 0f;
            Color activeColor = Color.red; // Pulse màu đỏ khi số lượng player thay đổi

            // 1. Chuyển sang trạng thái Active ngay lập tức
            if (_playerCountIcon != null) _playerCountIcon.color = activeColor;
            if (_playerCountText != null) _playerCountText.color = activeColor;

            // 2. Hiệu ứng Fade Out quay về trạng thái bình thường
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

        #endregion

        #region Confirmation & Modals


        public void AskConfirmation(string message, Action onYes)
        {
            if (_confirmationInstance == null && _confirmationPrefab != null)
            {
                _confirmationInstance = Instantiate(_confirmationPrefab, transform);
            }
            
            // Setup của ConfirmationModalUI chỉ nhận 2 tham số: message và action cho nút Yes
            if (_confirmationInstance != null) _confirmationInstance.Setup(message, onYes);
        }
        #endregion

        #region Chat System

        public void ShowChat(bool show)
        {
            _chatPanel?.SetActive(show);
        }

        public void ToggleChat()
        {
            if (_chatPanel == null) return;
            ShowChat(!_chatPanel.activeSelf);
        }

        public void UpdateAlivePlayerCount(int current, int total)
        {
            if (_playerCountText == null) return;

            // Nếu đang hiển thị cờ hoàn thành, không cập nhật text số người còn sống
            // để tránh việc con số xuất hiện đè lên icon lá cờ
            if (_playerFinishFlag != null && _playerFinishFlag.activeSelf) return;

            // Hiệu ứng Pulse đỏ khi số lượng thay đổi (người chết hoặc thoát)
            if (current != _prevAliveCount && _prevAliveCount != -1)
            {
                StopCoroutine(nameof(PulsePlayerCountRoutine));
                StartCoroutine(PulsePlayerCountRoutine());
            }
            _prevAliveCount = current;
            _playerCountText.text = current.ToString();
        }

        public void AddChatMessage(string sender, string message, bool isHost)
        {
            if (_chatLinePrefab == null || _chatContent == null) return;

            GameObject lineObj = Instantiate(_chatLinePrefab, _chatContent);
            TMP_Text text = lineObj.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                string prefix = isHost ? "<color=yellow>[Host]</color> " : "";
                text.text = $"{prefix}<b>{sender}:</b> {message}";
            }

            // Giới hạn số lượng tin nhắn (theo thiết kế là 50)
            if (_chatContent.childCount > _maxChatLines)
            {
                Destroy(_chatContent.GetChild(0).gameObject);
            }
        }

        #endregion

        #region Room & UI Management

        public void ShowRoomInfo(bool show)
        {
            if (_roomInfoModal == null) return;
            if (show) _roomInfoModal.Show();
            else _roomInfoModal.Hide();
        }

        public void ShowSettings(bool show)
        {
            if (_settingsModal != null) _settingsModal.SetActive(show);
        }

        /// <summary>
        /// Dọn dẹp tất cả các Modal đang mở để tránh đè lên nhau.
        /// </summary>
        public void HideAllModals()
        {
            ShowRoomInfo(false);
            ShowSettings(false);
        }

        #endregion

        #region Loading Screen

        public void ShowLoadingScreen(bool show)
        {
            if (_mapLoadingPanel != null) _mapLoadingPanel.SetActive(show);
        }

        public void ShowJoiningLoadingScreen(bool show)
        {
            if (_joiningRoomLoadingPanel != null) _joiningRoomLoadingPanel.SetActive(show);
        }

        public void ShowBackToMainMenuLoadingScreen()
        {
            StopBackgroundMusic();
            if (_backToMainMenuLoadingPanel != null) _backToMainMenuLoadingPanel.SetActive(true);
        }

        /// <summary>
        /// Dừng nhạc nền ngay lập tức khi thoát khỏi phòng.
        /// </summary>
        private void StopBackgroundMusic()
        {
            if (BackgroundMusicManager.Instance != null)
            {
                BackgroundMusicManager.Instance.GetAudioSource()?.Stop();
            }
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
                _loadingMapNameAuthorText.color = themeColor;
            }

            if (_loadingButtonCountText != null)
                _loadingButtonCountText.text = data.ButtonNumber.ToString();

            if (_loadingDifficultyText != null)
                _loadingDifficultyText.text = data.Difficulty.ToString("F1");
        }

        #endregion

        #region Audio SFX

        public void PlayClickSound()
        {
            if (_uiAudioSource != null && _clickSound != null)
            {
                // Lấy âm lượng SFX từ SettingsManager
                float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
                _uiAudioSource.PlayOneShot(_clickSound, volume);
            }
        }

        #endregion

        #region HUD Control

        public void SetHUDMode(bool isGameplay)
        {
            // Cập nhật HUD dựa trên trạng thái tham gia thực tế của người chơi
            UpdateGameplayHUDVisibility();
        }

        public void UpdatePlayStatus(bool isAFK)
        {
            // ACTION ORIENTED: Đang AFK thì hiện nút "Play" để vào game, đang Active thì hiện "Pause" để nghỉ.
            if (_playStatusText != null) 
            {
                _playStatusText.text = isAFK ? "Play" : "Pause";
            }

            if (_playStatusIcon != null)
            {
                // Nếu đang AFK -> hiện Icon Play (Hành động: Click để Active)
                _playStatusIcon.sprite = isAFK ? _playSprite : _pauseSprite;
                _playStatusIcon.color = isAFK ? _playActiveColor : _playAfkColor;
            }
        }

        public void OnGamePanelStartClick()
        {
            PlayClickSound();
            _logicManager?.RequestStartGame();
        }

        public void UpdateSpectateStatus(bool isSpectating)
        {
            if (_spectateStatusText != null)
                _spectateStatusText.text = isSpectating ? "Stop spectating" : "Spectate";

            if (_spectateIcon != null)
            {
                _spectateIcon.sprite = isSpectating ? _stopSpectateSprite : _spectateSprite;
                _spectateIcon.color = isSpectating ? _spectateActiveColor : _spectateNormalColor;
            }

            // Spectate_Btns (chứa 2 nút next/previous) chỉ hiện khi đang spectate
            if (_spectateControls != null)
            {
                _spectateControls.SetActive(isSpectating);
            }
        }

        // --- Lobby HUD Actions (Gán vào OnClick trong Inspector) ---

        #endregion

        #region Lobby HUD Actions

        public void OnPlayToggleClick()
        {
            PlayClickSound();
            _logicManager?.LocalPlayer?.ToggleAFKStatus();
        }

        public void OnSpectateToggleClick()
        {
            PlayClickSound();
            _logicManager?.LocalPlayer?.ToggleSpectateStatus();
        }

        public void OnShopClick()
        {
            PlayClickSound();
            // Ở đây sẽ gọi logic mở CharacterUI theo thiết kế
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Định dạng số giây thành chuỗi MM:SS.MS
        /// </summary>
        private string FormatTime(float timeInSeconds)
        {
            System.TimeSpan t = System.TimeSpan.FromSeconds(timeInSeconds);
            return string.Format("{0:0}:{1:00}.{2:000}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
        }

        private System.Collections.IEnumerator NotificationRoutine(string message, Color color, float duration)
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
            _floatNotificationText.rectTransform.DOAnchorPos(_originalCountdownPos, fadeTime).SetEase(DG.Tweening.Ease.Linear).SetUpdate(true);
            
            yield return new WaitForSecondsRealtime(duration);

            // 3. Fade Out từ dưới lên (tiếp tục đi lên trên)
            _floatNotificationText.DOFade(0f, fadeTime).SetUpdate(true);
            _floatNotificationText.rectTransform.DOAnchorPos(_originalCountdownPos + new Vector2(0, moveOffset), fadeTime).SetEase(DG.Tweening.Ease.Linear).SetUpdate(true);

            yield return new WaitForSecondsRealtime(fadeTime);

            _floatNotificationText.text = "";
            _notificationCoroutine = null;
        }

        #endregion

        /// <summary>
        /// Cập nhật trạng thái hiển thị của GameplayHUD dựa trên trạng thái game và trạng thái AFK của người chơi cục bộ.
        /// </summary>
        private void UpdateGameplayHUDVisibility()
        {
            if (_logicManager == null || _gameplayHUD == null) return;

            GameState currentState = _logicManager.GetCurrentGameState();
            IPlayer localPlayer = _logicManager.LocalPlayer;
            if (localPlayer == null) return;

            // Người chơi chỉ thấy GameplayHUD nếu:
            // - Game đang Playing
            // - Và quan trọng nhất: HỌ KHÔNG Ở TRONG LOBBY
            bool isParticipating = currentState == GameState.Playing && !localPlayer.IsInLobby.Value;

            if (_gameplayHUD != null) _gameplayHUD.SetActive(isParticipating);
            if (_lobbyHUD != null) _lobbyHUD.SetActive(!isParticipating);
            
            // Khi ở màn hình Gameplay, luôn ẩn Voting Panel để tránh đè phím
            if (isParticipating) SetVotingButtonVisible(false);

            // Tự động đóng các modal khi vào gameplay
            if (isParticipating) HideAllModals();
        }
    }
    
}