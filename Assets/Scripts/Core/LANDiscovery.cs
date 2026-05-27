using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;

namespace Core
{
    public class DiscoveredRoom
    {
        public string RoomId;
        public string HostName;
        public string IPAddress;
        public bool HasPasscode;
        public string Passcode; // Lưu để verify phía client (đối với LAN)
        public int CurrentPlayerCount = 1;
        public int MaxPlayerCount = 6;
        public DateTime LastSeen;
    }

    public class LANDiscovery : MonoBehaviour
    {
        public static LANDiscovery Instance { get; private set; }

        [SerializeField] private int _discoveryPort = 47777;
        private UdpClient _udpClient;
        private bool _isBroadcasting = false;
        private bool _isListening = false;

        // Sự kiện để UI đăng ký lắng nghe cập nhật ngay lập tức
        public event Action OnRoomsUpdated;

        // Cache cho việc broadcast (Host)
        private string _bRoomId;
        private string _bHostName;
        private string _bPasscode;
        private int _bMaxPlayers;
        private int _bCurrentPlayers;

        private bool _roomsChanged = false; // Flag đánh dấu dữ liệu đã thay đổi

        // Dictionary lưu trữ: RoomID -> Thông tin phòng
        public Dictionary<string, DiscoveredRoom> DiscoveredRooms = new Dictionary<string, DiscoveredRoom>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else Destroy(gameObject);
        }

        #region Host Logic (Broadcasting)
        public void StartBroadcasting(string roomId, string hostName, string passcode = "", int maxPlayers = 6)
        {
            _bRoomId = roomId;
            _bHostName = hostName;
            _bPasscode = passcode;
            _bMaxPlayers = maxPlayers;
            _bCurrentPlayers = 1; // Khởi tạo với 1 (Host)

            if (_isBroadcasting) return;
            _isBroadcasting = true;
            
            Task.Run(async () =>
            {
                using (UdpClient broadcaster = new UdpClient { EnableBroadcast = true })
                {
                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);

                    while (_isBroadcasting)
                    {
                        // Tạo thông điệp mới dựa trên dữ liệu đã cập nhật
                        string message = $"FLOODRUN|{_bRoomId}|{_bHostName}|{_bPasscode}|{_bMaxPlayers}|{_bCurrentPlayers}";
                        byte[] data = Encoding.UTF8.GetBytes(message);

                        await broadcaster.SendAsync(data, data.Length, endPoint);
                        await Task.Delay(2000); // Phát mỗi 2 giây
                    }
                }
            });
        }

        public void UpdateBroadcastData(int currentPlayers)
        {
            _bCurrentPlayers = currentPlayers;
        }

        public void StopBroadcasting()
        {
            if (!_isBroadcasting) return;

            // Lưu lại ID để gửi gói tin đóng phòng trước khi tắt hẳn broadcast
            string roomIdToClose = _bRoomId;
            
            Task.Run(async () =>
            {
                try
                {
                    using (UdpClient broadcaster = new UdpClient { EnableBroadcast = true })
                    {
                        IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
                        string closeMessage = $"FLOODRUN_CLOSE|{roomIdToClose}";
                        byte[] closeData = Encoding.UTF8.GetBytes(closeMessage);
                        await broadcaster.SendAsync(closeData, closeData.Length, endPoint);
                    }
                }
                catch { /* Bỏ qua lỗi socket khi shutdown */ }
            });

            _isBroadcasting = false;
        }
        #endregion

        #region Client Logic (Listening)
        public void StartListening()
        {
            if (_isListening) return;
            _isListening = true;
            DiscoveredRooms.Clear();

            // Cấu hình Socket để cho phép nhiều Instance chạy trên cùng 1 máy (Local Test)
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.ExclusiveAddressUse = false; 
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
            _udpClient.EnableBroadcast = true;

            Task.Run(async () =>
            {
                while (_isListening)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync();
                        string message = Encoding.UTF8.GetString(result.Buffer);
                        
                        if (message.StartsWith("FLOODRUN|"))
                        {
                            string[] parts = message.Split('|');
                            string roomId = parts[1];
                            string hostName = parts[2];
                            string passcode = parts.Length > 3 ? parts[3] : "";
                            int maxPlayers = (parts.Length > 4 && int.TryParse(parts[4], out int max)) ? max : 6;
                            int currentPlayers = (parts.Length > 5 && int.TryParse(parts[5], out int curr)) ? curr : 1;
                            string ip = result.RemoteEndPoint.Address.ToString();

                            lock (DiscoveredRooms)
                            {
                                bool isNew = !DiscoveredRooms.ContainsKey(roomId);
                                bool dataChanged = false;

                                if (!isNew)
                                {
                                    var existing = DiscoveredRooms[roomId];
                                    // Kiểm tra xem các thông tin quan trọng có thay đổi không
                                    if (existing.CurrentPlayerCount != currentPlayers || 
                                        existing.MaxPlayerCount != maxPlayers ||
                                        existing.HostName != hostName ||
                                        existing.HasPasscode != !string.IsNullOrEmpty(passcode))
                                    {
                                        dataChanged = true;
                                    }
                                }

                                // Luôn cập nhật thông tin để làm mới LastSeen (giữ phòng không bị timeout)
                                DiscoveredRooms[roomId] = new DiscoveredRoom
                                {
                                    RoomId = roomId,
                                    HostName = hostName,
                                    IPAddress = ip,
                                    HasPasscode = !string.IsNullOrEmpty(passcode),
                                    Passcode = passcode,
                                    MaxPlayerCount = maxPlayers,
                                    CurrentPlayerCount = currentPlayers,
                                    LastSeen = DateTime.Now
                                };

                                // Chỉ báo hiệu UI cập nhật nếu có thay đổi thực sự
                                if (isNew || dataChanged) _roomsChanged = true;
                            }
                        }
                        else if (message.StartsWith("FLOODRUN_CLOSE|"))
                        {
                            string[] parts = message.Split('|');
                            if (parts.Length > 1)
                            {
                                string closedRoomId = parts[1];
                                lock (DiscoveredRooms)
                                {
                                    if (DiscoveredRooms.Remove(closedRoomId))
                                        _roomsChanged = true;
                                }
                            }
                        }
                    }
                    catch (Exception) { /* Socket closed */ }
                }
            });
        }

        public void StopListening()
        {
            _isListening = false;
            _udpClient?.Close();
        }

        private void Update()
        {
            if (!_isListening) return;

            bool anyRemoved = false;

            // 1. Kiểm tra dọn dẹp mỗi giây (60 frames)
            if (Time.frameCount % 60 == 0)
            {
                lock (DiscoveredRooms)
                {
                    var now = DateTime.Now;
                    List<string> toRemove = new List<string>();
                    foreach (var room in DiscoveredRooms)
                    {
                        if ((now - room.Value.LastSeen).TotalSeconds > 5) toRemove.Add(room.Key);
                    }

                    if (toRemove.Count > 0)
                    {
                        foreach (var id in toRemove) DiscoveredRooms.Remove(id);
                        anyRemoved = true;
                    }
                }
            }

            // 2. Nếu có thay đổi (từ gói tin mới hoặc từ timeout), báo cho UI biết ngay lập tức
            if (_roomsChanged || anyRemoved)
            {
                _roomsChanged = false;
                OnRoomsUpdated?.Invoke();
            }
        }
        #endregion
    }
}