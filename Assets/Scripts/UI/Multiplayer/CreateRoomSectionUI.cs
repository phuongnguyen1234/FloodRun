using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections;
using Core;
using Core.Interfaces;
using System.Linq;
using Unity.Netcode.Transports.UTP;

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
        [SerializeField] private Toggle _usePasscodeToggle;
        [SerializeField] private TMP_InputField _passcodeInput;
        [SerializeField] private Button _createButton; // Gán nút Create trong Inspector

        private void Start()
        {
            // Cấu hình InputField cho passcode (chỉ cho phép nhập số và tối đa 6 ký tự)
            if (_passcodeInput != null)
            {
                _passcodeInput.characterLimit = 6;
                _passcodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                _passcodeInput.onValueChanged.AddListener(_ => ValidateInput());
            }

            // Lắng nghe sự kiện Checkbox để ẩn/hiện Input Passcode
            if (_usePasscodeToggle != null)
            {
                _usePasscodeToggle.onValueChanged.AddListener(OnPasscodeToggleChanged);
                _usePasscodeToggle.onValueChanged.AddListener(_ => ValidateInput());
                OnPasscodeToggleChanged(_usePasscodeToggle.isOn);
            }

            ValidateInput();

            // Lắng nghe sự kiện shutdown để bật lại nút nếu host thất bại
            if (NetworkManager.Singleton != null) {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnectResetUI;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnectResetUI;
            }
        }

        private void OnDisconnectResetUI(ulong id) => SetInteractions(true);

        private void OnPasscodeToggleChanged(bool isOn)
        {
            if (_passcodeInput != null)
                _passcodeInput.interactable = isOn;
        }

        /// <summary>
        /// Kiểm tra tính hợp lệ của input để bật/tắt nút Create.
        /// </summary>
        private void ValidateInput()
        {
            bool isPasscodeValid = true;
            if (_usePasscodeToggle != null && _usePasscodeToggle.isOn)
            {
                isPasscodeValid = _passcodeInput != null && _passcodeInput.text.Length >= 4 && _passcodeInput.text.Length <= 6;
            }

            if (_createButton != null) _createButton.interactable = isPasscodeValid;
        }

        /// <summary>
        /// Xử lý logic khi người chơi nhấn tạo phòng.
        /// </summary>
        public void CreateRoom()
        {
            SetInteractions(false);

            // 1. Lấy số lượng người chơi từ Dropdown
            int maxPlayers = 1;
            if (_maxPlayersDropdown != null)
            {
                string selectedOption = _maxPlayersDropdown.options[_maxPlayersDropdown.value].text;
                int.TryParse(selectedOption, out maxPlayers);
            }

            // 2. Xử lý Passcode
            string passcode = "";
            if (_usePasscodeToggle != null && _usePasscodeToggle.isOn)
            {
                passcode = _passcodeInput != null ? _passcodeInput.text : "";
                if (passcode.Length < 4 || passcode.Length > 6)
                {
                    Debug.LogWarning("[CreateRoom] Passcode must be between 4 and 6 digits.");
                    HomeUIManager.Instance.ShowNotification("Passcode must be 4 to 6 digits.");
                    SetInteractions(true); // Enable lại UI khi validation fail
                    return;
                }
            }

            // 3. Tạo ID phòng ngẫu nhiên (6 chữ số)
            string roomId = Random.Range(100000, 999999).ToString();

            // 4. Lấy thông tin Host
            string hostName = DataManager.Instance != null ? DataManager.Instance.Profile.PlayerName : "Player";

            Debug.Log($"[CreateRoom] Room Created by {hostName} | ID: {roomId} | Max Players: {maxPlayers} | Passcode: {(string.IsNullOrEmpty(passcode) ? "None" : passcode)}");

            // Đảm bảo dừng Listening của LANDiscovery nếu Host trước đó từng nhấn Tab "Join"
            LANDiscovery.Instance.StopListening();

            // Sử dụng Coroutine để khởi động Host an toàn
            StartCoroutine(StartHostRoutine(roomId, hostName, passcode, maxPlayers));
        }

        private void SetInteractions(bool state)
        {
            if (_createButton != null) _createButton.interactable = state;
            if (_maxPlayersDropdown != null) _maxPlayersDropdown.interactable = state;
            if (_usePasscodeToggle != null) _usePasscodeToggle.interactable = state;
            if (_passcodeInput != null) _passcodeInput.interactable = state && _usePasscodeToggle.isOn;
        }

        private IEnumerator StartHostRoutine(string roomId, string hostName, string passcode, int maxPlayers)
        {
            // 1. Tắt session cũ nếu đang chạy
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                // Đợi cho đến khi IsListening về false
                while (NetworkManager.Singleton.IsListening) yield return null;
                // Đợi thêm 1 frame nữa để đảm bảo socket đã giải phóng hoàn toàn
                yield return null;
            }

            // 2. Khởi động Host
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                // Thiết lập Host lắng nghe trên tất cả các card mạng (Wifi, Ethernet, Hotspot)
                transport.ConnectionData.Address = "0.0.0.0";
                transport.ConnectionData.ServerListenAddress = "0.0.0.0";
            }

            if (NetworkManager.Singleton.StartHost())
            {
                // Hiển thị loading screen khi bắt đầu chuyển scene
                if (HomeUIManager.Instance != null) HomeUIManager.Instance.ShowJoiningGameLoadingScreen(true);

                Debug.Log("<color=green>[CreateRoom]</color> Host started successfully. Port 7777 is now occupied.");

                // FIX: Chờ 1 frame để NetworkSceneManager kịp khởi tạo các handler nội bộ sau khi StartHost.
                // Nếu gọi LoadScene ngay lập tức, Netcode có thể bị NullReference khi tính toán các scene cần Unload.
                yield return null;

                if (!NetworkManager.Singleton.IsServer) yield break;

                MultiplayerRoomInfoCache.PendingRoomId = roomId;
                MultiplayerRoomInfoCache.PendingPasscode = passcode;
                MultiplayerRoomInfoCache.PendingMaxPlayers = maxPlayers; // Lưu thêm MaxPlayers
                MultiplayerRoomInfoCache.PendingHostName = hostName;     // Lưu thêm HostName

                NetworkManager.Singleton.SceneManager.LoadScene("Multiplayer", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
            else
            {
                Debug.LogError("<color=red>[CreateRoom]</color> Failed to start Host. Port 7777 might be already in use!");
                SetInteractions(true);
            }
        }
    }
}