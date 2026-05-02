using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

/// <summary>
/// Component này sẽ làm cho UI Button có hiệu ứng thu nhỏ khi nhấn và trở về kích thước ban đầu khi thả.
/// </summary>
public class UIButtonScalable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("Settings")]
    public float scaleTo = 0.95f; // Chỉ thu nhỏ X và Y xuống 0.95
    public float duration = 0.08f;

    public void OnPointerDown(PointerEventData eventData)
    {
        // Kill các tween cũ để tránh xung đột
        transform.DOKill();

        // CÁCH VIẾT CHUẨN: Tạo một Vector3 mới, giữ nguyên Z = 1
        Vector3 targetScale = new Vector3(scaleTo, scaleTo, 1f);
        
        // Hoặc cách viết ngắn gọn khác nếu origin là (1,1,1):
        // Vector3 targetScale = new Vector3(0.95f, 0.95f, 1f);

        transform.DOScale(targetScale, duration).SetEase(Ease.OutQuad);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        transform.DOKill();
        
        // Trở về scale chuẩn 2D: (1, 1, 1)
        transform.DOScale(Vector3.one, duration).SetEase(Ease.OutBack);
    }
}