using System;
using UnityEngine;
using TMPro;
using Core.Interfaces;
using System.Collections;
using UnityEngine.UI;
using Core;
using System.Linq;
using DG.Tweening;

namespace UI.Multiplayer
{
    /// <summary>
    /// Quản lý giao diện HUD trong Multiplayer.
    /// Tích hợp Chat, Voting UI và thông tin phòng.
    /// </summary>
    public partial class MultiplayerUIManager : MonoBehaviour, IMultiplayerUIManager
    {
        public static MultiplayerUIManager Instance { get; private set; }
        [SerializeField] private Slider _timeSlider;

        [Header("Loading Screens")]
        [SerializeField] private GameObject _mapLoadingPanel;
        [SerializeField] private GameObject _joiningRoomLoadingPanel;
        [SerializeField] private GameObject _backToMainMenuLoadingPanel;

        [Header("Map Loading Details")]
        [SerializeField] private Image _loadingPreviewImage;
        [Tooltip("Định dạng: <Tên map> [<Tier>] - <Tác giả>")]
        
        [SerializeField] private LobbyInfoBoard _lobbyInfoBoard; // Đối tượng bảng thông tin vật lý trong Scene
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
        private Color _originalPersonalTimeColor;
        private Color _originalPersonalTimeIconColor;


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
                if (_timeSlider != null) {
                    _timeSlider.minValue = 0;
                    _timeSlider.value = 0;
                }
                
                // Đảm bảo Modal Summary và Vote được gán manager và ở trạng thái ẩn lúc đầu
                if (_summaryModal != null) _summaryModal.gameObject.SetActive(false);
                if (_voteMapModal != null) 
                {
                    _voteMapModal.SetManager(_logicManager);
                    _voteMapModal.gameObject.SetActive(false);
                }
                
            }

            if (_floatNotificationText != null)
            {
                _originalCountdownPos = _floatNotificationText.rectTransform.anchoredPosition;
            }

            if (_buttonStepText != null) _originalButtonTextColor = _buttonStepText.color;
            if (_buttonIcon != null) _originalButtonIconColor = _buttonIcon.color;
            if (_playerCountText != null) _originalPlayerCountColor = _playerCountText.color;
            if (_playerCountIcon != null) _originalPlayerIconColor = _playerCountIcon.color;
            if (_personalTimeText != null) _originalPersonalTimeColor = _personalTimeText.color;
            if (_personalTimeIcon != null) _originalPersonalTimeIconColor = _personalTimeIcon.color;
            
            _prevActivatedCount = 0;
            _prevAliveCount = 0;

            if (_openVoteModalButton != null)
            {
                _openVoteModalButton.onClick.AddListener(OpenVotingModal);
            }

            InitializeChat();
        }

        private void Update()
        {
            UpdateChat();
        }

        /// <summary>
        /// Kiểm tra xem có bất kỳ Modal nào đang mở gây chặn tương tác không.
        /// </summary>
        public bool IsAnyModalOpen()
        {
            return (_roomInfoModal != null && _roomInfoModal.gameObject.activeSelf) ||
                   (_settingsModal != null && _settingsModal.activeSelf) ||
                   (_voteMapModal != null && _voteMapModal.gameObject.activeSelf) ||
                   (_summaryModal != null && _summaryModal.gameObject.activeSelf) ||
                   IsChatFocused();
        }

        /// <summary>
        /// Hiển thị thông báo tạm thời (floating text).
        /// TODO: Implement với floating text system (color, duration)
        /// </summary>
        public void ShowFloatNotification(string message, Color color = default, float duration = 2f)
        {
            if (_floatNotificationText == null) return;
            if (_notificationCoroutine != null) StopCoroutine(_notificationCoroutine);
            _notificationCoroutine = StartCoroutine(NotificationRoutine(message, color, duration));
        }

        /// <summary>
        /// Update Timer slider
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="maxTime"></param>
        /// <param name="isVotePhase"></param>
        public void UpdateTimeSlider(float currentTime, float maxTime, bool isVotePhase = false)
        {
            if (_timeSlider != null)
            {
                _timeSlider.maxValue = maxTime;
                _timeSlider.value = Mathf.Clamp(currentTime, 0, maxTime);

                if (_timeSlider.fillRect != null)
                {
                    var img = _timeSlider.fillRect.GetComponent<Image>();
                    if (img != null) img.color = isVotePhase ? _timerVoteColor : _timerNormalColor;
                }
            }
        }

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
                _loadingMapNameAuthorText.color = themeColor;
            }

            if (_loadingButtonCountText != null)
                _loadingButtonCountText.text = data.ButtonNumber.ToString();

            if (_loadingDifficultyText != null)
                _loadingDifficultyText.text = data.Difficulty.ToString("F1");
        }

        /// <summary>
        /// Cập nhật text "Waiting for players" trên Lobby World UI.
        /// </summary>
        public void SetWaitingForPlayersText(string text)
        {
            _lobbyInfoBoard?.SetPlayerCountText(text);
        }

        /// <summary>
        /// Cập nhật thông tin map trên Lobby World UI.
        /// </summary>
        public void UpdateLobbyWorldMapInfo(MapData data, float difficulty)
        {
            _lobbyInfoBoard?.UpdateMapInfo(data, difficulty, _palette);
        }

        /// <summary>
        /// Chỉ cập nhật phần độ khó trên bảng Lobby mà không ảnh hưởng đến tên hay ảnh map.
        /// </summary>
        public void UpdateLobbyDifficultyOnly(float difficulty)
        {
            _lobbyInfoBoard?.UpdateDifficultyDisplay(difficulty, _palette);
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

        #region Lobby HUD Actions

        public void OnShopClick()
        {
            PlayClickSound();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Định dạng số giây thành chuỗi MM:SS.MS
        /// </summary>
        private string FormatTime(float timeInSeconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(timeInSeconds);
            return string.Format("{0:0}:{1:00}.{2:000}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
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
    }
}