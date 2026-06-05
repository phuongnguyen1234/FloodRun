using UnityEngine;
using Core.Interfaces;
using Core.Events;
using Unity.Netcode;

/// <summary>
/// ExitRegion là một vùng đặc biệt trên bản đồ, khi Player đi vào sẽ kích hoạt sự kiện hoàn thành level nếu cửa đã được mở.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class ExitRegion : MonoBehaviour
{
    [Tooltip("Kéo một GameObject con chứa Collider (không phải Trigger) vào đây. Object này sẽ được bật lên để chặn cửa khi Player đi vào.")]
    [SerializeField] private GameObject _blockColliderObject;

    private IMapManager _mapManager; // FIX Bug 9: Reference to IMapManager

    private void Awake()
    {
        // Đảm bảo Collider là Trigger
        GetComponent<BoxCollider2D>().isTrigger = true;

        // Đảm bảo vật cản ban đầu được tắt đi để Player đi vào được
        if (_blockColliderObject != null)
        {
            _blockColliderObject.SetActive(false);
        }

        // FIX Bug 9: Get IMapManager reference
        _mapManager = GetComponentInParent<IMapManager>();
        if (_mapManager == null)
            Debug.LogError("[ExitRegion] IMapManager not found on map hierarchy!", this);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        IPlayer player = other.GetComponentInParent<IPlayer>();

        if (player == null || player.Status.Value == PlayerStatus.Finished) return;

        // MP: chỉ owner xử lý va chạm exit để tránh bắn event nhiều lần trên mọi client
        if (player is NetworkBehaviour netBehaviour
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && !netBehaviour.IsOwner)
            return;

        if (_mapManager != null)
        {
            // FIX: Chỉ cho phép Win khi map mechanics đã bắt đầu. 
            // Ngăn chặn việc kẹt status Finished khi chơi lại cùng một map (Loop cleanup).
            if (_mapManager.IsExitUnlocked && _mapManager.IsMapMechanicsStarted())
            {
                player.SetInvincible(true);

                if (_blockColliderObject != null)
                    _blockColliderObject.SetActive(true);
                else
                    Debug.LogWarning("ExitRegion: Chưa gán _blockColliderObject! Player có thể đi ngược ra ngoài.");

                player.Status.Value = PlayerStatus.Finished;
                GameplayEvents.TriggerLevelCompleted(player);
            }
            else
                Debug.Log("Cần kích hoạt tất cả các nút để mở cửa thoát hiểm!");
        }
    }
}
