using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using System;

namespace UI
{
    /// <summary>
    /// Modal hiển thị thông báo chung (Lỗi, Cảnh báo, Thông tin).
    /// Được thiết kế như một Singleton để gọi toàn cục.
    /// </summary>
    public class NotificationModalUI : MonoBehaviour
    {
        [Header("Notification Settings")]
        [SerializeField] private TMP_Text _contentText;
        [SerializeField] private Button _closeButton;

        private Action _onCloseCallback;

        private void Awake()
        {
            // Đảm bảo Modal không chặn raycast khi đang ẩn
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Hiển thị thông báo với nội dung tùy chỉnh.
        /// </summary>
        public void ShowMessage(string message, Action onClose = null)
        {
            if (_contentText != null) _contentText.text = message;
            _onCloseCallback = onClose;
            
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveAllListeners();
                _closeButton.onClick.AddListener(Hide);
            }

            gameObject.transform.SetAsLastSibling(); // Đảm bảo luôn hiện trên cùng
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _onCloseCallback?.Invoke();
            _onCloseCallback = null;
        }
    }
}