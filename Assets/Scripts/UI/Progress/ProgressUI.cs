using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện người dùng cho màn hình tiến trình (Progress Screen).
    /// </summary>
    public class ProgressUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button _closeButton;
        [Tooltip("Kéo TabController của màn hình này vào đây")]
        [SerializeField] private TabController _tabController; // Tham chiếu đến TabController bên trong Progress_Screen


        private void Start()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }

            // Tự động chọn tab mặc định khi màn hình Progress được mở
            if (_tabController != null && _tabController.contentContainer.childCount == 0)
            {
                _tabController.OpenContent(_tabController.GetComponentInChildren<TabButton>()?.contentPrefab);
            }
        }

        public void Close()
        {
            // 1. Trước khi hủy, ra lệnh cho HomeUIManager hiện lại màn hình chính
            if (HomeUIManager.Instance != null)
            {
                HomeUIManager.Instance.ShowHomeScreen();
            }
            
            Destroy(gameObject);
        }
    }
}