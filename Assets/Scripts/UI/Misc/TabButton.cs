using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Script bổ trợ cho các nút Tab. Hỗ trợ cả đổi Tab và mở Modal Overlap.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class TabButton : MonoBehaviour
    {
        [Header("References")]
        public TabController tabController;

        private Button _button;

        [Header("Tab Data")]
        public GameObject contentPrefab; 

        [Header("Overlap Settings")]
        [Tooltip("Nếu gán Prefab vào đây, nhấn nút sẽ Instantiate prefab này thay vì đổi content")]
        public GameObject overlapPrefab;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(SelectTab);

            if (tabController == null)
            {
                tabController = GetComponentInParent<TabController>();
            }
        }

        public void SelectTab()
        {
            // Ưu tiên 1: Nếu là nút mở Overlap (ví dụ: nút Settings mở thêm popup nhỏ)
            if (overlapPrefab != null)
            {
                Transform root = (tabController != null) ? tabController.transform.parent : transform.root;
                GameObject newObj = Instantiate(overlapPrefab, root);
                
                RectTransform rect = newObj.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = Vector2.zero;
                    rect.localScale = Vector3.one;
                }
                return;
            }

            // Ưu tiên 2: Đổi nội dung Tab
            if (tabController != null && contentPrefab != null)
            {
                tabController.OpenContent(contentPrefab);
            }
        }
    }
}
