 using UnityEngine;
using Unity.Cinemachine; 
using Core.Interfaces;
using Unity.Netcode; // Thêm Netcode để nhận diện Local Player

[RequireComponent(typeof(Collider2D))]
public class RoomCameraTrigger : MonoBehaviour
{
    [SerializeField] private CinemachineCamera _roomCamera;
    [SerializeField] private int _activePriority = 20;
    [SerializeField] private bool _isStartingRoom = false;

    // Biến static lưu trữ phòng ĐANG HOẠT ĐỘNG trên Client này
    private static RoomCameraTrigger _currentActiveRoom;
    
    // Lưu lại bộ Collider của chính phòng này để check trạng thái Player
    private Collider2D _roomCollider;

    private void Awake()
    {
        _roomCollider = GetComponent<Collider2D>();
    }

    private void Start()
    {
        // Khi game bắt đầu, hạ tất cả camera phòng về 0
        if (_roomCamera != null) _roomCamera.Priority = 0;

        // Nếu là phòng xuất phát, chiếm quyền ngay lập tức
        if (_isStartingRoom)
        {
            ActivateRoom();
        }
    }

    private void Update()
    {
        // CHỈ PHÒNG ĐANG ACTIVE MỚI CẦN CHECK XEM PLAYER CÒN Ở ĐÂY KHÔNG
        if (_currentActiveRoom != this) return;

        // Tìm Local Player trên máy này
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObj != null)
            {
                IPlayer player = localPlayerObj.GetComponent<IPlayer>();
                
                if (player != null)
                {
                    // SỬA LỖI MULTIPLAYER KHI CHẾT/TELEPORT:
                    // Nếu player đã chết HOẶC vị trí của player không còn nằm trong Trigger của phòng này nữa
                    if (player.IsDead || 
                        player.Status.Value == PlayerStatus.Dead || 
                        player.Status.Value == PlayerStatus.Lobby ||
                        !_roomCollider.OverlapPoint(localPlayerObj.transform.position))
                    {
                        DeactivateRoom();
                    }
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        IPlayer player = GetPlayer(other);
        
        // SỬA LỖI MULTIPLAYER: Chỉ xử lý nếu đối tượng chạm vào là LOCAL PLAYER (chính máy này)
        if (player != null && player is NetworkBehaviour netBehaviour && netBehaviour.IsLocalPlayer)
        {
            // Nếu thỏa mãn điều kiện và chưa phải phòng active, kèm theo player chưa chết
            if (_currentActiveRoom != this && !player.IsDead && player.Status.Value == PlayerStatus.InGame)
            {
                ActivateRoom();
            }
        }
    }

    private void ActivateRoom()
    {
        // 1. Tắt camera của phòng cũ (nếu có)
        if (_currentActiveRoom != null && _currentActiveRoom != this)
        {
            _currentActiveRoom.DeactivateRoom();
        }

        // 2. Gán phòng này làm phòng hiện tại và Bật camera lên
        _currentActiveRoom = this;
        if (_roomCamera != null)
        {
            _roomCamera.Priority = _activePriority;
        }
    }

    // Hàm chủ động nhả quyền Camera
    private void DeactivateRoom()
    {
        if (_roomCamera != null)
        {
            _roomCamera.Priority = 0;
        }
        if (_currentActiveRoom == this)
        {
            _currentActiveRoom = null;
        }
    }

    private void OnDestroy()
    {
        if (_currentActiveRoom == this) 
        {
            _currentActiveRoom = null;
        }
    }

    // Dò tìm IPlayer (giữ nguyên gốc của bạn)
    private IPlayer GetPlayer(Collider2D col)
    {
        IPlayer p = col.GetComponentInParent<IPlayer>();
        if (p != null) return p;

        if (col.attachedRigidbody != null)
        {
            p = col.attachedRigidbody.GetComponent<IPlayer>();
            if (p != null) return p;
        }

        return col.transform.root.GetComponent<IPlayer>();
    }
}