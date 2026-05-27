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
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _cancelButton; // Thêm nút Cancel để thoát modal

        private Action<string> _onConfirmCallback;

        private void Start()
        {
            // Chỉ cho phép nhập số
            if (_passcodeInput != null)
            {
                _passcodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                _passcodeInput.characterLimit = 6;
                _passcodeInput.onValueChanged.AddListener(_ => ValidateInput());
            }
            
            if (_cancelButton != null) 
                _cancelButton.onClick.AddListener(Hide);
        }

        public void Setup(Action<string> onConfirm)
        {
            _onConfirmCallback = onConfirm;
            if (_passcodeInput != null) _passcodeInput.text = "";
            ValidateInput();

            if (_joinButton != null)
            {
                _joinButton.onClick.RemoveAllListeners();
                _joinButton.onClick.AddListener(OnConfirmClick);
            }

            gameObject.SetActive(true);
        }

        private void ValidateInput()
        {
            if (_joinButton != null)
                _joinButton.interactable = _passcodeInput != null && _passcodeInput.text.Length >= 4;
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