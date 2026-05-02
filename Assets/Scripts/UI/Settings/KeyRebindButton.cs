using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using Core;
using System;
using Core.Interfaces;
using System.Linq;
using static Core.SettingsManager;

/// <summary>
/// Script để quản lý việc gán lại phím cho một hành động cụ thể.
/// Gắn vào một Button UI có Text hiển thị phím hiện tại.
/// </summary>
public class KeyRebindButton : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text _bindingText;

    [Header("Asset Reference (For Home Screen)")]
    [Tooltip("Kéo file .inputactions vào đây để nút có thể hoạt động khi chưa có Player.")]
    [SerializeField] private InputActionAsset _actionAsset;

    [Header("Settings")]
    [Tooltip("Tên đầy đủ của Action trong Input Actions Asset (ví dụ: 'Player/Jump')")]
    [SerializeField] private string _actionName;
    [Tooltip("Loại Key Binding mà nút này đại diện (Jump, Dive, Slide)")]
    [SerializeField] private KeyBindingType _keyBindingType;

    private InputAction _actionToRebind;
    private int _bindingIndex;
    private InputActionRebindingExtensions.RebindingOperation _rebindOperation;

    private void Awake()
    {
        if (string.IsNullOrEmpty(_actionName))
        {
            Debug.LogError("KeyRebindButton: Action Name không được để trống!", this);
            enabled = false;
            return;
        }

        // 1. Sửa lỗi Obsolete: Sử dụng FindObjectsInactive.Include để tìm cả các object bị ẩn
        // 2. Fix lỗi Null ở màn hình Home: Nếu không thấy Player, ta tạo một instance Action tạm thời
        IInputProvider inputProvider = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OfType<IInputProvider>()
            .FirstOrDefault();
        
        if (inputProvider != null && inputProvider.ActionAsset != null)
        {
                // Ưu tiên sử dụng Asset đang chạy thực tế của Player
            _actionToRebind = inputProvider.ActionAsset.FindAction(_actionName);
        }
        else if (_actionAsset != null)
        {
            // Nếu không có Player (Màn Home), sử dụng Asset được gán cứng trong Inspector
            _actionToRebind = _actionAsset.FindAction(_actionName);
            ApplySavedBindingToAction();
        }

        if (_actionToRebind == null)
        {
            Debug.LogError($"KeyRebindButton: Không tìm thấy Action '{_actionName}' trong Input Actions Asset. " +
                           "Hãy đảm bảo PlayerInputHandler đã được khởi tạo hoặc _actionAsset đã được gán trong Inspector!", this);
            enabled = false; // Vô hiệu hóa script nếu không thể tìm thấy Action
            return;
        }
        // Xác định Index của binding
        _bindingIndex = _actionToRebind.bindings.IndexOf(x => x.action == _actionToRebind.name && !x.isComposite);

        if (_bindingIndex == -1)
        {
            Debug.LogWarning($"KeyRebindButton: Không tìm thấy binding chính cho Action '{_actionName}'. Sử dụng binding đầu tiên.", this);
            _bindingIndex = 0; // Fallback to first binding
        }
    }

    

    /// <summary>
    /// Áp dụng phím đã lưu trong SettingsManager vào Action hiện tại của nút này.
    /// Giúp UI hiển thị đúng phím kể cả khi chưa có Player.
    /// </summary>
    private void ApplySavedBindingToAction()
    {
        if (Instance == null || _actionToRebind == null) return;

        string savedPath = _keyBindingType switch
        {
            KeyBindingType.Jump => Instance.JumpKey,
            KeyBindingType.Dive => Instance.DiveKey,
            KeyBindingType.Slide => Instance.SlideKey,
            _ => ""
        };

        if (!string.IsNullOrEmpty(savedPath))
            _actionToRebind.ApplyBindingOverride(savedPath);
    }

    private void OnEnable()
    {
        UpdateBindingDisplay();
        if (Instance != null)
        {
            Instance.OnSettingsApplied += UpdateBindingDisplay;
        }
    }

    private void OnDisable()
    {
        if (Instance != null)
        {
            Instance.OnSettingsApplied -= UpdateBindingDisplay;
        }
        _rebindOperation?.Dispose(); // Đảm bảo operation được dọn dẹp
    }

    public void StartRebind()
    {
        if (_actionToRebind == null) return;

        _bindingText.text = "Press a Key...";

        // 1. Lưu lại trạng thái của Map trước khi rebind
        bool wasEnabled = _actionToRebind.actionMap.enabled;

        // 2. Vô hiệu hóa Action Map. 
        // Input System yêu cầu Action (hoặc Map chứa nó) phải ở trạng thái Disabled để Rebind.
        _actionToRebind.actionMap.Disable();

        _rebindOperation = _actionToRebind.PerformInteractiveRebinding(_bindingIndex)
            .WithControlsExcluding("<Pointer>") // Loại trừ chuột và cảm ứng (bao gồm cả di chuyển và click chuột)
            .WithControlsExcluding("<Gamepad>") // Loại trừ toàn bộ các thiết bị tay cầm
            .WithExpectedControlType("Key")     // Chỉ chấp nhận các phím (Key) từ bàn phím
            .OnComplete(operation =>
            {
                string newBindingPath = _actionToRebind.bindings[_bindingIndex].overridePath;
                Debug.Log($"Rebound '{_actionName}' to '{newBindingPath}'");

                // Cập nhật SettingsManager
                Instance.SetKeyBinding(_keyBindingType, newBindingPath);
                Instance.SaveSettings();

                operation.Dispose();
                _rebindOperation = null;
                
                // 3. Khôi phục lại trạng thái ban đầu của Map. 
                // Nếu trước đó Map đang tắt (do Game Pause) thì nó sẽ vẫn tắt.
                if (wasEnabled) _actionToRebind.actionMap.Enable();
                UpdateBindingDisplay();
            })
            .OnCancel(operation =>
            {
                Debug.Log("Rebind cancelled.");
                operation.Dispose();
                _rebindOperation = null;
                if (wasEnabled) _actionToRebind.actionMap.Enable();
                UpdateBindingDisplay(); // Quay lại hiển thị phím cũ
            })
            .Start();
    }

    private void UpdateBindingDisplay()
    {
        if (_actionToRebind == null || _bindingText == null) return;

        // Lấy tên hiển thị của binding hiện tại
        _bindingText.text = _actionToRebind.GetBindingDisplayString(_bindingIndex);
    }
}