using UnityEngine;

/// <summary>
/// PlayerAnimator chịu trách nhiệm cập nhật các tham số của Animator dựa trên trạng thái của PlayerMotor và Rigidbody2D.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerMotor))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerAnimator : MonoBehaviour
{
    private Animator _animator;
    private PlayerMotor _motor;
    private Rigidbody2D _rb;

    // Sử dụng hash thay vì chuỗi để tăng hiệu suất
    private readonly int _animIDSpeed = Animator.StringToHash("Speed");
    private readonly int _animIDGrounded = Animator.StringToHash("IsGrounded");
    private readonly int _animIDVerticalVelocity = Animator.StringToHash("VerticalVelocity");
    private readonly int _animIDSwimming = Animator.StringToHash("IsSwimming");
    private readonly int _animIDDie = Animator.StringToHash("Die");
    private readonly int _animIDWallCling = Animator.StringToHash("IsClinging");
    private readonly int _animIDSliding = Animator.StringToHash("IsSliding");
    private readonly int _animIDClimbing = Animator.StringToHash("IsClimbing");
    private readonly int _animIDClimbSpeed = Animator.StringToHash("ClimbSpeed");
    private readonly int _animIDZiplining = Animator.StringToHash("IsZiplining");

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

    void Update()
    {
        // Cập nhật các tham số của Animator dựa trên trạng thái của Motor và Rigidbody
        UpdateAnimationParameters();

        // Cập nhật Sprite Label (Back, Side_Right, v.v.)
        _motor.UpdateSpriteLabels();
        _wasGotorGrounded = _motor.IsGrounded; // Cập nhật trạng thái grounded của motor cho frame tiếp theo
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
        bool isMovingUpward = _rb.linearVelocity.y > 0.1f;
        if (isMovingUpward)
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
        if (isMovingUpward) animatorGrounded = false; // Tuyệt đối không Grounded khi đang bay lên

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
        
        // 5. Climbing đặc thù
        _animator.SetBool(_animIDClimbing, _motor.IsClimbing);
        // Nếu đang leo, set tốc độ animation dựa trên input Y (hoặc vận tốc Y)
        if (_motor.IsClimbing)
        {
            _animator.SetFloat(_animIDClimbSpeed, Mathf.Abs(_rb.linearVelocity.y));
        }
    }

    public void TriggerDeath()
    {
        _animator.SetTrigger(_animIDDie);
    }
}
