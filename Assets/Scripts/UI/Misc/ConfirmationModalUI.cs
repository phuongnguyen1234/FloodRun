using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;

namespace UI
{
    /// <summary>
    /// Modal xác nhận hành động (Ví dụ: Thoát phòng, Kick người chơi).
    /// </summary>
    public class ConfirmationModalUI : MonoBehaviour
    {
        public static ConfirmationModalUI Instance { get; private set; }

        [Header("Confirmation Components")]
        [SerializeField] private TMP_Text _promptText;
        [SerializeField] private Button _yesButton;
        [SerializeField] private Button _noButton;

        private Action _onYesCallback;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (_yesButton != null) _yesButton.onClick.AddListener(() => { _onYesCallback?.Invoke(); Hide(); });
            if (_noButton != null) _noButton.onClick.AddListener(Hide);
            
            gameObject.SetActive(false);
        }

        public static ConfirmationModalUI GetInstance()
        {
            if (Instance != null) return Instance;
            
            // CÚ PHÁP MỚI: Chỉ truyền vào FindObjectsInactive.Include, bỏ hoàn toàn tham số SortMode
            Instance = FindObjectsByType<ConfirmationModalUI>(FindObjectsInactive.Include).FirstOrDefault();
            
            // Nếu vẫn không tìm thấy, log warning
            if (Instance == null)
            {
                Debug.LogWarning("[ConfirmationModalUI] Instance not found in scene!");
            }
            
            return Instance;
        }

        /// <summary>
        /// Hiển thị câu hỏi xác nhận và đăng ký hành động khi nhấn Yes.
        /// </summary>
        /// <param name="message">Câu hỏi hiển thị</param>
        /// <param name="onYes">Hành động thực thi khi chọn Yes</param>
        public void Setup(string message, Action onYes)
        {
            if (_promptText != null) _promptText.text = message;
            _onYesCallback = onYes;
            gameObject.SetActive(true);
        }

        public static void Ask(string message, Action onYes)
        {
            var modal = GetInstance();
            if (modal != null) modal.Setup(message, onYes);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}