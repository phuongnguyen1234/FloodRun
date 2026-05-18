using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;
using System.Collections.Generic;
using System.Collections;
using Core; // For MapData and LevelManager
using Unity.Cinemachine; // For CinemachineCamera
using Core.Events; // For GameplayEvents
using Unity.Collections; // For FixedString
using UnityEngine.SceneManagement; // For SceneManager.LoadScene
using System.Linq; // For FindObjectsByType with LINQ

    /// <summary>
    /// Quản lý vòng lặp Game trong môi trường Multiplayer.
    /// Điều phối trạng thái: Lobby -> Voting -> Playing -> Summary.
    /// </summary>
    public class MultiplayerManager : NetworkBehaviour, IMultiplayerManager
    {
        public static MultiplayerManager Instance { get; private set; }

        public enum GameState { Lobby, Voting, Playing, Summary }
        
        [Header("Sync Variables")]
        public NetworkVariable<GameState> CurrentState = new NetworkVariable<GameState>(GameState.Lobby);
        
        // Đồng bộ thông tin phòng
        public NetworkVariable<FixedString32Bytes> RoomId { get; } = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<FixedString32Bytes> Passcode { get; } = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
        // Danh sách người chơi đồng bộ
        public NetworkList<PlayerNetworkData> PlayerDataList { get; private set; }
        
        // Triển khai từ IMultiplayerManager
        private NetworkVariable<float> _networkTime = new NetworkVariable<float>(0f);
        public NetworkVariable<float> NetworkTime => _networkTime;

        public int GetCurrentGameState() => (int)CurrentState.Value;

        public bool IsGameActive => CurrentState.Value == GameState.Playing;

        // --- New fields similar to GameplayManager ---
        [Header("Multiplayer Scene References")]
        [Tooltip("Kéo Virtual Camera từ Scene vào đây")]
        [SerializeField] private CinemachineCamera _vcam; 
        [Tooltip("Object trống dùng để chứa Map sau khi Instantiate để giữ Hierarchy gọn gàng")]
        [SerializeField] private Transform _mapParent;
        [Tooltip("Kéo object LobbySpawn trong scene vào đây")]
        [SerializeField] private PlayerSpawn _lobbySpawn;
        
        private GameObject _currentMapInstance; 
        private IMapManager _mapManager; // Reference to the instantiated map's manager

        private IMultiplayerUIManager _uiManager;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Khởi tạo NetworkList với quyền mặc định
            PlayerDataList = new NetworkList<PlayerNetworkData>(
                new List<PlayerNetworkData>(), 
                NetworkVariableReadPermission.Everyone, 
                NetworkVariableWritePermission.Server
            );

            // Đăng ký ở Awake để đảm bảo bắt được sự kiện ngay cả khi Player spawn cực sớm
            GameplayEvents.OnLocalPlayerSpawned += OnLocalPlayerSpawnedHandler;

            // Tìm UI Manager thông qua Interface (Pattern tương tự GameplayManager)
            if (_uiManager == null)
            {
                _uiManager = FindObjectsByType<Component>().OfType<IMultiplayerUIManager>().FirstOrDefault();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {   
                CurrentState.Value = GameState.Lobby;
            }

            // Host gán thông tin phòng từ dữ liệu tạm
            if (IsServer)
            {
                if (!string.IsNullOrEmpty(MultiplayerRoomInfoCache.PendingRoomId))
                {
                    SetRoomInfo(MultiplayerRoomInfoCache.PendingRoomId, MultiplayerRoomInfoCache.PendingPasscode);
                    MultiplayerRoomInfoCache.PendingRoomId = null;
                    MultiplayerRoomInfoCache.PendingPasscode = null;
                }

                // Đăng ký sự kiện thay đổi danh sách để cập nhật LAN Discovery
                PlayerDataList.OnListChanged += OnPlayerDataListChanged;
                LANDiscovery.Instance.UpdateBroadcastData(PlayerDataList.Count);

                // Server lắng nghe client thoát để xóa khỏi list
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedFromServer;
            }

            // FIX: Đối với Host/Local Client, Player thường spawn trước khi Manager này kịp lắng nghe sự kiện.
            // Kiểm tra nếu PlayerObject đã tồn tại thì thực hiện setup ngay.
            if (IsClient && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                if (NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent<IPlayer>(out var localPlayer))
                {
                    OnLocalPlayerSpawnedHandler(localPlayer);
                }
            }

            // Mỗi Client khi spawn sẽ gửi thông tin tên của mình lên cho Server
            if (IsClient)
            {
                string myName = DataManager.Instance != null ? DataManager.Instance.Profile.PlayerName : "Player " + NetworkManager.Singleton.LocalClientId;
                RegisterPlayerServerRpc(NetworkManager.Singleton.LocalClientId, myName, IsServer);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RegisterPlayerServerRpc(ulong clientId, string playerName, bool isHost)
        {
            // Kiểm tra xem đã tồn tại chưa để tránh trùng lặp
            for (int i = 0; i < PlayerDataList.Count; i++)
            {
                if (PlayerDataList[i].ClientId == clientId) return;
            }

            PlayerDataList.Add(new PlayerNetworkData
            {
                ClientId = clientId,
                PlayerName = playerName,
                IsHost = isHost
            });
        }

        private void OnPlayerDataListChanged(NetworkListEvent<PlayerNetworkData> changeEvent)
        {
            if (IsServer && LANDiscovery.Instance != null)
            {
                LANDiscovery.Instance.UpdateBroadcastData(PlayerDataList.Count);
            }
        }

        private void OnClientDisconnectedFromServer(ulong clientId)
        {
            // Nếu là server, xóa player khỏi danh sách data
            if (IsServer)
            {
                for (int i = 0; i < PlayerDataList.Count; i++)
                {
                    if (PlayerDataList[i].ClientId == clientId)
                    {
                        PlayerDataList.RemoveAt(i);
                        break;
                    }
                }
            }

            // NOTE: MultiplayerDisconnectHandler đảm nhận xử lý UI + scene load khi local client disconnect
            // Chúng ta chỉ cần cleanup data ở đây
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                Debug.Log("[MultiplayerManager] Local client disconnected, data cleaned up. Waiting for DisconnectHandler...");
            }
        }

        public override void OnDestroy()
        {
            GameplayEvents.OnLocalPlayerSpawned -= OnLocalPlayerSpawnedHandler;

            // Unsubscribe callback bất kể IsServer hay IsClient
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedFromServer;
            }

            // Server-specific cleanup
            if (IsServer && NetworkManager.Singleton != null)
            {
                if (PlayerDataList != null) PlayerDataList.OnListChanged -= OnPlayerDataListChanged;
            }

            // Dừng broadcasting khi host thoát
            if (IsServer && LANDiscovery.Instance != null)
            {
                LANDiscovery.Instance.StopBroadcasting();
            }
        }

        /// <summary>
        /// Host gọi hàm này để thiết lập thông tin phòng hiển thị cho mọi người.
        /// </summary>
        public void SetRoomInfo(string roomId, string passcode)
        {
            if (!IsServer) return;
            RoomId.Value = roomId;
            Passcode.Value = passcode;
            Debug.Log($"[MultiplayerManager] Room Info Synced: {roomId} | {passcode}");
        }

        private void Update()
        {
            if (!IsServer) return;

            // Server xử lý logic đếm ngược và chuyển trạng thái
            if (IsGameActive)
            {
                _networkTime.Value += Time.deltaTime;
            }
        }

        public void RequestStartGame() { if (IsServer) CurrentState.Value = GameState.Voting; }
        public void SendChatMessage(string message) { /* Logic gửi chat qua RPC */ }

        public void RequestKickPlayer(ulong clientId)
        {
            if (!IsServer) return;
            _uiManager?.AskConfirmation("Are you sure you want to kick this player?", () => {
                Debug.Log($"[MultiplayerManager] Kicking client {clientId}");
                NetworkManager.Singleton.DisconnectClient(clientId);
            });
        }

        public void RequestLeaveRoom()
        {
            _uiManager?.AskConfirmation("Do you want to leave the room?", () => {
                ExecuteLeave();
            });
        }

        private void ExecuteLeave()
        {
            if (IsServer)
            {
                LANDiscovery.Instance.StopBroadcasting();
            }

            _uiManager?.ShowBackToMainMenuLoadingScreen();
            
            NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene("Home");
        }

        private void OnLocalPlayerSpawnedHandler(IPlayer localPlayer)
        {
            // Sử dụng Coroutine để đợi các Component mạng (NetworkTransform) ổn định trước khi xử lý
            StartCoroutine(SetupLocalPlayerRoutine(localPlayer));
        }

        private IEnumerator SetupLocalPlayerRoutine(IPlayer localPlayer)
        {
            // Chờ 1 frame để đảm bảo Ownership và Physics của Player đã được Netcode xác nhận
            yield return null;

            // 1. Xử lý Spawn Position
            // Fallback: Nếu quên chưa kéo LobbySpawn vào Inspector, tự động tìm trong Scene
            if (_lobbySpawn == null) _lobbySpawn = FindAnyObjectByType<PlayerSpawn>();

            if (_lobbySpawn != null)
            {
                Vector3 spawnPos = _lobbySpawn.GetRandomSpawnPosition();
                localPlayer.Teleport(spawnPos);
                Debug.Log($"[MultiplayerManager] Local player teleported to LobbySpawn: {spawnPos}");
            }
            else Debug.LogWarning("[MultiplayerManager] LobbySpawn not found! Player will stay at default position.");

            // 2. Xử lý Camera Follow
            // Fallback: Tìm Virtual Camera trong scene nếu chưa gán
            if (_vcam == null) _vcam = FindAnyObjectByType<CinemachineCamera>();

            if (_vcam != null && localPlayer is MonoBehaviour playerMono)
            {
                Debug.Log($"[MultiplayerManager] Setting camera follow for: {playerMono.name}");
                _vcam.Priority = 10;

                // Ưu tiên sử dụng CinemachineTargetSetter nếu có gắn trên Camera
                var targetSetter = _vcam.GetComponent<CinemachineTargetSetter>();
                if (targetSetter != null)
                {
                    targetSetter.SetCameraTarget(playerMono.transform);
                }
                else
                {
                    _vcam.Follow = playerMono.transform;
                    _vcam.LookAt = playerMono.transform;
                }
            }
            else Debug.LogWarning("[MultiplayerManager] Virtual Camera or Local Player reference is missing.");

            // Tắt màn hình joining loading sau khi đã setup xong xuôi vị trí và camera
            _uiManager?.ShowJoiningLoadingScreen(false);
        }
    }
