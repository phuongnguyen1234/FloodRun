using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI
{
    /// <summary>
    /// Hiển thị thông tin của một phòng trong danh sách tìm kiếm.
    /// </summary>
    public class RoomItemUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_Text _roomInfoText; // "PlayerName's Room (1/6)"
        [SerializeField] private TMP_Text _roomIdText;   // "Room ID: 123456"
        [SerializeField] private Button _joinButton;
        [SerializeField] private Image _hostAvatar;    // Placeholder cho avatar

        private string _roomId;
        private JoinRoomSectionUI _parentUI;

        /// <summary>
        /// Khởi tạo dữ liệu cho Item.
        /// </summary>
        public void Setup(string hostName, string roomId, int currentPlayers, int maxPlayers, JoinRoomSectionUI parent)
        {
            _roomId = roomId;
            _parentUI = parent;

            if (_roomInfoText != null)
                _roomInfoText.text = $"{hostName}'s Room ({currentPlayers}/{maxPlayers})";
            
            if (_roomIdText != null)
                _roomIdText.text = $"Room ID: {roomId}";

            _joinButton.onClick.RemoveAllListeners();
            _joinButton.onClick.AddListener(() => {
                if (_parentUI != null) _parentUI.JoinRoomById(_roomId);
            });

            // Tự động disable nếu phòng đã đầy
            if (_joinButton != null)
                _joinButton.interactable = currentPlayers < maxPlayers;
        }

        /// <summary>
        /// Bật/Tắt nút Join của item này để tránh spam khi đang kết nối.
        /// </summary>
        public void SetInteractable(bool state)
        {
            if (_joinButton != null)
                _joinButton.interactable = state;
        }
    }
}