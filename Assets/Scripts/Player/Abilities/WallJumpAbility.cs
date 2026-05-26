using UnityEngine;
using Core.Interfaces;
using Unity.Netcode;

/// <summary>
/// Khả năng nhảy tường cho Player. Cho phép bám vào tường khi đang ở trên không và nhảy bật ra khỏi tường.
/// Tường luôn tĩnh cố định, không di chuyển. Cơ chế bám dùng offset method mà không re-parent.
/// </summary>
public class WallJumpAbility : NetworkBehaviour, IPlayerAbility
{
    [Header("Settings")]
    [SerializeField] private LayerMask _wallLayer;
    [Tooltip("Khoảng cách Raycast kiểm tra tường.")]
    [SerializeField] private float _wallCheckDistance = 0.8f;
    [Tooltip("Offset theo chiều dọc của điểm xuất phát Raycast kiểm tra tường. Giá trị dương sẽ kiểm tra cao hơn.")]
    [SerializeField] private float _wallCheckVerticalOffset = 0.5f;
    [SerializeField] private Vector2 _jumpOffForce = new Vector2(10f, 12f); // Lực đẩy ngang và cao
    [SerializeField] private float _wallJumpLockTime = 0.2f; // Thời gian mất kiểm soát sau khi nhảy tường
    [Tooltip("Khoảng cách từ tâm Player đến mặt tường khi bám. Chỉnh nhỏ lại để bám sát hơn.")]
    [SerializeField] private float _clingOffset = 0.35f;

    [Header("Audio")]
    [SerializeField] private AudioClip _wallClingSound;
    [SerializeField] private AudioClip _wallJumpSound;

    private PlayerMotor _motor;
    private PlayerInputHandler _input;
    private Rigidbody2D _rb;
    
    private bool _isClinging = false;
    private bool _isAbilityEnabled = true;
    private Transform _currentWall; // Lưu trữ tường đang bám
    private Transform _lastWall;    // Lưu trữ tường vừa nhảy khỏi
    private Vector2 _clingWallNormal; // Vector pháp tuyến của tường hiện tại (dùng để snap vị trí)
    private float _clingTimer;
    private bool _currentWallHasTimer = false; // Biến kiểm tra xem tường hiện tại có timer không
    private float _reClingCooldown = 0.1f; // Thời gian không cho phép bám tường lại sau khi nhảy

