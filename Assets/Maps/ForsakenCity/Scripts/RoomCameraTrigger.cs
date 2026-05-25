using UnityEngine;
using Unity.Cinemachine; 
using Core.Interfaces;

[RequireComponent(typeof(Collider2D))]
public class RoomCameraTrigger : MonoBehaviour
{
    [SerializeField] private CinemachineCamera _roomCamera;
    [SerializeField] private int _activePriority = 20;
    [SerializeField] private bool _isStartingRoom = false;

    // Biến static lưu trữ phòng ĐANG HOẠT ĐỘNG. Dùng chung cho tất cả các phòng.
    private static RoomCameraTrigger _currentActiveRoom;

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

    private void OnTriggerEnter2D(Collider2D other)
    {
        IPlayer player = GetPlayer(other);
        
        // NẾU chạm vào Player VÀ phòng này CHƯA PHẢI là phòng đang active
        if (player != null && _currentActiveRoom != this)
        {
            ActivateRoom();
        }
    }

    // CHÚNG TA KHÔNG CẦN HÀM OnTriggerExit2D NỮA!

    private void ActivateRoom()
    {
        // 1. Tắt camera của phòng cũ (nếu có)
        if (_currentActiveRoom != null && _currentActiveRoom._roomCamera != null)
        {
            _currentActiveRoom._roomCamera.Priority = 0;
        }

        // 2. Gán phòng này làm phòng hiện tại và Bật camera lên
        _currentActiveRoom = this;
        if (_roomCamera != null)
        {
            _roomCamera.Priority = _activePriority;
        }
    }

    private void OnDestroy()
    {
        // Reset biến static khi Restart Game hoặc chuyển Map để tránh lỗi
        if (_currentActiveRoom == this) 
        {
            _currentActiveRoom = null;
        }
    }

    // Dò tìm IPlayer (bọc 3 lớp chống kẹt xương)
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