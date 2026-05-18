using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class MultiplayerUI : MonoBehaviour
    {
        [SerializeField] private Button[] _tabButtons; // Kéo các nút tab Create/Join vào đây
        [SerializeField] private EnterPasscodeModalUI _passcodeModalPrefab; 
        [SerializeField] private NotificationModalUI _notificationPrefab;

        private NotificationModalUI _notificationInstance;
        private EnterPasscodeModalUI _passcodeModalInstance;

        private void OnEnable()
        {
        }

        public void SetTabsInteractable(bool state)
        {
            if (_tabButtons == null) return;
            foreach (var btn in _tabButtons)
            {
                if (btn != null) btn.interactable = state;
            }
        }

        public void ShowPasscodeModal(Action<string> onConfirm)
        {
            if (_passcodeModalInstance == null && _passcodeModalPrefab != null)
            {
                _passcodeModalInstance = Instantiate(_passcodeModalPrefab, transform);
            }

            if (_passcodeModalInstance != null)
                _passcodeModalInstance.Setup(onConfirm);
        }

        public void HidePasscodeModal()
        {
            if (_passcodeModalInstance != null)
                _passcodeModalInstance.Hide();
        }

        public void ShowNotification(string message, Action onClose = null)
        {
            if (_notificationInstance == null && _notificationPrefab != null)
            {
                _notificationInstance = Instantiate(_notificationPrefab, transform);
            }

            if (_notificationInstance != null)
                _notificationInstance.ShowMessage(message, onClose);
        }

        public void Close()
        {
            if (HomeUIManager.Instance != null)
            {
                HomeUIManager.Instance.ShowHomeScreen();
            }

            Destroy(gameObject);
        }
    }
}
