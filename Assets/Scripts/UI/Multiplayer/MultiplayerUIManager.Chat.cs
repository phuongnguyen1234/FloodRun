using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UI.Multiplayer
{
    /// <summary>
    /// Partial class xử lý các logic liên quan đến hệ thống Chat của Multiplayer HUD.
    /// Quản lý hiển thị tin nhắn, object pooling, cuộn tin nhắn và đếm số tin nhắn chưa đọc.
    /// </summary>
    public partial class MultiplayerUIManager
    {
        [Header("Chat System")]
        [SerializeField] private GameObject _chatPanel;
        [SerializeField] private GameObject _chatLinePrefab;
        [SerializeField] private Transform _chatContent;
        [SerializeField] private int _maxChatLines = 50;
        [SerializeField] private TMP_InputField _chatInputField;
        [SerializeField] private ScrollRect _chatScrollRect;

        [Header("Unread Messages")]
        [SerializeField] private GameObject _unreadBadgeObj;
        [SerializeField] private TMP_Text _unreadCountText;

        private Queue<GameObject> _chatLinePool = new Queue<GameObject>();
        private bool _preventRefocus = false;
        private int _unreadCount = 0;

        /// <summary>
        /// Khởi tạo các sự kiện cho hệ thống Chat.
        /// </summary>
        private void InitializeChat()
        {
            if (_chatInputField != null)
            {
                _chatInputField.onSubmit.AddListener(OnChatSubmit);
            }
        }

        /// <summary>
        /// Hiển thị hoặc ẩn khung Chat.
        /// Tự động reset bộ đếm tin nhắn chưa đọc và cuộn xuống dưới cùng khi mở.
        /// </summary>
        /// <param name="show">Trạng thái hiển thị mong muốn</param>
        public void ShowChat(bool show)
        {
            _chatPanel?.SetActive(show);
            if (show)
            {
                ResetUnreadCount();
                if (gameObject.activeInHierarchy)
                {
                    StartCoroutine(ScrollToBottom());
                }
            }
        }

        /// <summary>
        /// Chuyển đổi trạng thái đóng/mở của khung Chat.
        /// </summary>
        public void ToggleChat()
        {
            if (_chatPanel == null) return;
            ShowChat(!_chatPanel.activeSelf);
        }

        /// <summary>
        /// Thêm một tin nhắn mới vào khung Chat.
        /// Sử dụng Object Pooling để tái sử dụng các GameObject chat line nhằm tối ưu hiệu năng.
        /// Tự động cập nhật số lượng tin nhắn chưa đọc nếu khung Chat đang đóng, hoặc cuộn xuống dưới cùng nếu đang mở.
        /// </summary>
        /// <param name="sender">Tên người gửi</param>
        /// <param name="message">Nội dung tin nhắn</param>
        /// <param name="isHost">Xác định xem người gửi có phải là Host không (để làm nổi bật text)</param>
        public void AddChatMessage(string sender, string message, bool isHost)
        {
            if (_chatLinePrefab == null || _chatContent == null) return;

            GameObject lineObj;
            if (_chatLinePool.Count < _maxChatLines)
            {
                lineObj = Instantiate(_chatLinePrefab, _chatContent);
                _chatLinePool.Enqueue(lineObj);
            }
            else
            {
                lineObj = _chatLinePool.Dequeue();
                lineObj.transform.SetAsLastSibling();
                _chatLinePool.Enqueue(lineObj);
            }

            TMP_Text text = lineObj.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                string prefix = isHost ? "<color=yellow>[Host]</color> " : "";
                text.text = $"{prefix}<b>{sender}:</b> {message}";
            }

            if (_chatPanel != null && !_chatPanel.activeInHierarchy)
            {
                _unreadCount++;
                UpdateUnreadUI();
            }
            else if (gameObject.activeInHierarchy)
            {
                // Nếu chat đang mở, cuộn xuống dưới cùng
                StartCoroutine(ScrollToBottom());
            }
        }

        /// <summary>
        /// Coroutine cuộn khung Chat xuống vị trí dưới cùng (tin nhắn mới nhất).
        /// Cần đợi đến cuối frame để Content Size Fitter cập nhật chính xác kích thước trước khi cuộn.
        /// </summary>
        private IEnumerator ScrollToBottom()
        {
            // Phải đợi đến cuối frame để Content Size Fitter cập nhật kích thước mới
            yield return new WaitForEndOfFrame();
            if (_chatScrollRect != null)
            {
                _chatScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>
        /// Xử lý sự kiện khi người dùng nhấn Enter để gửi tin nhắn.
        /// Gửi dữ liệu qua Logic Manager, dọn dẹp ô nhập liệu và tạm ngưng việc tự động bật lại focus.
        /// </summary>
        /// <param name="text">Nội dung tin nhắn cần gửi</param>
        private void OnChatSubmit(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                _logicManager?.SendChatMessage(text);
                _chatInputField.text = "";
            }
            
            _chatInputField.DeactivateInputField();

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(PreventImmediateRefocus());
            }
        }

        /// <summary>
        /// Ngăn chặn việc tự động bật lại (refocus) khung chat ngay trong frame mà phím Enter vừa được dùng để gửi tin nhắn.
        /// </summary>
        private IEnumerator PreventImmediateRefocus()
        {
            _preventRefocus = true;
            yield return null;
            _preventRefocus = false;
        }

        /// <summary>
        /// Kiểm tra sự kiện bấm phím Enter mỗi frame để tự động mở và focus vào khung Chat.
        /// </summary>
        private void UpdateChat()
        {
            if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                if (_chatInputField != null && !_preventRefocus && !_chatInputField.isFocused)
                {
                    if (_chatPanel != null && !_chatPanel.activeInHierarchy)
                    {
                        ShowChat(true);
                    }
                    _chatInputField.ActivateInputField();
                }
            }
            // Bổ sung logic cho phím "/"
            else if (Keyboard.current != null && Keyboard.current.slashKey.wasPressedThisFrame)
            {
                if (_chatInputField != null && !_chatInputField.isFocused)
                {
                    if (_chatPanel != null && !_chatPanel.activeInHierarchy)
                    {
                        ShowChat(true);
                    }
                    _chatInputField.ActivateInputField();
                }
            }
        }
        
        /// <summary>
        /// Kiểm tra xem người chơi hiện tại có đang focus vào khung chat để nhập văn bản hay không.
        /// </summary>
        /// <returns>True nếu đang focus, False nếu không.</returns>
        private bool IsChatFocused()
        {
            return _chatInputField != null && _chatInputField.isFocused;
        }

        /// <summary>
        /// Cập nhật hiển thị UI của bộ đếm tin nhắn chưa đọc (ẩn/hiện badge và cập nhật số lượng).
        /// </summary>
        private void UpdateUnreadUI()
        {
            if (_unreadBadgeObj != null)
            {
                _unreadBadgeObj.SetActive(_unreadCount > 0);
            }
            if (_unreadCountText != null)
            {
                _unreadCountText.text = _unreadCount > 99 ? "99+" : _unreadCount.ToString();
            }
        }

        /// <summary>
        /// Đặt lại bộ đếm tin nhắn chưa đọc về 0 và cập nhật UI.
        /// </summary>
        private void ResetUnreadCount()
        {
            _unreadCount = 0;
            UpdateUnreadUI();
        }
    }
}
