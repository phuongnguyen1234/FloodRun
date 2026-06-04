using UnityEngine;
using Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;

/// <summary>
/// Vùng kích hoạt các thay đổi thuộc tính cho Player (Speed Boost) khi va chạm.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class SpeedBoost : MonoBehaviour
{
    [Header("Map Actions")]
    [Tooltip("Kéo MapAction_PlayerAttribute vào đây để cấu hình thay đổi tốc độ.")]
    [SerializeReference]
    private List<MapAction> _actions = new List<MapAction>();

    [Header("Events")]
    [Tooltip("Sự kiện nảy ra khi Local Player va chạm vùng trigger này.")]
    public UnityEvent OnBoostTriggered;

    private IGameLoopManager _gameLoop;
    private IMapManager _cachedMapManager;

    private void Awake()
    {
        // Đảm bảo BoxCollider2D là Trigger để Player đi xuyên qua được
        if (TryGetComponent<BoxCollider2D>(out var col))
        {
            col.isTrigger = true;
        }

        OnBoostTriggered ??= new UnityEvent();

        // Cache Managers sớm tại Awake để tối ưu hiệu suất (không tìm mỗi khi va chạm)
        _gameLoop = FindObjectsByType<MonoBehaviour>().OfType<IGameLoopManager>().FirstOrDefault();
        
        _cachedMapManager = GetComponentInParent<IMapManager>();
        _cachedMapManager ??= FindObjectsByType<MonoBehaviour>().OfType<IMapManager>().FirstOrDefault(m => (m as MonoBehaviour).gameObject.activeInHierarchy);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 1. Tìm IPlayer từ đối tượng va chạm (check cả cha để bao quát Collider chân/thân)
        IPlayer player = other.GetComponentInParent<IPlayer>();
        if (player == null || player.IsDead) return;

        if (_gameLoop == null) return;

        // 3. CHỈ kích hoạt nếu là Local Player (Người chơi máy này đang điều khiển)
        if (player != _gameLoop.LocalPlayer) return;

        // 4. Kiểm tra Mechanics của map đã bắt đầu chưa (tránh nhận boost khi đang 3s countdown)
        if (_cachedMapManager != null && !_cachedMapManager.IsMapMechanicsStarted()) return;

        // 5. Thực thi logic
        OnBoostTriggered?.Invoke();
        ExecuteActions(_cachedMapManager);
    }

    private void ExecuteActions(IMapManager manager)
    {
        if (_actions == null || _actions.Count == 0) return;

        foreach (var action in _actions)
        {
            if (action != null) StartCoroutine(action.ExecuteRoutine(manager));
        }
    }
}
