using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện tham gia phòng bằng Room ID.
    /// </summary>
    public class JoinRoomSectionUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_InputField _roomIdInput;

        private void Start()
        {
            // Cho phép ấn Enter trên bàn phím để Join nhanh
            if (_roomIdInput != null)
                _roomIdInput.onSubmit.AddListener((_) => JoinRoom());
        }

        /// <summary>
        /// Xử lý logic khi người chơi nhấn Join Room.
        /// </summary>
        public void JoinRoom()
        {
            if (_roomIdInput == null) return;

            string roomId = _roomIdInput.text.Trim().ToUpper(); // Room ID thường không phân biệt hoa thường

            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogWarning("[JoinRoom] Room ID cannot be empty.");
                return;
            }

            // 1. Thực hiện logic Join Room
            Debug.Log($"[JoinRoom] Attempting to join room ID: {roomId}");

            // TODO: Gọi NetworkManager.JoinRoom(roomId) tại đây
            
            // Xóa input sau khi nhấn để sẵn sàng cho lần sau (nếu cần)
            // _roomIdInput.text = "";
        }
    }
}