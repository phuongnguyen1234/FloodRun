using UnityEngine;

/// <summary>
/// Khả năng leo thang mới, sử dụng phương pháp quét OverlapBox để phát hiện thang, giúp tránh tình trạng bị kẹt khi đứng sát thang hoặc ở góc thang.
/// </summary>
public class LadderClimbAbilityNew : MonoBehaviour, IPlayerAbility
{
    [Header("Settings")]
    [SerializeField] private float _climbSpeed = 6f;
    [SerializeField] private float _jumpOffForce = 12f;
    
    [SerializeField] private LayerMask _ladderLayer;

    [Header("Detection")]
    [Tooltip("Khoảng cách sai số khi check đỉnh thang để bắt đầu snap.")]
    [SerializeField] private float _topSnapThreshold = 0.1f;

    private PlayerMotor _motor;
    private PlayerInputHandler _input;
    private PlayerController _controller;
    private Rigidbody2D _rb;
    private BoxCollider2D _bodyCollider;
    private Collider2D[] _allPlayerColliders;

    private bool _isAbilityEnabled = true;
    private bool _isClimbing = false;
    private ILadder _activeLadder;
    private ILadder _hoveredLadder; // Thang đang chạm vào trigger

    private void Awake()
    {
        _motor = GetComponent<PlayerMotor>();
        _input = GetComponent<PlayerInputHandler>();
        _controller = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody2D>();
        
        // Lấy Body Collider từ Motor (theo cấu trúc file PlayerMotor bạn cung cấp)
        // Lưu ý: Đảm bảo _bodyCollider trong PlayerMotor là public hoặc có Getter
        _bodyCollider = _motor.BodyCollider;
        _allPlayerColliders = GetComponents<Collider2D>();
    }

    private void OnEnable() => _input.OnJump += HandleJump;
    private void OnDisable() => _input.OnJump -= HandleJump;

    public void EnableAbility() => _isAbilityEnabled = true;
    public void DisableAbility()
    {
        _isAbilityEnabled = false;
        if (_isClimbing) ExitClimbing();
    }

    private void Update()
    {
        if (!_isAbilityEnabled || _motor.IsSwimming || _motor.IsZiplining) 
        {
            if (_isClimbing) ExitClimbing();
            return;
        }

        float verticalInput = _input.MoveInput.y;

        // --- KIỂM TRA BẮT ĐẦU LEO (Quét chủ động bằng OverlapBox) ---
        if (!_isClimbing)
        {
            // Quét xem có collider nào thuộc layer Ladder đang đè lên thân Player không
            Collider2D hit = Physics2D.OverlapBox(_bodyCollider.bounds.center, _bodyCollider.size, 0, _ladderLayer);
            if (hit != null && hit.TryGetComponent(out ILadder ladder))
            {
                _hoveredLadder = ladder;
                if (Mathf.Abs(verticalInput) > 0.1f)
                {
                    StartClimbing(_hoveredLadder);
                }
            }
        }

        // --- LOGIC KHI ĐANG LEO ---
        if (_isClimbing)
        {
            float playerPivotY = transform.position.y;
            float ladderTopY = _activeLadder.GetTopY();

            // 1. Check Snap lên đỉnh thang: Kích hoạt ngay khi Pivot (transform.position) vượt qua đỉnh thang
            if (verticalInput > 0.1f && playerPivotY >= ladderTopY)
            {
                SnapToTop(ladderTopY);
                return;
            }

            // 2. Check thoát thang ở đáy: Nếu chạm đất và đang nhấn xuống
            if (verticalInput < -0.1f && _motor.IsGrounded)
            {
                ExitClimbing();
            }
        }
    }

    private void FixedUpdate()
    {
        if (_isClimbing)
        {
            // Di chuyển dọc, triệt tiêu vận tốc ngang hoàn toàn
            float vVelocity = _input.MoveInput.y * _climbSpeed;
            _rb.linearVelocity = new Vector2(0, vVelocity);
            
            // Khóa di chuyển ngang của Motor
            _motor.LockMovement(Time.fixedDeltaTime * 2);
        }
    }

    private void StartClimbing(ILadder ladder)
    {
        _isClimbing = true;
        _activeLadder = ladder;
        _motor.SetClimbing(true);

        _rb.gravityScale = 0f;
        _rb.linearVelocity = Vector2.zero;

        // Snap X vào giữa thang ngay lập tức
        _rb.position = new Vector2(ladder.GetCenterX(), _rb.position.y);

        // Bỏ qua va chạm với "sàn" ở đỉnh thang để có thể leo xuyên qua
        ToggleTopPlatformCollision(ladder, true);
    }

    private void SnapToTop(float topY)
    {
        // Tính toán khoảng cách từ Pivot (transform.position) đến điểm thấp nhất của chân (FeetCollider)
        // Nếu không có chân, dùng đáy của BodyCollider làm fallback
        float currentFootY = (_motor.FeetCollider != null) ? _motor.FeetCollider.bounds.min.y : _bodyCollider.bounds.min.y;
        
        // Offset này là khoảng cách từ chân lên đến Pivot (luôn dương nếu Pivot ở trên chân)
        float pivotOffset = transform.position.y - currentFootY;

        // Vị trí mới = Tọa độ Y đỉnh thang + offset bù lại cho Pivot + một khoảng đệm nhỏ (0.05f) để không kẹt vào sàn
        _rb.position = new Vector2(_rb.position.x, topY + pivotOffset + 0.05f);
        
        // QUAN TRỌNG: Ép hệ thống vật lý cập nhật ngay để GroundCheck thấy đất
        Physics2D.SyncTransforms();
        
        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, -0.2f); // Ép nhẹ xuống để đảm bảo IsGrounded = true ngay frame sau
        ExitClimbing();
    }

    private void HandleJump()
    {
        if (_isClimbing)
        {
            ExitClimbing();
            // Nhảy thoát khỏi thang
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0);
            _rb.AddForce(Vector2.up * _jumpOffForce, ForceMode2D.Impulse);
            
            // Reset cooldown để tránh double jump lỗi
            _controller?.ResetJumpCooldown();
        }
    }

    private void ExitClimbing()
    {
        if (!_isClimbing) return;

        if (_activeLadder != null)
            ToggleTopPlatformCollision(_activeLadder, false);

        _isClimbing = false;
        _activeLadder = null;
        _motor.SetClimbing(false);

        // CHỈ khôi phục trọng lực nếu không đang bơi (vì bơi cần gravity = 0)
        if (!_motor.IsSwimming)
            _rb.gravityScale = _motor.OriginalGravityScale;
    }

    private void ToggleTopPlatformCollision(ILadder ladder, bool ignore)
    {
        Collider2D topCol = ladder.GetTopPlatformCollider();
        if (topCol != null)
        {
            foreach (var pCol in _allPlayerColliders)
            {
                Physics2D.IgnoreCollision(pCol, topCol, ignore);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Đã chuyển sang dùng OverlapBox trong Update để ổn định hơn
        if (other.TryGetComponent(out ILadder ladder)) _motor.SetTouchingLadder(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.TryGetComponent(out ILadder ladder))
        {
            _motor.SetTouchingLadder(false);
            if (_isClimbing && _activeLadder == ladder) ExitClimbing();
        }
    }
}
