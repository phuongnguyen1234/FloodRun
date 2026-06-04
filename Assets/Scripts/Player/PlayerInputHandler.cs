using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Core.Interfaces;
using Core.Events;
using Core;

/// <summary>
/// Xử lý đầu vào của người chơi, bao gồm di chuyển, nhảy, lặn và trượt. Cho phép thay đổi phím bấm thông qua SettingsManager.
/// </summary>
public class PlayerInputHandler : MonoBehaviour, IPlayerAbility, IInputProvider
{
    private PlayerInputActions _inputActions;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LadderInput { get; private set; }

    // Biến kiểm tra trạng thái giữ phím Space và Shift
    public bool JumpInput { get; private set; }
    public bool DiveInput { get; private set; }
    public bool SlideInput { get; private set; }

    public PlayerInputActions InputActions => _inputActions; // Expose for rebinding

    public InputActionAsset ActionAsset => _inputActions.asset;

    public event Action OnJump;

    void Awake()
    {
        _inputActions = new PlayerInputActions();
        ApplySettingsBindings();
    }

    void OnEnable()
    {
        _inputActions.Enable();

        _inputActions.Player.Move.performed += OnMove;
        _inputActions.Player.Move.canceled += OnMoveCanceled;
        _inputActions.Player.Ladder.performed += OnLadder;
        _inputActions.Player.Ladder.canceled += OnLadderCanceled;

        _inputActions.Player.Jump.performed += OnJumpPerformed;
        
        // Lắng nghe sự kiện bắt đầu và kết thúc nhấn nút Jump (Space) để xác định trạng thái giữ phím
        _inputActions.Player.Jump.started += OnJumpStarted;
        _inputActions.Player.Jump.canceled += OnJumpCanceled;

        // Đăng ký sự kiện Dive
        _inputActions.Player.Dive.started += OnDiveStarted;
        _inputActions.Player.Dive.canceled += OnDiveCanceled;

        // Đăng ký sự kiện Slide (Giả định bạn đã thêm Action 'Slide' trong Input Actions Asset)
        _inputActions.Player.Slide.started += OnSlideStarted;
        _inputActions.Player.Slide.canceled += OnSlideCanceled;

        // Subscribe to settings changes
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OnKeyBindingsChanged += ApplySettingsBindings;
        }
        GameplayEvents.OnPauseRequested += HandlePause;
    }

    private void OnMove(InputAction.CallbackContext context) => MoveInput = context.ReadValue<Vector2>();

    private void OnMoveCanceled(InputAction.CallbackContext context) => MoveInput = Vector2.zero;

    private void OnLadder(InputAction.CallbackContext context) => LadderInput = context.ReadValue<Vector2>();

    private void OnLadderCanceled(InputAction.CallbackContext context) => LadderInput = Vector2.zero;

    private void OnJumpPerformed(InputAction.CallbackContext context) => OnJump?.Invoke();

    private void OnJumpStarted(InputAction.CallbackContext context) => JumpInput = true;
    private void OnJumpCanceled(InputAction.CallbackContext context) => JumpInput = false;

    private void OnDiveStarted(InputAction.CallbackContext context) => DiveInput = true;
    private void OnDiveCanceled(InputAction.CallbackContext context) => DiveInput = false;

    private void OnSlideStarted(InputAction.CallbackContext context) => SlideInput = true;
    private void OnSlideCanceled(InputAction.CallbackContext context) => SlideInput = false;

    private void HandlePause(bool paused)
    {
        if (_inputActions == null) return;

        if (paused)
            _inputActions.Disable();
        else
            _inputActions.Enable();
    }

    void OnDisable()
    {
        if (_inputActions != null)
        {
            _inputActions.Player.Move.performed -= OnMove;
            _inputActions.Player.Move.canceled -= OnMoveCanceled;
            _inputActions.Player.Ladder.performed -= OnLadder;
            _inputActions.Player.Ladder.canceled -= OnLadderCanceled;
            _inputActions.Player.Jump.performed -= OnJumpPerformed;
            _inputActions.Player.Jump.started -= OnJumpStarted;
            _inputActions.Player.Jump.canceled -= OnJumpCanceled;
            _inputActions.Player.Dive.started -= OnDiveStarted;
            _inputActions.Player.Dive.canceled -= OnDiveCanceled;
            _inputActions.Player.Slide.started -= OnSlideStarted;
            _inputActions.Player.Slide.canceled -= OnSlideCanceled;

            // Explicitly disable Player action map to prevent memory leaks
            _inputActions.Player.Disable();
            _inputActions.Disable();
        }

        // Unsubscribe from settings changes
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OnKeyBindingsChanged -= ApplySettingsBindings;
        }
        GameplayEvents.OnPauseRequested -= HandlePause;
    }

    void OnDestroy()
    {
        // Giải phóng hoàn toàn InputActions để tránh leak và lỗi finalizer
        if (_inputActions != null)
        {
            try
            {
                // Ensure Player action map is disabled before disposing
                _inputActions.Player.Disable();
                _inputActions.Disable();
                _inputActions.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PlayerInputHandler] Error during input cleanup: {e.Message}");
            }
            finally
            {
                _inputActions = null;
            }
        }
    }

    public void EnableAbility()
    {
        _inputActions?.Enable();
    }

    public void DisableAbility()
    {
        ClearInputs();
        _inputActions?.Disable();
    }

    public void ClearInputs()
    {
        MoveInput = Vector2.zero;
        LadderInput = Vector2.zero;
        JumpInput = false;
        DiveInput = false;
        SlideInput = false;
    }

    /// <summary>
    /// Đè các phím bấm mặc định bằng các phím đã lưu trong SettingsManager.
    /// </summary>
    private void ApplySettingsBindings()
    {
        if (SettingsManager.Instance == null) return;

        // Sử dụng ApplyBindingOverride để thay đổi phím mà không cần mở Asset InputActions lên sửa
        // Path ví dụ: "<Keyboard>/space"
        _inputActions.Player.Jump.ApplyBindingOverride(SettingsManager.Instance.JumpKey);
        _inputActions.Player.Dive.ApplyBindingOverride(SettingsManager.Instance.DiveKey);
        _inputActions.Player.Slide.ApplyBindingOverride(SettingsManager.Instance.SlideKey);
    }
}