    private void Awake()
    {
        _motor = GetComponent<PlayerMotor>();
        _input = GetComponent<PlayerInputHandler>();
        _rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable() 
    {
        if (_input != null) _input.OnJump += HandleJump;
        if (_motor != null) _motor.OnTeleported += HandleTeleport;
        if (_motor != null) _motor.OnJumpTriggered += ExitCling;
    }

    private void OnDisable() 
    {
        if (_input != null) _input.OnJump -= HandleJump;
        if (_motor != null) _motor.OnTeleported -= HandleTeleport;
        if (_motor != null) _motor.OnJumpTriggered -= ExitCling;
    }

    private void HandleTeleport() => ExitCling();

    public void EnableAbility() => _isAbilityEnabled = true;
    public void DisableAbility() {
        _isAbilityEnabled = false;
        if (_isClinging) ExitCling();
    }

    private void Update()
    {
        // Chỉ Owner (hoặc Singleplayer) mới xử lý logic bám tường
        if (IsSpawned && !IsOwner) return;
        
        // Guard: Chờ cho đến khi các component được initialize
        if (_motor == null || _input == null || _rb == null) return;

        // Nếu đang trượt Zipline, không cho phép bám tường
        if (_motor.IsZiplining)
        {
            if (_isClinging) ExitCling();
            return;
        }

        // Luôn giảm thời gian cooldown/grace period
        if (_reClingCooldown > 0) _reClingCooldown -= Time.deltaTime;

        // Nếu đang leo thang, bơi, hoặc ở dưới đất, không cho phép bám tường
        // FIX: Không cho phép bám tường khi đang Slide để tránh kẹt trong khe hẹp
        // LƯU Ý: _motor.IsGrounded bây giờ sẽ luôn false khi đang Cling, trừ khi ta chủ động nhả ra.
        if (!_isAbilityEnabled || _motor.IsClimbing || _motor.IsSwimming || _motor.IsSliding || _motor.IsSubmerged)
        {
            if (_isClinging) ExitCling();
            return;
        }

        if (_isClinging)
        {
            // Kiểm tra an toàn: Nếu tường đang bám bị hủy (destroy), tự động nhả ra
            if (_currentWall == null)
            {
                ExitCling();
                return;
            }

            // MAINTAIN POSITION: Giữ player sát tường bằng cách snap X position mỗi frame
            // Vì tường tĩnh, ta chỉ cần snap X, Y có thể di chuyển tự do dọc tường
            float targetX = _currentWall.position.x + (_clingWallNormal.x * _clingOffset);
            Vector3 currentPos = transform.position;
            transform.position = new Vector3(targetX, currentPos.y, currentPos.z);

            // Xử lý timer nếu được bật
            if (_currentWallHasTimer)
            {
                _clingTimer -= Time.deltaTime;
                if (_clingTimer <= 0f)
                {
                    JumpOff();
                    return; // Thoát khỏi Update để tránh logic khác chạy trong cùng frame
                }
            }
        }
        else // Nếu không đang bám tường
        {
            CheckForWall();
        }
    }

    private void CheckForWall()
    {
        // FIX: Chỉ cho phép tìm tường để bám nếu nhân vật đang ở trên không.
        // Điều này ngăn việc bám tường ngay khi đang đứng trên mặt đất.
        if (_motor.IsGrounded) return;

        // Raycast về phía trước mặt nhân vật để tìm tường
        Vector2 raycastOrigin = (Vector2)transform.position + Vector2.up * _wallCheckVerticalOffset;
        Vector2 direction = _motor.IsFacingRight ? Vector2.right : Vector2.left; // Hướng Raycast

        // Tăng nhẹ khoảng cách Raycast nếu nhân vật đang đứng sát tường. 
        // Đảm bảo _wallCheckDistance > bán kính của CircleCollider (ví dụ 0.6f hoặc 0.7f)
        RaycastHit2D hit = Physics2D.Raycast(raycastOrigin, direction, _wallCheckDistance, _wallLayer);

        if (hit.collider != null && hit.collider.TryGetComponent(out IWallSurface surface))
        {
            // COOLDOWN LOGIC: Chỉ chặn nếu là chính bức tường cũ và vẫn còn trong thời gian cooldown.
            // Nếu chạm vào tường mới (tường song song), bám được ngay lập tức.
            if (hit.transform == _lastWall && _reClingCooldown > 0) return;

            // FIX: Chỉ cho phép bám vào tường nếu bề mặt đó gần như thẳng đứng.
            // Kiểm tra vector pháp tuyến (Normal). Nếu Y > 0.1f tức là bề mặt bị nghiêng quá nhiều (dốc).
            // Điều này chặn việc bám vào tường nghiêng từ đầu, nhưng nếu đã bám rồi thì vẫn giữ được.
            if (Mathf.Abs(hit.normal.y) > 0.15f) return;

            EnterCling(hit, surface);
        }
    }

    private void EnterCling(RaycastHit2D hit, IWallSurface wallSurface)
    {
        Transform wallTransform = hit.transform;
        _isClinging = true;
        _motor.SetClinging(true); // Báo cho Motor (và Animator) biết

        // 2. Ngừng vật lý (Đứng im trên không)
        _rb.linearVelocity = Vector2.zero;
        _rb.gravityScale = 0f;
        // Chuyển sang Kinematic khi bám tường để Player không bị rơi xuống
        _rb.bodyType = RigidbodyType2D.Kinematic;

        // SNAP VỊ TRÍ: Đưa Player về sát mặt tường dựa trên điểm va chạm và offset
        // hit.normal.x sẽ là 1 nếu tường ở bên trái, và -1 nếu tường ở bên phải.
        // Chúng ta cộng thêm một khoảng offset theo hướng của Normal để tránh Player bị lún vào trong tường.
        float targetX = hit.point.x + (hit.normal.x * _clingOffset);
        transform.position = new Vector3(targetX, transform.position.y, transform.position.z);

        // Đặt thời gian ân hạn ngắn để tránh việc bị đẩy ra ngay lập tức nếu bám gần sàn
        _reClingCooldown = 0.15f;

        // Kiểm tra xem tường này có setup timer không
        if (wallSurface is IWallJumpSurface timedWall && timedWall.UseJumpTimer)
        {
            _currentWallHasTimer = true;
            _clingTimer = timedWall.ClingDuration;
        }
        else
        {
            _currentWallHasTimer = false;
        }

        // Phát âm thanh khi bắt đầu bám tường
        _motor.PlaySound(_wallClingSound);

        // 3. Quay mặt ra ngoài (ngược lại với phía có tường)
        // Nếu tường bên trái (normal.x > 0), player phải nhìn phải.
        // Nếu tường bên phải (normal.x < 0), player phải nhìn trái.
        bool shouldFaceRight = hit.normal.x > 0;
        if (_motor.IsFacingRight != shouldFaceRight) _motor.Flip();

        // 4. Lưu trạng thái tường
        _currentWall = wallTransform;
        _clingWallNormal = hit.normal;
    }

    private void HandleJump()
    {
        if (_isClinging)
        {
            JumpOff();
        }
    }

    private void JumpOff()
    {
        if (!_isClinging) return; // Guard clause để tránh nhảy nhiều lần

        // FIX: Nếu nước đã dâng lên đến người (Submerged) hoặc đã bắt đầu bơi,
        // ta chỉ nhả tường ra để bắt đầu trạng thái bơi luôn thay vì thực hiện cú nhảy bật tường.
        if (_motor.IsSubmerged || _motor.IsSwimming)
        {
            ExitCling();
            return;
        }

        // Ghi nhớ bức tường này để áp dụng cooldown riêng cho nó
        _lastWall = _currentWall;

        ExitCling();

        // Nhảy bật ra theo hướng mặt hiện tại (đã được flip ra ngoài)
        float horizontalDir = _motor.IsFacingRight ? 1f : -1f;
        Vector2 localJumpForce = new Vector2(horizontalDir * _jumpOffForce.x, _jumpOffForce.y);
        
        // FIX: Xoay vector lực nhảy theo góc xoay hiện tại của Player (do Player đang xoay theo tường).
        // Điều này đảm bảo khi tường xoay ngang, Player sẽ nhảy "lên" so với tường (tức là ra xa tường theo hướng mới).
        Vector2 rotatedForce = Quaternion.Euler(0, 0, _rb.rotation) * localJumpForce;
        
        _rb.AddForce(rotatedForce, ForceMode2D.Impulse);

        // FIX: Thông báo cho Motor rằng một cú nhảy đã xảy ra để Animator 
        // chuyển sang trạng thái Jump ngay lập tức thay vì Idle.
        _motor.NotifyJumpTriggered();

        // Phát âm thanh nhảy tường
        _motor.PlaySound(_wallJumpSound);

        // Khóa điều khiển Motor một chút để lực nhảy không bị SmoothDamp triệt tiêu
        _motor.LockMovement(_wallJumpLockTime);

        // Đặt cooldown để ngăn bám tường lại ngay lập tức. Thêm một chút thời gian đệm.
        _reClingCooldown = _wallJumpLockTime + 0.05f; 
    }

    private void ExitCling()
    {
        if (!_isClinging) return;

        _isClinging = false;
        _motor.SetClinging(false); // Tắt trạng thái bám tường
        
        // Trả lại trạng thái Dynamic để vật lý hoạt động lại (trọng lực, lực nhảy)
        _rb.bodyType = RigidbodyType2D.Dynamic;
        
        // Chỉ khôi phục trọng lực nếu KHÔNG phải đang bơi.
        // Nếu đang bơi, PlayerMotor đã set gravity = 0, ta không được ghi đè lại.
        if (!_motor.IsSwimming)
            _rb.gravityScale = _motor.OriginalGravityScale;

        _currentWall = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (_motor == null) return;
        Gizmos.color = Color.yellow;
        Vector3 raycastOrigin = transform.position + Vector3.up * _wallCheckVerticalOffset;
        Vector3 dir = _motor.IsFacingRight ? Vector3.right : Vector3.left; // Hướng Raycast
        Gizmos.DrawRay(raycastOrigin, dir * _wallCheckDistance);
    }
}