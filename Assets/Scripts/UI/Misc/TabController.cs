using TMPro;
using UnityEngine;
using DG.Tweening;
using UnityEngine.Events;

namespace UI
{

    /// <summary>
    /// Quản lý các thành phần cơ bản của một hệ thống Tab hoặc Content View.
    /// Có thể được gắn vào Prefab chứa các Tab Button và Content Container.
    /// </summary>
    public class TabController : MonoBehaviour
    {
        [Header("UI References")]
        public TextMeshProUGUI headerText;
        public Transform contentContainer; // Vẫn giữ để các script khác có thể truy cập nếu cần

        [Header("Default Tab (Optional)")]
        [SerializeField] private GameObject _defaultContent;

        [Header("Modal Settings")]
        [SerializeField] private bool _destroyOnClose = false;
        [Tooltip("Sự kiện thực thi khi đóng (ví dụ: hiện lại màn hình Home)")]
        public UnityEvent OnCloseEvent;

        private void OnEnable()
        {
            if (_defaultContent != null) OpenContent(_defaultContent);
        }

        /// <summary>
        /// Xóa nội dung cũ và load một Prefab nội dung mới vào Modal.
        /// Thường dùng cho các Tab.
        /// </summary>
        /// <param name="contentPrefab">Prefab của trang nội dung (ví dụ: Map_Section, Stats_Section)</param>
        public void OpenContent(GameObject contentPrefab)
        {
            if (contentContainer == null)
            {
                Debug.LogWarning($"[TabController] Content Container chưa được gán cho {gameObject.name}!");
                return;
            }
            if (contentContainer == null) return;

            // 1. Xóa bỏ các nội dung cũ đang hiển thị trong container
            foreach (Transform child in contentContainer)
            {
                Destroy(child.gameObject);
            }

            // 2. Tạo nội dung mới từ Prefab
            if (contentPrefab != null)
            {
                GameObject newContent = Instantiate(contentPrefab, contentContainer);
                
                // Tối ưu cho UI: Reset RectTransform thay vì transform thông thường
                RectTransform rect = newContent.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = Vector2.zero;
                    rect.DOKill(); // Dừng mọi tween đang chạy trên RectTransform này
                    
                    // Đảm bảo content mới luôn khớp với container
                    rect.sizeDelta = Vector2.zero; 
                    // Tạo hiệu ứng nhỏ lúc xuất hiện cho mượt
                    rect.localScale = Vector3.one * 0.95f; 
                    rect.DOScale(Vector3.one, 0.1f).SetUpdate(true);
                }
            }
        }

        public void Close()
        {
            OnCloseEvent?.Invoke();

            if (_destroyOnClose) Destroy(gameObject);
            else gameObject.SetActive(false);
        }
    }
}
