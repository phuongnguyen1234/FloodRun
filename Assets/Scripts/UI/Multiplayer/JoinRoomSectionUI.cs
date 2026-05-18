using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Collections.Generic;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện tham gia phòng bằng Room ID.
    /// </summary>
    public class JoinRoomSectionUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_InputField _roomIdInput;
        [SerializeField] private TMP_Text _lastUpdatedText;
        [SerializeField] private Transform _roomListContent;
        [SerializeField] private GameObject _roomItemPrefab;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private Button _manualJoinButton; // Gán nút Join cạnh ô nhập ID trong Inspector

        private float _timeSinceLastUpdate = 0f;
        private Coroutine _autoRefreshCoroutine;
        private Dictionary<string, RoomItemUI> _roomItemMap = new Dictionary<string, RoomItemUI>();

        private void Start()
        {
            LANDiscovery.Instance.StartListening();

            // Đăng ký sự kiện cập nhật từ LANDiscovery để refresh ngay lập tức
            LANDiscovery.Instance.OnRoomsUpdated += RefreshRoomList;

            // Cấu hình InputField cho Room ID (chỉ cho phép nhập số và tối đa 6 ký tự giống CreateRoom)
            if (_roomIdInput != null)
            {
                _roomIdInput.characterLimit = 6;
                _roomIdInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                
                // Cho phép ấn Enter trên bàn phím để Join nhanh
                _roomIdInput.onSubmit.AddListener((_) => JoinRoom());
                // Cải tiến UX: Validate ngay khi người dùng đang nhập
                _roomIdInput.onValueChanged.AddListener(_ => ValidateManualJoin());
            }

            if (_refreshButton != null)
                _refreshButton.onClick.AddListener(RefreshRoomList);

            if (_manualJoinButton != null)
                _manualJoinButton.onClick.AddListener(JoinRoom);

            // Đăng ký sự kiện để biết khi nào kết nối thất bại để hiện lại nút
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            }

            ValidateManualJoin();
            _autoRefreshCoroutine = StartCoroutine(AutoRefreshRoutine());
        }

        private void OnDisable()
        {
            if (_autoRefreshCoroutine != null) StopCoroutine(_autoRefreshCoroutine);
            
            if (LANDiscovery.Instance != null)
            {
                LANDiscovery.Instance.OnRoomsUpdated -= RefreshRoomList;
            }

            // Giải phóng cổng UDP Discovery khi thoát khỏi tab Join
            LANDiscovery.Instance.StopListening();

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        private void Update()
        {
            _timeSinceLastUpdate += Time.deltaTime;
            if (_lastUpdatedText != null)
            {
                _lastUpdatedText.text = $"Rooms on this network (Last updated: {Mathf.FloorToInt(_timeSinceLastUpdate)} seconds ago)";
            }
        }

        private IEnumerator AutoRefreshRoutine()
        {
            // Refresh định kỳ chỉ là phương án dự phòng cho gói tin UDP bị mất
            while (true)
            {
                yield return new WaitForSeconds(10f);
                if (gameObject.activeInHierarchy) RefreshRoomList();
            }
        }

        public void RefreshRoomList()
        {
            _timeSinceLastUpdate = 0f;
            Debug.Log("[JoinRoom] Fetching rooms...");

            var discoveredRooms = LANDiscovery.Instance.DiscoveredRooms;
            
            // 1. Xóa các UI Item của phòng không còn tồn tại
            var roomsToRemove = _roomItemMap.Keys.Where(id => !discoveredRooms.ContainsKey(id)).ToList();
            foreach (var id in roomsToRemove)
            {
                if (_roomItemMap.TryGetValue(id, out var item))
                {
                    Destroy(item.gameObject);
                    _roomItemMap.Remove(id);
                }
            }

            // 2. Cập nhật phòng cũ hoặc sinh phòng mới
            foreach (var room in discoveredRooms.Values)
            {
                if (_roomItemMap.TryGetValue(room.RoomId, out var existingItem))
                {
                    existingItem.Setup(room.HostName, room.RoomId, room.CurrentPlayerCount, room.MaxPlayerCount, this);
                }
                else if (_roomItemPrefab != null)
                {
                    GameObject itemObj = Instantiate(_roomItemPrefab, _roomListContent);
                    RoomItemUI itemUI = itemObj.GetComponent<RoomItemUI>();
                    itemUI.Setup(room.HostName, room.RoomId, room.CurrentPlayerCount, room.MaxPlayerCount, this);
                    _roomItemMap.Add(room.RoomId, itemUI);
                }
            }
        }

        public void JoinRoomById(string roomId)
        {
            // Join trực tiếp từ danh sách phòng, không làm phiền ô nhập thủ công
            ExecuteJoin(roomId);
        }

        /// <summary>
        /// Xử lý logic khi người chơi nhấn Join Room.
        /// </summary>
        public void JoinRoom()
        {
            if (_roomIdInput == null) return;

            string roomId = _roomIdInput.text.Trim(); 

            if (roomId.Length != 6)
            {
                Debug.LogWarning("[JoinRoom] Room ID must be exactly 6 digits.");
                ShowNotification("Room ID must be exactly 6 digits.");
                return;
            }

            ExecuteJoin(roomId);
        }

        /// <summary>
        /// Kiểm tra độ dài Room ID để bật/tắt nút Join thủ công.
        /// </summary>
        private void ValidateManualJoin()
        {
            if (_manualJoinButton != null)
                _manualJoinButton.interactable = _roomIdInput != null && _roomIdInput.text.Length == 6;
        }

        private void ExecuteJoin(string roomId)
        {
            // Kiểm tra xem mã 6 số có khớp với IP nào đã dò được không
            if (LANDiscovery.Instance.DiscoveredRooms.TryGetValue(roomId, out DiscoveredRoom room))
            {
                // Kiểm tra phòng đầy trước khi xử lý
                if (room.CurrentPlayerCount >= room.MaxPlayerCount)
                {
                    Debug.LogWarning($"[JoinRoom] Room {roomId} is full ({room.CurrentPlayerCount}/{room.MaxPlayerCount}).");
                    ShowNotification("This room is full.");
                    return;
                }

                string connectionIP = room.IPAddress; // Tin tưởng hoàn toàn vào IP từ LANDiscovery

                if (room.HasPasscode)
                {
                    var mpUI = GetComponentInParent<MultiplayerUI>();
                    if (mpUI != null)
                    {
                        mpUI.ShowPasscodeModal((inputPass) =>
                        {
                            if (inputPass == room.Passcode)
                            {
                                // Passcode đúng → bắt đầu kết nối
                                LANDiscovery.Instance.StopListening();
                                mpUI.HidePasscodeModal(); // Đóng modal khi đã đúng
                                SetInteractions(false); // Disable UI khi bắt đầu connect
                                ConnectToIP(connectionIP);
                            }
                            else
                            {
                                // Passcode sai → chỉ hiển thị notification, không disable UI
                                ShowNotification("Incorrect passcode.");
                            }
                        });
                    }
                }
                else
                {
                    // Không cần passcode → bắt đầu kết nối ngay
                    LANDiscovery.Instance.StopListening();
                    SetInteractions(false); // Disable UI khi bắt đầu connect
                    ConnectToIP(connectionIP);
                }
            }
            else
            {
                Debug.LogWarning($"[JoinRoom] Room ID {roomId} not found on local network.");
                ShowNotification("Room ID not found or expired.");
            }
        }

        private void SetInteractions(bool state)
        {
            if (_roomIdInput != null) _roomIdInput.interactable = state;
            if (_refreshButton != null) _refreshButton.interactable = state;
            if (_manualJoinButton != null) _manualJoinButton.interactable = state;

            foreach (var item in _roomItemMap.Values)
            {
                if (item != null) item.SetInteractable(state);
            }

            // Disable Tab buttons ở MultiplayerUI
            var mpUI = GetComponentInParent<MultiplayerUI>();
            if (mpUI != null) mpUI.SetTabsInteractable(state);
        }

        private void ShowNotification(string message)
        {
            // Thử dùng MultiplayerUI trước (khi ở Multiplayer prefab)
            var mpUI = GetComponentInParent<MultiplayerUI>();
            if (mpUI != null)
            {
                mpUI.ShowNotification(message);
                return;
            }

            // Fallback: Dùng HomeUIManager (khi ở Home scene)
            if (HomeUIManager.Instance != null)
            {
                HomeUIManager.Instance.ShowNotification(message);
                return;
            }

            // Cuối cùng: chỉ log
            Debug.LogWarning($"[JoinRoom Notification] {message}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            // Nếu kết nối thất bại hoặc bị ngắt, bật lại các nút để người chơi thử lại
            SetInteractions(true);

            // Tắt loading screen nếu kết nối bị ngắt
            if (HomeUIManager.Instance != null) HomeUIManager.Instance.ShowJoiningGameLoadingScreen(false);

            Debug.LogWarning("[JoinRoom] Connection failed or disconnected.");
        }

        private void ConnectToIP(string ip)
        {
            Debug.Log($"[JoinRoom] Connecting to {ip}");
            // Hiển thị loading screen từ HomeUIManager
            if (HomeUIManager.Instance != null) HomeUIManager.Instance.ShowJoiningGameLoadingScreen(true);
            
            StartCoroutine(ConnectRoutine(ip));
        }

        private IEnumerator ConnectRoutine(string ip)
        {
            // 1. Tắt session cũ (nếu có)
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                while (NetworkManager.Singleton.IsListening) yield return null;
                yield return null;
            }

            // 2. FIX: Khi test trên localhost, cần dùng 127.0.0.1 thay vì broadcast IP
            // Vì cả host lẫn client chạy trên cùng máy (loopback)
            string connectionAddress = ip;
            if (ip != "127.0.0.1" && (ip.StartsWith("192.168") || ip.StartsWith("10.") || ip.StartsWith("172.")))
            {
                // Nếu là IP private, kiểm tra xem có localhost instance không (dev environment)
                // Thử connect tới 127.0.0.1 trước, nếu thất bại thì dùng broadcast IP
                Debug.Log("[JoinRoom] Detected private IP. Trying localhost first for local testing...");
                connectionAddress = "127.0.0.1";
            }

            // 3. Cấu hình Transport và Start Client
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.ConnectionData.Address = connectionAddress;
            Debug.Log($"[JoinRoom] Connecting to {connectionAddress}");

            if (!NetworkManager.Singleton.StartClient())
            {
                Debug.LogError($"[JoinRoom] Failed to start client connection to {connectionAddress}. Check NetworkManager settings and logs.");
                ShowNotification("Failed to connect. Check logs for details.");
                // Tắt loading nếu thất bại ngay lập tức
                if (HomeUIManager.Instance != null) HomeUIManager.Instance.ShowJoiningGameLoadingScreen(false);
                SetInteractions(true); // Re-enable UI if StartClient fails immediately
                yield break;
            }

            // 4. Đợi kết nối với timeout 15 giây
            float timeout = 15f;
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                if (NetworkManager.Singleton.IsConnectedClient)
                {
                    Debug.Log("[JoinRoom] Successfully connected to server!");
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Timeout - kết nối thất bại
            Debug.LogError($"[JoinRoom] Connection timeout after {timeout}s to {connectionAddress}");
            ShowNotification("Connection timeout. Server may be offline.");
            if (HomeUIManager.Instance != null) HomeUIManager.Instance.ShowJoiningGameLoadingScreen(false);

            // Shutdown network để cleanup
            NetworkManager.Singleton.Shutdown();
            SetInteractions(true);
        }
    }
}