using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace UI
{
    /// <summary>
    /// Modal yêu cầu nhập Passcode khi tham gia phòng bảo mật.
    /// Được quản lý cục bộ bởi JoinRoomSectionUI.
    /// </summary>
    public class EnterPasscodeModalUI : MonoBehaviour
    {
        [Header("Passcode Components")]
        [SerializeField] private TMP_InputField _passcodeInput;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton; // Thêm nút Cancel để thoát modal

        private Action<string> _onConfirmCallback;

        private void Start()
        {
            // Chỉ cho phép nhập số
            if (_passcodeInput != null)
            {
                _passcodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                _passcodeInput.characterLimit = 6;
            }
            
            if (_cancelButton != null) 
                _cancelButton.onClick.AddListener(Hide);
        }

        public void Setup(Action<string> onConfirm)
        {
            _onConfirmCallback = onConfirm;
            if (_passcodeInput != null) _passcodeInput.text = "";

            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveAllListeners();
                _confirmButton.onClick.AddListener(OnConfirmClick);
            }

            gameObject.SetActive(true);
        }

        private void OnConfirmClick()
        {
            if (_passcodeInput != null && _passcodeInput.text.Length >= 4)
            {
                _onConfirmCallback?.Invoke(_passcodeInput.text);
                // KHÔNG gọi Hide() ở đây, để JoinRoomSectionUI quyết định
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}