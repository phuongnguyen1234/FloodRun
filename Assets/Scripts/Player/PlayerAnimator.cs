using UnityEngine;
using Unity.Netcode;

/// <summary>
/// PlayerAnimator chịu trách nhiệm cập nhật các tham số của Animator dựa trên trạng thái của PlayerMotor và Rigidbody2D.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerMotor))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerAnimator : NetworkBehaviour
{
    private Animator _animator;
    private PlayerMotor _motor;
    private Rigidbody2D _rb;

    // Sử dụng hash thay vì chuỗi để tăng hiệu suất
    private readonly int _animIDSpeed = Animator.StringToHash("Speed");
    private readonly int _animIDGrounded = Animator.StringToHash("IsGrounded");
    private readonly int _animIDVerticalVelocity = Animator.StringToHash("VerticalVelocity");
    private readonly int _animIDSwimming = Animator.StringToHash("IsSwimming");
    private readonly int _animIDWallCling = Animator.StringToHash("IsClinging");
    private readonly int _animIDSliding = Animator.StringToHash("IsSliding");
    private readonly int _animIDClimbing = Animator.StringToHash("IsClimbing");
    private readonly int _animIDClimbSpeed = Animator.StringToHash("ClimbSpeed");
    private readonly int _animIDZiplining = Animator.StringToHash("IsZiplining");
    private readonly int _animIDJumpInput = Animator.StringToHash("JumpInput");
    private readonly int _animIDDoJump = Animator.StringToHash("DoJump");

    private float _stableStateGraceTimer; // Buffer để tránh nháy animation Fall
    private float _landedBufferTimer; // Buffer để tránh nháy animation Idle sau khi tiếp đất
    private const float GRACE_TIME = 0.15f;
    private const float LANDED_BUFFER_TIME = 0.05f; // Thời gian buffer nhỏ sau khi tiếp đất
    private bool _wasSwimming; // Theo dõi trạng thái bơi frame trước
    private bool _wasGotorGrounded; // Theo dõi trạng thái grounded frame trước

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _motor = GetComponent<PlayerMotor>();
        _rb = GetComponent<Rigidbody2D>();

        // Đảm bảo animation không can thiệp vào vị trí thực tế của prefab
        // vì chúng ta đang điều khiển nhân vật bằng PlayerMotor (Physics)
        _animator.applyRootMotion = false;
    }

    private void OnEnable()
    {
        if (_motor != null) _motor.OnJumpTriggered += HandleJumpTriggered;
    }

    private void OnDisable()
    {
        if (_motor != null) _motor.OnJumpTriggered -= HandleJumpTriggered;
    }

    private void HandleJumpTriggered()
    {
        _animator.SetTrigger(_animIDDoJump);

        // FIX: Ép trực tiếp tham số để Animator "nhảy" state ngay trong frame này,
        // tránh việc đi qua Entry -> Idle của Sub-machine Locomotion.
        _animator.SetBool(_animIDGrounded, false);
        _animator.SetFloat(_animIDVerticalVelocity, 10f);
        
        // CỰC KỲ QUAN TRỌNG: Ép các biến đệm mặt đất về 0 ngay lập tức
        // Điều này chặn việc Animator tự động chuyển ngược về Idle/Run ngay trong frame đầu tiên
        _stableStateGraceTimer = 0;
        _landedBufferTimer = 0;
    }

    void Update()
    {
        // Chỉ Owner mới tính toán và set parameter cho Animator
        // NetworkAnimator sẽ tự đồng bộ các tham số này sang các máy khách khác
        if (!IsSpawned || IsOwner)
        {
            UpdateAnimationParameters();
        }

        // Visuals (Sprite swap + Rotation) cần chạy trên tất cả các máy để ai cũng thấy đúng hướng/loại sprite
        _motor.UpdateSpriteLabels();
        
        // FIX: Sync visual rotation cho proxy (flip + swimming rotation)
        if (IsSpawned && !IsOwner)
        {
            SyncProxyVisuals();
        }
        
        _wasGotorGrounded = _motor.IsGrounded; // Cập nhật trạng thái grounded của motor cho frame tiếp theo
    }
    
    /// <summary>
    /// Đồng bộ visual của proxy dựa trên NetworkVariable từ Owner
    /// </summary>
    private void SyncProxyVisuals()
    {
        // Sync Flip (Scale X)
        if (_motor._useScaleFlip)
        {
            Vector3 currentScale = transform.localScale;
            float targetScaleX = _motor.IsFacingRight ? Mathf.Abs(currentScale.x) : -Mathf.Abs(currentScale.x);
            if (!Mathf.Approximately(currentScale.x, targetScaleX))
            {
                currentScale.x = targetScaleX;
                transform.localScale = currentScale;
            }
        }
        
        // Sync Rotation (Swimming + Water Exit Reset)
        // FIX: Luôn sync rotation, không chỉ khi IsSwimming, vì ta cần sync cả rotation reset (90°) khi lên khỏi nước
        if (_motor._visualsRoot != null)
        {
            float targetRotZ = _motor.VisualsRotationZ;
            Quaternion targetRot = Quaternion.Euler(0, 0, targetRotZ);
            _motor._visualsRoot.localRotation = Quaternion.Lerp(
                _motor._visualsRoot.localRotation,
                targetRot,
                Time.deltaTime * 15f
            );
        }
    }

    private void UpdateAnimationParameters()
    {
        // Xác định xem nhân vật có đang ở trạng thái "ổn định" (chạm đất, bơi, leo, đu dây) hay không.
        // Biến này được dùng để quản lý Grace Timer (Coyote time) cho animation.
        bool isCurrentlyStable = _motor.IsGrounded || _motor.IsSwimming || _motor.IsZiplining || _motor.IsClimbing;
        
        // Kiểm tra xem có đang thực hiện một "Hành động đặc biệt" nào không
        bool isDoingSpecialAbility = _motor.IsSwimming || _motor.IsClimbing || _motor.IsZiplining || _motor.IsClinging || _motor.IsSliding;

        // 1. Cập nhật Grace Timer (Grounded state cho animation)
        if (_motor.IsGrounded)
            _stableStateGraceTimer = GRACE_TIME;
        else
            _stableStateGraceTimer -= Time.deltaTime;

        // 2. Xử lý Buffer tiếp đất (Tránh nháy Idle khi vừa chạm sàn)
        if (_motor.IsGrounded && !_wasGotorGrounded)
            _landedBufferTimer = LANDED_BUFFER_TIME;
        else if (_landedBufferTimer > 0)
            _landedBufferTimer -= Time.deltaTime;

        // 3. CẢI TIẾN QUAN TRỌNG: Nếu đang nhảy lên (vY dương), hủy ngay lập tức các buffer mặt đất
        // Hạ thấp ngưỡng từ 0.5 xuống 0.1 để nhạy hơn với cú nhảy.
        // CẢI TIẾN: Hạ thấp ngưỡng nhận diện từ 2.0f xuống 0.5f.
        // Điều này giúp Animator nhận ra nhân vật đang "bay" nhanh hơn khi được phóng đi từ Zipline/Nước.
        bool isJumpingUpward = _rb.linearVelocity.y > 0.5f;
        bool isMovingUpward = _rb.linearVelocity.y > 0.1f;

        if (isJumpingUpward && !_motor.IsGrounded)
        {
            _stableStateGraceTimer = 0;
            _landedBufferTimer = 0;
        }
        else if (!isDoingSpecialAbility && _stableStateGraceTimer > 0 && !isCurrentlyStable)
        {
            _stableStateGraceTimer = 0;
        }

        _wasSwimming = _motor.IsSwimming;

        // 4. Tính toán Speed hiển thị (Làm mượt để tránh nháy animation)
        float moveSpeed = _motor.IsSwimming || _motor.IsZiplining 
            ? _rb.linearVelocity.magnitude 
            : Mathf.Abs(_rb.linearVelocity.x);

        // Fix lỗi đứng yên khi đâm vào tường (nhưng vẫn nhấn phím) trong nước
        if (_motor.IsSwimming && _motor.InputMoveDirection.sqrMagnitude > 0.1f && moveSpeed < 1.0f)
            moveSpeed = 2.0f;

        if (moveSpeed < 0.1f) moveSpeed = 0f;
        _animator.SetFloat(_animIDSpeed, moveSpeed);

        // 5. Xác định trạng thái tiếp đất cho Animator
        bool animatorGrounded = _stableStateGraceTimer > 0 && _landedBufferTimer <= 0;
        
        // CẢI TIẾN: Nếu đang nhảy lên mạnh (vY > 2), ép Grounded về false ngay lập tức.
        // Điều này ngăn việc Animator bị "kẹt" ở trạng thái Grounded trong frame đầu tiên của cú nhảy.
        // CẢI TIẾN: Chỉ ép grounded về false nếu nhân vật thực sự KHÔNG chạm đất.
        // Khi chạy lên dốc, vY > 2 là bình thường, không nên chuyển sang animation Jump.
        if (isJumpingUpward && !_motor.IsGrounded) animatorGrounded = false;
        else if (isMovingUpward && !_motor.IsGrounded) animatorGrounded = false;

        // Khi đang bơi hoặc leo thang, ta coi như "Grounded" để không chạy animation ngã (Fall)
        _animator.SetBool(_animIDGrounded, animatorGrounded || _motor.IsSwimming || _motor.IsClimbing);

        // 6. Vận tốc dọc
        float verticalVel = (animatorGrounded && !isMovingUpward && !_motor.IsSwimming && !_motor.IsClimbing) ? 0f : _rb.linearVelocity.y;
        if (Mathf.Abs(verticalVel) < 0.1f) verticalVel = 0f;
        _animator.SetFloat(_animIDVerticalVelocity, verticalVel);

        // 4. Các trạng thái Boolean trực tiếp
        _animator.SetBool(_animIDSwimming, _motor.IsSwimming);
        _animator.SetBool(_animIDWallCling, _motor.IsClinging);
        _animator.SetBool(_animIDSliding, _motor.IsSliding);
        _animator.SetBool(_animIDZiplining, _motor.IsZiplining);
        _animator.SetBool(_animIDJumpInput, _motor.JumpInput);
        
        // 5. Climbing đặc thù
        _animator.SetBool(_animIDClimbing, _motor.IsClimbing);
        // Nếu đang leo, set tốc độ animation dựa trên input Y (hoặc vận tốc Y)
        if (_motor.IsClimbing)
        {
            _animator.SetFloat(_animIDClimbSpeed, Mathf.Abs(_rb.linearVelocity.y));
        }
    }
}