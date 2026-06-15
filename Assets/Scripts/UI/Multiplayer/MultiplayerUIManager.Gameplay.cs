using System.Collections;
using Core.Interfaces;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Multiplayer
{
    public partial class MultiplayerUIManager
    {
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
        [SerializeField] private Button _spectateButton;

        [Space]
        [SerializeField] private Color _playActiveColor = Color.white;
        [SerializeField] private Color _playAfkColor = Color.yellow;
        [SerializeField] private Color _spectateNormalColor = Color.white;
        [SerializeField] private Color _spectateActiveColor = Color.red;

        [Header("Gameplay HUD (Common with SP)")]
        [SerializeField] private TMP_Text _personalTimeText;
        [SerializeField] private Image _personalTimeIcon;
        [SerializeField] private TMP_Text _bestRecordTimeText;
        [SerializeField] private TMP_Text _floatNotificationText;
        [Tooltip("Cờ hoàn thành map cho người chơi cục bộ")]
        [SerializeField] private GameObject _playerFinishFlag;

        /// <summary>
        /// Hiển thị số player còn lại trong round
        /// </summary>
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
        [SerializeField] private GameObject _buttonFinishFlag;

        /// <summary>
        /// Ẩn hiện HUD theo vị trí (lobby/map)
        /// </summary>
        /// <param name="isGameplay"></param>
        public void SetHUDMode(bool isGameplay)
        {
            UpdateGameplayHUDVisibility();
        }

        /// <summary>
        /// Cập nhật trạng thái hiển thị của GameplayHUD dựa trên trạng thái game và trạng thái AFK của người chơi cục bộ.
        /// </summary>
        private void UpdateGameplayHUDVisibility()
        {
            if (_logicManager == null || _gameplayHUD == null) return;

            GameState currentState = _logicManager.GetCurrentGameState();
            IPlayer localPlayer = _logicManager.LocalPlayer;
            if (localPlayer == null) return;

            // [HUD Visibility Logic Refactor]
            // Gameplay HUD hiện khi đang InGame hoặc đã Finished (về đích)
            bool isParticipating = localPlayer.Status.Value == PlayerStatus.InGame || 
                                 localPlayer.Status.Value == PlayerStatus.Finished;

            // Control visibility of main HUDs
            if (_gameplayHUD != null) _gameplayHUD.SetActive(isParticipating);
            if (_lobbyHUD != null) _lobbyHUD.SetActive(!isParticipating);
            
            // Nếu đang trong map chơi, ẩn các icon/nút không cần thiết của Lobby
            if (_playStatusText != null) _playStatusText.transform.parent.gameObject.SetActive(!isParticipating);
            
            if (_spectateStatusText != null)
            {
                _spectateStatusText.transform.parent.gameObject.SetActive(!isParticipating);
            }

            if (_spectateButton != null)
            {
                _spectateButton.interactable = currentState == GameState.Playing;
            }

            // FIX 1: LobbyInfoBoard luôn hiện thông tin map hiện tại
            _lobbyInfoBoard?.SetVisibility(true);

            // Tự động đóng các modal khi vào gameplay
            if (isParticipating) HideAllModals();
        }

        /// <summary>
        /// Toggle UI nút trạng thái
        /// </summary>
        /// <param name="isAFK"></param>
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

        /// <summary>
        /// Handle ấn nút Play
        /// </summary>
        public void OnPlayToggleClick()
        {
            PlayClickSound();
            _logicManager?.LocalPlayer?.ToggleAFKStatus();
        }

        /// <summary>
        /// Handle request bắt đầu game khi ấn Play
        /// </summary>
        public void OnGamePanelStartClick()
        {
            PlayClickSound();
            _logicManager?.RequestStartGame();
        }

        /// <summary>
        /// Reset gameplayHUD
        /// </summary>
        public void ResetGameplayHUD()
        {
            // 1. Ẩn các lá cờ hoàn thành (của người chơi và của nút bấm)
            ShowPlayerFinishFlag(false);
            ShowButtonFinishFlag(false);
            SetWaitingForPlayersText(""); 

            // Ẩn Summary Modal nếu đang hiện
            if (_summaryModal != null) _summaryModal.gameObject.SetActive(false);

            // FIX 1: Không ẩn LobbyInfoBoard khi reset, nó là vật thể thế giới luôn hiện
            // _lobbyInfoBoard?.SetVisibility(false); 

            // 2. Reset các biến tracking hiệu ứng pulse để sẵn sàng cho thông số mới
            _prevActivatedCount = 0;
            _prevAliveCount = 0;

            // 3. Reset màu sắc thời gian và xóa text đếm ngược/thông báo
                if (_personalTimeText != null) _personalTimeText.color = _originalPersonalTimeColor;
            SetCountdownText("");
        }

        /// <summary>
        /// Update thông số air
        /// </summary>
        /// <param name="currentAir"></param>
        /// <param name="bonusAir"></param>
        /// <param name="bonusMax"></param>
        /// <param name="rate"></param>
        public void UpdateAirUI(float currentAir, float bonusAir, float bonusMax, float rate)
        {
            if (_airSlider != null)
                _airSlider.value = Mathf.Clamp(currentAir, 0, _airSlider.maxValue);

            // Cập nhật Text Drain Rate với màu sắc
            if (_airRateText != null)
            {
                UpdateAirRateUI(rate);
            }

            // Cập nhật lớp Bubble Air (Bonus) giống SingleplayerUIManager
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

        /// <summary>
        /// Update thông số air drain rate
        /// </summary>
        /// <param name="rate"></param>
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

        /// <summary>
        /// Update tiến độ button trong map
        /// </summary>
        /// <param name="current"></param>
        /// <param name="total"></param>
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
            bool isSequenceFinished = total > 0 && current >= total;

            if (isSequenceFinished)
            {
                _buttonStepText.text = "";
                if (_buttonFinishFlag != null) _buttonFinishFlag.SetActive(true);
            }
            else
            {
                _buttonStepText.text = (current + 1).ToString();
                if (_buttonFinishFlag != null) _buttonFinishFlag.SetActive(false);
            }
        }

        /// <summary>
        /// Pulse màu button HUD
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Update số lượng player trong round
        /// </summary>
        /// <param name="current"></param>
        /// <param name="total"></param>
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
   
        /// <summary>
        /// Pulse màu player (HUD)
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Hiện icon flag của button khi hết nút
        /// </summary>
        /// <param name="show"></param>
        public void ShowButtonFinishFlag(bool show)
        {
            if (_buttonFinishFlag != null) _buttonFinishFlag.SetActive(show);
        }

        /// <summary>
        /// Hiển thị text đếm ngược 3s
        /// </summary>
        /// <param name="text"></param>
        public void SetCountdownText(string text)
        {
            if (_floatNotificationText != null)
            {
                // FIX: Nếu lệnh này là xóa text (rỗng) mà đang có thông báo float đang chạy (Map Completed, kết quả round...), 
                // thì bỏ qua để không làm mất thông báo quan trọng đó.
                if (string.IsNullOrEmpty(text) && _notificationCoroutine != null) return;

                // Reset lại trạng thái của Text (tránh bị ảnh hưởng bởi tween notification)
                _floatNotificationText.DOKill();
                _floatNotificationText.color = Color.white;
                _floatNotificationText.alpha = 1f;
                _floatNotificationText.rectTransform.anchoredPosition = _originalCountdownPos;
                _floatNotificationText.text = text;
            }
        }

        /// <summary>
        /// Update thời gian kỷ lục
        /// </summary>
        /// <param name="time"></param>
        public void UpdatePersonalRecord(float time)
        {
            if (_personalTimeText != null)
                _personalTimeText.text = FormatTime(time);
        }

        /// <summary>
        /// Hiển thị personal record (best time) giống như singleplayer.
        /// </summary>
        public void SetRecordTime(float bestTime, float maxMapTime)
        {
            if (_personalTimeText != null) _personalTimeText.color = _originalPersonalTimeColor;
            if (_personalTimeIcon != null) _originalPersonalTimeIconColor = _personalTimeIcon.color;

            if (_bestRecordTimeText != null)
                _bestRecordTimeText.text = FormatTime(bestTime > 0f ? bestTime : maxMapTime);
        }

        /// <summary>
        /// Hiển thị màu xanh timer khi hoàn thành map
        /// </summary>
        /// <param name="highlightAsVictory"></param>
        public void SetPersonalTimeHighlight(bool highlightAsVictory)
        {
            Color c = highlightAsVictory ? Color.green : _originalPersonalTimeColor;
            if (_personalTimeText != null) _personalTimeText.color = c;
            if (_personalTimeIcon != null) _personalTimeIcon.color = highlightAsVictory ? Color.green : _originalPersonalTimeIconColor;
        }

        /// <summary>
        /// Hiển thị icon flag của player
        /// </summary>
        /// <param name="show"></param>
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
    }
}