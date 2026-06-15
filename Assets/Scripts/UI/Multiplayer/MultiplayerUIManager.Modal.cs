using System;
using System.Collections.Generic;
using Core.Interfaces;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Multiplayer
{
    public partial class MultiplayerUIManager
    {
        [Header("Modals & Modifiers")]
        [SerializeField] private VoteMapModal _voteMapModal;
        [SerializeField] private SummaryModalUI _summaryModal;
        [SerializeField] private RoomInfoModalUI _roomInfoModal; 
        [SerializeField] private GameObject _settingsModal;
        [SerializeField] private Button _openVoteModalButton;
        [SerializeField] private NotificationModalUI _notificationPrefab;
        [SerializeField] private ConfirmationModalUI _confirmationPrefab;

        /// <summary>
        /// Mở modal vote map
        /// </summary>
        public void OpenVotingModal()
        {
            if (_voteMapModal == null) return;
            _voteMapModal.gameObject.SetActive(true);
            _voteMapModal.Setup();
        }

        /// <summary>
        /// Set nút vote trên modal
        /// </summary>
        /// <param name="visible"></param>
        public void SetVotingButtonVisible(bool visible)
        {
            if (_openVoteModalButton != null)
            {
                // FIX Bug 2: Nút Vote chỉ nhìn thấy ở giai đoạn Voting
                _openVoteModalButton.gameObject.SetActive(visible);
                _openVoteModalButton.interactable = visible;
            }

            // FIX: Kích hoạt panel Voting để Slider Global (nếu nằm trong đây) có thể hiển thị và chạy
            // Panel này chứa slider thời gian, nên nó cần active để slider chạy
            // Tuy nhiên, nếu nút vote luôn active, panel này có thể không cần active liên tục.
            if (_voteMapModal != null)
                _voteMapModal.gameObject.SetActive(visible);

            // Nếu bắt đầu đợt vote mới (visible = true), reset trạng thái vote của local player
            if (visible && _voteMapModal != null)
            {
                _voteMapModal.ResetVoteStatus();
            }

            // Đảm bảo Modal luôn đóng khi trạng thái Voting thay đổi (bắt đầu hoặc kết thúc)
            // Người chơi sẽ phải click nút Vote thủ công để mở lại.
            if (_voteMapModal != null) _voteMapModal.gameObject.SetActive(false);
        }

        /// <summary>
        /// Hiện modal summary
        /// </summary>
        /// <param name="results"></param>
        public void ShowSummary(List<RoundSummaryData> results)
        {
            if (_summaryModal == null) 
            {
                return;
            }
            _summaryModal.gameObject.SetActive(true); // Bật GameObject lên trước
            _summaryModal.Show(results);
        }

        /// <summary>
        /// Hiện modal thông tin room
        /// </summary>
        /// <param name="show"></param>
        public void ShowRoomInfo(bool show)
        {
            if (_roomInfoModal == null) return;
            if (show) _roomInfoModal.Show();
            else _roomInfoModal.Hide();
        }

        /// <summary>
        /// Hiện modal settings
        /// </summary>
        /// <param name="show"></param>
        public void ShowSettings(bool show)
        {
            if (_settingsModal != null) _settingsModal.SetActive(show);
            // FIX: Việc khóa input bây giờ do MultiplayerManagerNew quản lý tập trung trong Update
        }

        /// <summary>
        /// Dọn dẹp tất cả các Modal đang mở để tránh đè lên nhau.
        /// </summary>
        public void HideAllModals()
        {
            ShowRoomInfo(false);
            ShowSettings(false);
        }

        /// <summary>
        /// Hiển thị modal thông báo xác nhận 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="onYes"></param>
        public void AskConfirmation(string message, Action onYes)
        {
            if (_confirmationInstance == null && _confirmationPrefab != null)
            {
                _confirmationInstance = Instantiate(_confirmationPrefab, transform);
            }
            
            // Setup của ConfirmationModalUI chỉ nhận 2 tham số: message và action cho nút Yes
            if (_confirmationInstance != null) _confirmationInstance.Setup(message, onYes);
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
    }
}