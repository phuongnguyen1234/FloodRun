using UnityEngine;
using Core.Interfaces;
using Core.Events;

/// <summary>
/// ExitRegion là một vùng đặc biệt trên bản đồ, khi Player đi vào sẽ kích hoạt sự kiện hoàn thành level nếu cửa đã được mở.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class ExitRegion : MonoBehaviour
{
    [Tooltip("Kéo một GameObject con chứa Collider (không phải Trigger) vào đây. Object này sẽ được bật lên để chặn cửa khi Player đi vào.")]
    [SerializeField] private GameObject _blockColliderObject;

    private void Awake()
    {
        // Đảm bảo Collider là Trigger
        GetComponent<BoxCollider2D>().isTrigger = true;

        // Đảm bảo vật cản ban đầu được tắt đi để Player đi vào được
        if (_blockColliderObject != null)
        {
            _blockColliderObject.SetActive(false);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. Debug để kiểm tra xem va chạm vật lý có xảy ra không (quan trọng)
        // Debug.Log($"Va chạm với: {other.gameObject.name} - Layer: {LayerMask.LayerToName(other.gameObject.layer)}");

        // 2. Tìm IPlayer: GetComponentInParent sẽ tìm trên chính object đó trước, sau đó mới tìm lên cha.
        // Cách này gọn và bao quát hơn logic cũ.
        IPlayer player = other.GetComponentInParent<IPlayer>();

        if (player != null)
        {
            // Kiểm tra thông qua MapManager xem cửa đã mở chưa
            if (MapManager.Instance != null)
            {
                if (MapManager.Instance.IsExitUnlocked)
                {
                    // CHỈ khi cửa đã mở thì mới set bất tử và chặn đường về
                    player.SetInvincible(true);

                    if (_blockColliderObject != null)
                    {
                        _blockColliderObject.SetActive(true);
                    }
                    else
                    {
                        Debug.LogWarning("ExitRegion: Chưa gán _blockColliderObject! Player có thể đi ngược ra ngoài.");
                    }

                    // Bắn sự kiện hoàn thành level ra toàn hệ thống
                    GameplayEvents.TriggerLevelCompleted(this);
                }
                else
                {
                    Debug.Log("Cần kích hoạt tất cả các nút để mở cửa thoát hiểm!");
                    // Có thể thêm hiển thị UI thông báo tại đây
                }
            }
        }
    }
}
