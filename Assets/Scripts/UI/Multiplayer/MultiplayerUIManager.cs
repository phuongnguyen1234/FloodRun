using System;
using UnityEngine;
using TMPro;
using Core.Interfaces;
using UnityEngine.UI;
using Core;
using System.Linq;
using UI;

namespace UI.Multiplayer
{
    /// <summary>
    /// Quản lý giao diện HUD trong Multiplayer.
    /// Tích hợp Chat, Voting UI và thông tin phòng.
    /// </summary>
    public class MultiplayerUIManager : MonoBehaviour, IMultiplayerUIManager
    {
        public static MultiplayerUIManager Instance { get; private set; }

        [Header("Multiplayer Specific UI")]
        [SerializeField] private GameObject _votingPanel;
        [SerializeField] private GameObject _chatPanel;
        [SerializeField] private TMP_Text _playerCountText;
        [SerializeField] private RoomInfoModalUI _roomInfoModal; 
        [SerializeField] private GameObject _settingsModal;

        [Header("Chat Settings")]
        [SerializeField] private GameObject _chatLinePrefab;
        [SerializeField] private Transform _chatContent;
        [SerializeField] private int _maxChatLines = 50;

        [Header("Global Modals")]
        [SerializeField] private NotificationModalUI _notificationPrefab;
        [SerializeField] private ConfirmationModalUI _confirmationPrefab;
        
        private NotificationModalUI _notificationInstance;
        private ConfirmationModalUI _confirmationInstance;

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
        [SerializeField] private DifficultyPalette _palette;

        [Header("Inherited HUD (Reused from SP)")]
        [SerializeField] private Slider _timeSlider;
        // Bạn có thể kéo các component từ HUD cũ vào đây

        [Header("Audio SFX")]
        [SerializeField] private AudioClip _clickSound;
        [SerializeField] private AudioSource _uiAudioSource;

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
            var logicManager = FindObjectsByType<MonoBehaviour>().OfType<IMultiplayerManager>().FirstOrDefault();
            if (logicManager != null)
            {
                if (_roomInfoModal != null) _roomInfoModal.SetManager(logicManager);
                // Bạn có thể gán cho các modal khác ở đây (ví dụ: ChatModal, VoteModal...)
                // if (_chatModal != null) _chatModal.SetManager(logicManager);
            }
        }

        // Giữ lại UpdateLevelProgress vì nó có triển khai cơ bản
        public void UpdateLevelProgress(float time) { if (_timeSlider != null) _timeSlider.value = time; }
        
        // Cập nhật để nhận callback khi đóng thông báo
        public void ShowNotification(string message, Action onClose = null)
        {
            if (_notificationInstance == null && _notificationPrefab != null)
            {
                _notificationInstance = Instantiate(_notificationPrefab, transform);
            }
            
            if (_notificationInstance != null) _notificationInstance.ShowMessage(message, onClose);
        }

        public void AskConfirmation(string message, Action onYes)
        {
            if (_confirmationInstance == null && _confirmationPrefab != null)
            {
                _confirmationInstance = Instantiate(_confirmationPrefab, transform);
            }
            
            if (_confirmationInstance != null) _confirmationInstance.Setup(message, onYes);
        }

        // Các hàm hỗ trợ Chat và Voting
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
            if (_playerCountText != null) 
                _playerCountText.text = $"{current}/{total} Players Alive";
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

        public void PlayClickSound()
    {
        if (_uiAudioSource != null && _clickSound != null)
        {
            // Lấy âm lượng SFX từ SettingsManager
            float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
            _uiAudioSource.PlayOneShot(_clickSound, volume);
        }
    }
    }
}