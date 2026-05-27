using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using Core.Interfaces;
using Core; // For LANDiscovery and HomeUIManager
using UI; // For NotificationModalUI, this is self-reference but for clarity
using UI.Multiplayer;
using System.Linq;

namespace UI
{
    public class RoomInfoModalUI : MonoBehaviour
    {
        [Header("Room Details")]
        [SerializeField] private TMP_Text _roomIdText;
        [SerializeField] private TMP_Text _passcodeText;

        [Header("Player List")]
        [SerializeField] private Transform _playerListContent;
        [SerializeField] private GameObject _playerItemPrefab;

        [Header("Actions")]
        [SerializeField] private Button _leaveRoomButton;
        [SerializeField] private Button _resetCharacterButton;

        private IMultiplayerManager _manager;
        private IMultiplayerUIManager _uiManager;

        private void Start()
        {
            if (_leaveRoomButton != null) _leaveRoomButton.onClick.AddListener(OnLeaveClick);
            if (_resetCharacterButton != null) _resetCharacterButton.onClick.AddListener(OnResetCharacterClick);

            // Lấy reference tới UI Manager để gọi hàm AskConfirmation
            if (_uiManager == null)
            {
                _uiManager = FindObjectsByType<Component>().OfType<IMultiplayerUIManager>().FirstOrDefault();
            }
        }

        private void OnEnable()
        {
            SubscribeToManagerEvents(true);
            RefreshUI();
            _manager?.LocalPlayer?.DisableAbility(); // Khóa input khi hiện modal
        }

        private void OnDisable()
        {
            SubscribeToManagerEvents(false);
            _manager?.LocalPlayer?.EnableAbility(); // Mở lại input khi đóng modal
        }

        // UI Manager sẽ gọi hàm này ngay khi scene load xong
        public void SetManager(IMultiplayerManager manager)
        {
            _manager = manager;
        }

        private void SubscribeToManagerEvents(bool subscribe)
        {
            if (_manager == null) return;

            if (subscribe)
            {
                _manager.PlayerDataList.OnListChanged += OnPlayerDataListChanged;
                _manager.RoomId.OnValueChanged += OnRoomInfoChanged;
                _manager.Passcode.OnValueChanged += OnRoomInfoChanged;
            }
            else
            {
                _manager.PlayerDataList.OnListChanged -= OnPlayerDataListChanged;
                _manager.RoomId.OnValueChanged -= OnRoomInfoChanged;
                _manager.Passcode.OnValueChanged -= OnRoomInfoChanged;
            }
        }

        private void OnPlayerDataListChanged(NetworkListEvent<PlayerNetworkData> changeEvent) => RefreshUI();

        // Cải tiến: Chỉ Refresh khi giá trị thực sự thay đổi để tiết kiệm hiệu năng (Network Optimization)
        private void OnRoomInfoChanged(Unity.Collections.FixedString32Bytes previousValue, Unity.Collections.FixedString32Bytes newValue) 
        {
            RefreshUI();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            RefreshUI();
        }

        public void Hide() => gameObject.SetActive(false);

        public void RefreshUI()
        {
            if (_manager == null || !NetworkManager.Singleton.IsConnectedClient) return;

            // 1. Cập nhật Text thông tin phòng từ NetworkVariable
            if (_roomIdText != null) 
                _roomIdText.text = $"Room ID: {_manager.RoomId.Value}";
            
            if (_passcodeText != null)
            {
                string pass = _manager.Passcode.Value.ToString();
                // Cải tiến: Hiển thị thân thiện hơn
                _passcodeText.text = string.IsNullOrEmpty(pass) ? "Passcode: <color=grey>None</color>" : $"Passcode: <color=yellow>{pass}</color>";
            }

            // 2. Refresh danh sách người chơi (Sử dụng pooling nếu danh sách thay đổi quá thường xuyên)
            foreach (Transform child in _playerListContent) Destroy(child.gameObject);

            bool localIsHost = NetworkManager.Singleton.IsServer;

            foreach (var playerData in _manager.PlayerDataList)
            {
                GameObject itemObj = Instantiate(_playerItemPrefab, _playerListContent);
                RoomPlayerItemUI itemUI = itemObj.GetComponent<RoomPlayerItemUI>();

                string pName = playerData.PlayerName.ToString();
                bool isHost = playerData.IsHost;

                itemUI.Setup(playerData.ClientId, pName, isHost, localIsHost, this);
            }
        }

        public void KickPlayer(ulong clientId)
        {
            _manager?.RequestKickPlayer(clientId);
        }

        private void OnLeaveClick()
        {
            _manager?.RequestLeaveRoom();
        }

        private void OnResetCharacterClick()
        {
            if (_uiManager != null)
            {
                _uiManager.AskConfirmation("Reset your character? You will be sent to respawn point.", () =>
                {
                    if (_manager != null)
                    {
                        _manager.RequestResetPlayer();
                        Hide(); // Đóng modal ngay khi nhấn Reset
                    }
                });
            }
        }

        private void OnDestroy()
        {
            SubscribeToManagerEvents(false);
        }
    }
}