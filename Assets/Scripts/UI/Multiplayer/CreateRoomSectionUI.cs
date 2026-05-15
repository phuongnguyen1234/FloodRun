using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện tạo phòng trong Multiplayer.
    /// Cho phép cấu hình số lượng người chơi và mã bảo mật (Passcode).
    /// </summary>
    public class CreateRoomSectionUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_Dropdown _maxPlayersDropdown;
        [SerializeField] private TMP_InputField _passcodeInput;

        private void Start()
        {
            // Cấu hình InputField cho passcode (chỉ cho phép nhập số và tối đa 6 ký tự)
            if (_passcodeInput != null)
            {
                _passcodeInput.characterLimit = 6;
                _passcodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            }
        }

        /// <summary>
        /// Xử lý logic khi người chơi nhấn tạo phòng.
        /// Hiện tại chỉ kiểm tra tính hợp lệ của dữ liệu và log ra console.
        /// </summary>
        public void CreateRoom()
        {
            // 1. Lấy số lượng người chơi từ Dropdown
            int maxPlayers = 1; // Giá trị mặc định, cho trường hợp player muốn chơi đơn nhưng độ khó động
            if (_maxPlayersDropdown != null)
            {
                string selectedOption = _maxPlayersDropdown.options[_maxPlayersDropdown.value].text;
                int.TryParse(selectedOption, out maxPlayers);
            }

            // 2. Lấy Passcode
            string passcode = _passcodeInput != null ? _passcodeInput.text : "";

            // 3. Kiểm tra tính hợp lệ cơ bản
            if (passcode.Length < 6)
            {
                Debug.LogWarning("[CreateRoom] Passcode must be exactly 6 digits.");
                // Bạn có thể thêm UI thông báo lỗi tại đây giống như StatsSectionUI
                return;
            }

            // 4. Thực hiện logic Create Room
            Debug.Log($"[CreateRoom] Creating room... | Max Players: {maxPlayers} | Passcode: {passcode}");
            
            // TODO: Gọi NetworkManager.CreateRoom(maxPlayers, passcode) tại đây khi tích hợp Multiplayer backend
        }
    }
}