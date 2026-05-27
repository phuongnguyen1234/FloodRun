using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

namespace UI
{
    public class RoomPlayerItemUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private Image _avatarImage;
        [SerializeField] private TMP_Text _playerNameText;
        [SerializeField] private Button _kickButton;

        private ulong _clientId;
        private RoomInfoModalUI _parentModal;

        public void Setup(ulong clientId, string playerName, bool isHost, bool localIsHost, RoomInfoModalUI parent)
        {
            _clientId = clientId;
            _parentModal = parent;

            // 1. Hiển thị tên (Thêm prefix [Host] nếu là host theo design)
            _playerNameText.text = isHost ? $"[Host] {playerName}" : playerName;
            
            // 3. Logic nút Kick:
            // - Chỉ hiện nếu người đang xem là Host
            // - Không hiện nếu dòng này là chính bản thân Host (không tự kick mình)
            if (_kickButton != null)
            {
                bool isSelf = clientId == NetworkManager.Singleton.LocalClientId;
                _kickButton.gameObject.SetActive(localIsHost && !isSelf);
                
                _kickButton.onClick.RemoveAllListeners();
                _kickButton.onClick.AddListener(OnKickClick);
            }
            
            // Placeholder cho avatar
            if (_avatarImage != null) { /* Set sprite từ DataManager của client tương ứng */ }
        }

        private void OnKickClick()
        {
            // Gọi ngược lại modal để xử lý kick
            _parentModal?.KickPlayer(_clientId);
        }
    }
}