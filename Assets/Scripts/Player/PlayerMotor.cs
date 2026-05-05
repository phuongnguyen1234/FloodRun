using UnityEngine;
using UnityEngine.Events;
using Core.Interfaces;
using Core;
using UnityEngine.U2D.Animation;

/// <summary>
/// PlayerMotor chịu trách nhiệm quản lý tất cả logic liên quan đến di chuyển
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMotor : MonoBehaviour, IPlayerAbility, IPlayerMotorAttributes
{
    [SerializeField] private float _speed = 8f;
    [SerializeField] private float _exitFloodForce = 12f;                       // Lực đẩy khi thoát khỏi nước
    [Tooltip("Collider chính cho thân người chơi. Nên dùng BoxCollider2D.")]
    [SerializeField] private BoxCollider2D _bodyCollider;
    public BoxCollider2D BodyCollider => _bodyCollider;
    [Tooltip("Collider phụ ở chân để di chuyển mượt hơn. Nên dùng CircleCollider2D.")]
    [SerializeField] private CircleCollider2D _feetCollider;
    public CircleCollider2D FeetCollider => _feetCollider;
    [Tooltip("Collider dùng để xác định khi nào bắt đầu bơi (thường đặt ở ngực/đầu). Nếu để trống sẽ dùng Body Collider.")]
    [SerializeField] private Collider2D _swimTriggerCollider;
    [SerializeField] private float _jumpForce = 11.5f;
    [Header("Visuals Root")]
    [Tooltip("Transform chứa toàn bộ Sprite/Xương của nhân vật. Chúng ta sẽ xoay cái này thay vì xoay Rigidbody khi bơi.")]
    [SerializeField] private Transform _visualsRoot;
    [Tooltip("Lực đẩy riêng khi thực hiện nhảy vô hạn trên không")]
    [SerializeField] private float _infJumpForce = 12f;

    [Tooltip("Chiều rộng của Collider khi đang Slide. Nên nhỏ hơn hoặc bằng 1 để chui vừa khe 1 grid.")]
    [SerializeField] private float _slideWidth = 0.8f;
    
    [Header("Movement Smoothing")]
    [Range(0, .3f)] [SerializeField] private float _groundSmoothing = .05f;      // Độ mượt khi di chuyển (nhạy)
    [Range(0, .5f)] [SerializeField] private float _swimSmoothing = .15f;        // Độ mượt khi bơi (có quán tính nước)
    [SerializeField] private float _swimRotationSpeed = 25f;                    // Tốc độ xoay người theo hướng bơi (càng cao xoay càng nhanh)
    
    [SerializeField] private bool _airControl = true;							// Whether or not a player can steer while jumping;

    [Header("Audio")]
    private AudioSource _sfxAudioSource;

    [Header("Slope Handling")]
    [SerializeField] private PhysicsMaterial2D _grippyMaterial;                 // Material with high friction
    [SerializeField] private PhysicsMaterial2D _slipperyMaterial;               // Material with zero friction
    
    [Header("Collision Checks")]
    [SerializeField] private Transform _groundCheck;
    [Tooltip("Kích thước của hộp kiểm tra mặt đất (Rộng, Cao).")]
    [SerializeField] private Vector2 _groundCheckSize = new Vector2(0.5f, 0.4f);
    [SerializeField] private LayerMask _groundLayer;
    public LayerMask GroundLayer => _groundLayer;
    
    [SerializeField] private Transform _ceilingCheck;
    [SerializeField] private Vector2 _ceilingCheckSize = new Vector2(0.5f, 0.2f);

    [Header("Bone Animation Settings")]
    [Tooltip("Tên Label trong Sprite Library tương ứng với hướng bên phải")]
    [SerializeField] private string _rightLabel = "Side_Right";
    [Tooltip("Tên Label trong Sprite Library tương ứng với hướng bên trái")]
    [SerializeField] private string _leftLabel = "Side_Left";
    [Tooltip("Tên Label trong Sprite Library cho trạng thái đứng yên")]
    [SerializeField] private string _idleLabel = "Idle_3Q";
    [Tooltip("Tên Label trong Sprite Library cho trạng thái leo thang (nhìn từ sau lưng)")]
    [SerializeField] private string _climbLabel = "Back";

    [Tooltip("Nếu Sprite trong Library đã vẽ sẵn hướng Trái/Phải, hãy tắt cái này để tránh lật 2 lần")]
    [SerializeField] private bool _useScaleFlip = false;

    private SpriteResolver[] _spriteResolvers;
    private string _currentLabel;

    private Rigidbody2D _rb;
    private bool _facingRight = true;
    public bool IsFacingRight => _facingRight;

    private Vector3 _velocity = Vector3.zero;
    public float OriginalGravityScale => _originalGravityScale;
    private float _originalGravityScale;
    private bool _isInFloodZone = false; // Đang nằm trong vùng trigger của Flood nói chung
    private bool _isAttemptingToJumpOutOfWater = false;
    private bool _isSwimHorizontal = false; // Trạng thái bơi ngang
    private bool _isAbilityEnabled = true;
    private float _movementLockTimer = 0f; // Timer khóa di chuyển
    private Vector2 _groundNormal = Vector2.up; // Vector pháp tuyến của mặt đất hiện tại

    // FIX: Hỗ trợ nhiều vùng flood chồng nhau
    private readonly System.Collections.Generic.List<IFloodZone> _activeFloodZones = new System.Collections.Generic.List<IFloodZone>();
    private readonly System.Collections.Generic.Dictionary<IFloodZone, Collider2D> _floodColliders = new System.Collections.Generic.Dictionary<IFloodZone, Collider2D>();

    public IFloodZone CurrentFlood { get; private set; }

    public bool IsGrounded { get; private set; }
    public float JumpForce => _jumpForce;
    public float InfJumpForce => _infJumpForce;
    public bool IsSwimming { get; private set; }
    public bool IsSubmerged { get; private set; } // True khi player ngập trong nước/flood (dựa vào swim trigger)
    public bool IsClinging { get; private set; }
    public bool IsSliding { get; private set; }
    public bool IsDiving { get; private set; }
    public bool IsZiplining { get; private set; }
    public bool IsTouchingLadder { get; private set; } // Trạng thái đang chạm vào vùng thang (Trigger)
    public bool IsClimbing { get; private set; } // Trạng thái leo thang
    public bool IsDead { get; set; } // Trạng thái chết (được set từ Controller)

    // Variables để restore trạng thái collider
    private Vector2 _originalColliderSize;
    private Vector2 _originalColliderOffset;
    private Vector3 _originalCeilingCheckPos;
    private Vector2 _originalCeilingCheckSize; // Thêm biến để lưu kích thước gốc của ceiling check
    private Vector3 _originalGroundCheckPos;
    private Vector2 _originalGroundCheckSize;
    private Vector3 _originalSwimTriggerPos;
    private Vector2 _originalSwimTriggerSize; // Lưu size gốc của trigger (nếu là Box)

    [Header("Events")]
	[Space]
	public UnityEvent OnLandEvent;
    public UnityEvent OnExitWaterEvent;

    private Vector2 _inputMoveDirection;
    public Vector2 InputMoveDirection => _inputMoveDirection;

    /// <summary>
    /// Cập nhật nhãn (Label) cho toàn bộ các bộ phận cơ thể dựa trên trạng thái.
    /// </summary>
    public void UpdateSpriteLabels()
    {
        string targetLabel = _idleLabel;

        if (IsClimbing)
        {
            targetLabel = _climbLabel;
        }
        else if (Mathf.Abs(_rb.linearVelocity.x) > 0.1f || IsSwimming)
        {
            targetLabel = _facingRight ? _rightLabel : _leftLabel;
        }

        if (_currentLabel == targetLabel) return;
        _currentLabel = targetLabel;

        foreach (var resolver in _spriteResolvers)
        {
            // Lấy category hiện tại của từng bộ phận (ví dụ: "Arm", "Leg") 
            // và đổi label sang target (ví dụ: "Back")
            string category = resolver.GetCategory();
            resolver.SetCategoryAndLabel(category, targetLabel);
        }
    }

    #region IPlayerAbility Implementation
    public void EnableAbility() 
    {
        _isAbilityEnabled = true;
        
        // Đảm bảo Collider chân được trả về trạng thái đúng khi bật lại Ability
        if (!IsSwimming && !IsSliding && _feetCollider != null)
        {
            _feetCollider.enabled = true;
        }
    }
    public void DisableAbility() 
    {
        _isAbilityEnabled = false;
        // Dọn dẹp sạch trạng thái Flood để tránh lỗi logic khi Teleport hoặc Reset
        _activeFloodZones.Clear();
        _floodColliders.Clear();
        IsSwimming = false;
        IsSubmerged = false;
        CurrentFlood = null;
    }
    #endregion

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        
        // Lưu lại thông số ban đầu của Collider
        if (_bodyCollider != null)
        {
            _originalColliderSize = _bodyCollider.size;
            _originalColliderOffset = _bodyCollider.offset;
        }
        // Cố gắng lấy AudioSource, nếu không có thì thêm mới để phát SFX.
        // Đảm bảo Player có một AudioSource để phát hiệu ứng âm thanh.
        if (!TryGetComponent<AudioSource>(out _sfxAudioSource)) {
            _sfxAudioSource = gameObject.AddComponent<AudioSource>();
            _sfxAudioSource.playOnAwake = false;
        }

        // Lấy tất cả các SpriteResolver trên các bộ phận cơ thể (Arms, Legs, Torso, v.v.)
        _spriteResolvers = GetComponentsInChildren<SpriteResolver>();
        _originalGravityScale = _rb.gravityScale;

        if (_ceilingCheck != null) _originalCeilingCheckPos = _ceilingCheck.localPosition;
        if (_ceilingCheck != null)
        {
            _originalCeilingCheckSize = _ceilingCheckSize; // Lưu kích thước gốc
        }
        if (_groundCheck != null)
        {
            _originalGroundCheckPos = _groundCheck.localPosition;
            _originalGroundCheckSize = _groundCheckSize;
        }
        if (_swimTriggerCollider != null)
        {
            _originalSwimTriggerPos = _swimTriggerCollider.transform.localPosition;
            if (_swimTriggerCollider is BoxCollider2D box)
            {
                _originalSwimTriggerSize = box.size;
            }
        }

        if (OnLandEvent == null)
			OnLandEvent = new UnityEvent();
        if (OnExitWaterEvent == null)
            OnExitWaterEvent = new UnityEvent();
    }

    void FixedUpdate()
    {
        if (!_isAbilityEnabled) return;

        UpdateGroundNormal(); // Cập nhật pháp tuyến mặt đất mỗi frame vật lý

        bool wasGrounded = IsGrounded;
        IsGrounded = false;

        // Kiểm tra mặt đất mỗi khung hình
        if (_groundCheck != null)
        {
            // CẢI TIẾN QUAN TRỌNG: Bắt đầu quét từ một điểm cao hơn _groundCheck (0.1f) 
            // để đảm bảo nếu chân bị lún sâu vào sàn (do Snap), BoxCast vẫn phát hiện được mặt sàn.
            Vector2 castOrigin = (Vector2)_groundCheck.position + Vector2.up * 0.1f;
            
            // CẢI TIẾN: Tăng nhẹ baseDist từ 0.35f lên 0.4f để bù đắp sai số khi Snap từ thang lên sàn
            float baseDist = IsClimbing ? 0.45f : (IsSwimming ? 0.15f : 0.4f);
            float totalCheckDist = baseDist + 0.1f;

            RaycastHit2D hit = Physics2D.BoxCast(castOrigin, _groundCheckSize, _rb.rotation, Vector2.down, totalCheckDist, _groundLayer);

            // FIX: Ngoài việc chạm collider, phải kiểm tra xem bề mặt đó có đủ "phẳng" không.
            // hit.normal.y > 0.7f tương đương với góc nghiêng khoảng 45 độ. 
            // Nếu chạm vào tường đứng, normal.y sẽ gần bằng 0 và bị loại bỏ -> Sửa lỗi Infinity Jump.
            bool isGroundedThisFrame = hit.collider != null && !hit.collider.isTrigger && hit.normal.y > 0.7f;

            IsGrounded = isGroundedThisFrame;

            if (IsGrounded && !wasGrounded)
                OnLandEvent.Invoke();
        }

        // Xử lý xoay người theo dốc khi đang trượt (Slide)
        // Chỉ xoay khi đang ở dưới đất để tránh xoay loạn xạ trên không
        if (IsSliding && IsGrounded) HandleSlopeRotation();
        else if (IsSwimming && !IsZiplining) HandleSwimmingRotation();
        
        // RESET ROTATION & COLLIDER STATE
        // FIX: Khi đang Clinging, không được reset rotation hoặc bật lại chân vì WallJump đang quản lý
        if (!IsSliding && !IsZiplining && !IsClinging && !IsSwimming)
        {
            if (_rb.rotation != 0) _rb.rotation = 0f;
            Quaternion defaultRot = Quaternion.Euler(0, 0, 90f);
            if (_visualsRoot != null && _visualsRoot.localRotation != defaultRot)
                _visualsRoot.localRotation = defaultRot;

            // CẢI TIẾN: Luôn đảm bảo CircleCollider chân được bật lại khi không ở các trạng thái đặc biệt
            if (_feetCollider != null && !_feetCollider.enabled) 
                _feetCollider.enabled = true;
        }
        else if (IsSwimming) _rb.rotation = 0f;


        // Kiểm tra điều kiện bơi khi đang ở trong vùng nước (nhưng chưa kích hoạt trạng thái bơi)
        // Nếu đang ở trong vùng flood, liên tục kiểm tra trạng thái ngập nước và bơi.
        // Điều này cần thiết cho các trường hợp nước dâng lên hoặc người chơi di chuyển vào vùng nước sâu hơn.
        if (_isInFloodZone)
        {
            // 1. Cập nhật trạng thái "ngập" (submerged) dựa trên vị trí hiện tại
            UpdateSubmergedState();

            // 2. Xử lý chuyển đổi trạng thái Bơi (Swimming)
            // Bắt đầu bơi nếu: ngập nước + chưa bơi + KHÔNG đang đu dây + flood cho phép bơi
            if (IsSubmerged && !IsSwimming && !IsZiplining && !CurrentFlood.NoSwim)
            {
                StartSwimming();
            }
            // Dừng bơi nếu: đang bơi + không còn ngập nước (ví dụ: bơi vào vùng nước nông)
            else if (IsSwimming && !IsSubmerged)
            {
                ExitWater(); // FIX: Gọi ExitWater để kích hoạt lực đẩy (nếu đang giữ nút nhảy)
            }
        }
        else if (IsSubmerged) IsSubmerged = false; // Đảm bảo IsSubmerged là false nếu không ở trong flood nào
    }

    private void HandleSwimmingRotation()
    {
        // Sử dụng vận tốc thực tế của Rigidbody để xoay người
        // Khi bơi, chúng ta xoay VisualsRoot chứ không xoay Rigidbody để tránh lỗi va chạm (jitter)
        if (_visualsRoot == null) return;
        Vector2 vel = _rb.linearVelocity;

        // FIX: Nếu vận tốc thấp nhưng người chơi đang nhấn phím (đâm vào tường), 
        // vẫn tính toán góc xoay dựa trên phím nhấn để tránh bị trả về tư thế Float (90 độ).
        bool isInputting = _inputMoveDirection.sqrMagnitude > 0.01f;
        if (vel.sqrMagnitude > 0.4f || isInputting) 
        {
            Vector2 angleSource = vel.sqrMagnitude > 0.4f ? vel : _inputMoveDirection;
            // Sử dụng Mathf.Abs(x) để đảm bảo góc quay luôn dựa trên hướng nhìn của Sprite.
            // Loại bỏ việc clamp 1.2f để cho phép nhân vật đạt góc 90 độ hoàn hảo khi bơi dọc.
            float targetAngle = Mathf.Atan2(angleSource.y, Mathf.Abs(angleSource.x)) * Mathf.Rad2Deg;

            // Tăng giá trị nhân với Time.fixedDeltaTime sẽ làm giảm thời gian xoay (xoay gắt hơn)
            // Xoay Visuals
            _visualsRoot.localRotation = Quaternion.Lerp(
                _visualsRoot.localRotation, 
                Quaternion.Euler(0, 0, targetAngle), 
                Time.fixedDeltaTime * _swimRotationSpeed
            );        }
        else
        {
            _visualsRoot.localRotation = Quaternion.RotateTowards(
                _visualsRoot.localRotation, 
                Quaternion.Euler(0, 0, 90f),
                Time.fixedDeltaTime * 800f
            );        
        }
    }

    // Bắn tia Raycast xuống dưới để lấy vector pháp tuyến (độ nghiêng) của sàn
    private void UpdateGroundNormal()
    {
        // Chỉ check khi ground check tồn tại
        if (_groundCheck == null) return;
        
        // Sử dụng một khoảng cách cố định (0.5f) thay vì dựa vào bán kính cũ
        RaycastHit2D hit = Physics2D.Raycast(_groundCheck.position, Vector2.down, 0.5f, _groundLayer);
        if (hit.collider != null)
        {
            _groundNormal = hit.normal;
        }
        else
        {
            _groundNormal = Vector2.up;
        }
    }

    // Xoay Rigidbody để khớp với độ nghiêng của dốc
    private void HandleSlopeRotation()
    {
        // Tính góc xoay từ vector pháp tuyến (Normal) so với vector lên (Up)
        float angle = Vector2.SignedAngle(Vector2.up, _groundNormal);
        
        // Smooth rotation để tránh giật cục khi chuyển tiếp giữa các đoạn dốc
        // Lerp từ góc hiện tại sang góc mới (Lưu ý: Unity rotation ngược chiều kim đồng hồ là dương, nhưng check slope thì tùy hướng)
        // SignedAngle trả về: nghiêng phải (normal hướng phải) -> góc âm.
        // Rotation Z của 2D: nghiêng phải (ngược chiều KĐH) -> góc dương? Cần test.
        // Thực tế: Rotate Z > 0 là nghiêng trái. Rotate Z < 0 là nghiêng phải.
        // SignedAngle(Up, Normal): Normal nghiêng phải -> Angle < 0.
        // Muốn Box nghiêng phải theo dốc -> Rotate Z cũng phải < 0.
        // => Dùng trực tiếp angle.
        
        float targetRotation = angle;
        if (Mathf.Abs(targetRotation) > 1f) // Chỉ xoay nếu dốc đủ lớn
        {
             _rb.SetRotation(Mathf.LerpAngle(_rb.rotation, targetRotation, Time.fixedDeltaTime * 10f));
        }
        else
        {
             _rb.SetRotation(Mathf.LerpAngle(_rb.rotation, 0f, Time.fixedDeltaTime * 10f));
        }
    }

    // Hàm Helper để SlideAbility lấy vận tốc hướng theo dốc
    public Vector2 GetSlopeVelocity(Vector2 horizontalDirection, float speed)
    {
        // Tính vector tiếp tuyến (vuông góc với normal)
        // Vector3.Cross(normal, forward) sẽ cho vector hướng sang phải dọc theo bề mặt 2D
        Vector2 slopeDir = Vector3.Cross(_groundNormal, Vector3.forward); // Mặc định hướng phải
        
        // Nếu input muốn đi sang trái, đảo ngược vector slope
        if (horizontalDirection.x < 0) slopeDir = -slopeDir;

        // Đảm bảo slopeDir luôn hướng theo đúng chiều mong muốn dựa trên horizontalDirection đầu vào
        // (Logic Cross product có thể bị ngược tùy vào hệ tọa độ, kiểm tra dot product để chắc chắn)
        if (Vector2.Dot(slopeDir, horizontalDirection) < 0) slopeDir = -slopeDir;

        return slopeDir * speed;
    }

    public void Move(Vector2 input)
    {
        if (!_isAbilityEnabled) return;
        
        _inputMoveDirection = input;
        
        // CẢI TIẾN: Nếu đang ở các trạng thái tự động điều khiển vận tốc, 
        // thoát sớm để tránh xung đột gây khựng (stutter).
        // FIX: Thêm IsClinging để Motor không tự di chuyển khi đang bám tường
        if (IsZiplining || IsSliding || IsDiving || IsClimbing || IsClinging)
        {
            return;
        }

        // XỬ LÝ FLIP: Chỉ tự động quay mặt theo phím nhấn khi không ở các trạng thái đặc biệt bên trên
        // (Trạng thái đặc biệt sẽ tự quản lý hướng nhìn của riêng chúng)
        if (input.x > 0 && !_facingRight) Flip();
        else if (input.x < 0 && _facingRight) Flip();
        
        // Nếu đang bị khóa di chuyển (do WallJump, Slide), giảm timer và bỏ qua logic di chuyển
        if (_movementLockTimer > 0)
        {
            _movementLockTimer -= Time.deltaTime;
            return;
        }

        if (IsSwimming)
        {
            // Logic bơi: Di chuyển tự do WASD, không chịu ảnh hưởng trọng lực
            
            // Đảm bảo khi bơi luôn dùng vật liệu trơn (không ma sát) để không bị dính vào tường
            if (_bodyCollider != null) 
                _bodyCollider.sharedMaterial = _slipperyMaterial;
            if (_feetCollider != null)
                _feetCollider.sharedMaterial = _slipperyMaterial;

            float targetSwimX = input.x * _speed;
            // FIX: Ngăn chặn triệt để việc Collider đâm xuyên/giật khi bơi sát tường.
            // Nếu đang nhấn phím di chuyển vào tường, ta chủ động set target velocity về 0
            // để SmoothDamp hãm nhân vật lại một cách êm ái, tránh việc Physics Engine đẩy ngược quá gắt.
            if (Mathf.Abs(input.x) > 0.01f && _bodyCollider != null)
            {
                Vector2 checkDir = Vector2.right * Mathf.Sign(input.x);
                // Quét một hộp theo kích thước hiện tại của body
                RaycastHit2D wallHit = Physics2D.BoxCast(_bodyCollider.bounds.center, 
                    new Vector2(_bodyCollider.size.x, _bodyCollider.size.y * 0.8f), 
                    _rb.rotation, checkDir, 0.15f, _groundLayer);

                // CẢI TIẾN: Chỉ chặn nếu va chạm với vật thể cứng, bỏ qua Trigger (như Thang)
                if (wallHit.collider != null && !wallHit.collider.isTrigger)
                {
                    targetSwimX = 0f;
                    _velocity.x = 0f; // Reset bộ đệm SmoothDamp để dừng ngay lập tức
                }
            }

            float newX = Mathf.SmoothDamp(_rb.linearVelocity.x, targetSwimX, ref _velocity.x, _swimSmoothing);
            float newY = Mathf.SmoothDamp(_rb.linearVelocity.y, input.y * _speed, ref _velocity.y, _swimSmoothing);
            
            _rb.linearVelocity = new Vector2(newX, newY);
            
            // Fix lỗi 1.4e: Nếu không bấm nút và vận tốc cực nhỏ, gán thẳng về 0
            if (input == Vector2.zero && _rb.linearVelocity.magnitude < 0.01f)
            {
                _rb.linearVelocity = Vector2.zero;
                _velocity = Vector3.zero; // Reset biến tham chiếu vận tốc của SmoothDamp
            }

            // TỰ ĐỘNG RESIZE KHI BƠI NGANG (Giống FE2)
            if (Mathf.Abs(input.x) > 0.1f)
            {
                if (!_isSwimHorizontal)
                {
                    ResizeCollider(0f, false); // Bơi: Thực hiện phép xoay 90 độ quanh tâm
                    _isSwimHorizontal = true;
                }
            }
            else
            {
                // Khi đứng yên trong nước (đứng bơi), trả lại collider đứng thẳng
                // Cần kiểm tra cả trần và sàn vì bơi ngang xoay quanh tâm (nở ra 2 đầu)
                if (_isSwimHorizontal && !CheckForCeiling() && !CheckForGroundExpansion())
                {
                    ResetCollider();
                    _isSwimHorizontal = false;
                }
            }

            return; // Kết thúc hàm Move, bỏ qua logic đi bộ
        }

        float moveInput = input.x; // Khi đi bộ chỉ lấy trục X

        // Chỉ cho phép điều khiển khi đang ở trên mặt đất hoặc khi bật _airControl
        if (IsGrounded || _airControl)
        {
            PhysicsMaterial2D materialToApply = _slipperyMaterial;
            // Xử lý trượt dốc: Nếu đứng yên trên mặt đất, dùng vật liệu ma sát cao.
            // Nếu di chuyển hoặc ở trên không, dùng vật liệu trơn.
            if (IsGrounded && moveInput == 0)
                materialToApply = _grippyMaterial;

            if (_bodyCollider != null) _bodyCollider.sharedMaterial = materialToApply;
            if (_feetCollider != null) _feetCollider.sharedMaterial = materialToApply;

            // Di chuyển nhân vật bằng cách tìm vận tốc mục tiêu
            float targetVelocityX = moveInput * _speed;

            // FIX: Chống giật animation khi chạy sát tường
            // CẢI TIẾN: Loại bỏ !IsTouchingLadder. Wall Check phải luôn chạy để triệt tiêu vận tốc 
            // khi đâm vào tường thật nằm đằng sau thang, giúp Animator chuyển về trạng thái Idle/Slide đúng lúc.
            // LƯU Ý: Nếu Player không di chuyển được khi chạm thang, hãy kiểm tra xem Thang có đang ở GroundLayer không.
            // Thang nên là Trigger và thuộc layer khác (ví dụ: Interactable).
            if (Mathf.Abs(moveInput) > 0.01f && _bodyCollider != null)
            {
                Vector2 checkDir = Vector2.right * Mathf.Sign(moveInput);
                // Quét một hộp nhỏ về phía trước để xem có tường không
                // Co size Y lại một chút để tránh quét trúng mặt đất
                RaycastHit2D wallHit = Physics2D.BoxCast(_bodyCollider.bounds.center, 
                    new Vector2(_bodyCollider.size.x, _bodyCollider.size.y * 0.8f), 
                    0f, checkDir, 0.1f, _groundLayer);

                // CẢI TIẾN: Chỉ chặn nếu va chạm với vật thể cứng, bỏ qua Trigger (như Thang)
                if (wallHit.collider != null && !wallHit.collider.isTrigger)
                {
                    // Nếu phát hiện tường, triệt tiêu vận tốc ngang
                    targetVelocityX = 0f;
                    // Triệt tiêu vận tốc hiện tại ngay lập tức để Animator không bị nhầm
                    _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                    _velocity.x = 0f; // Reset bộ đệm của SmoothDamp
                }
            }
            
            // 1. Chỉ làm mượt trục X, giữ nguyên trục Y để lực nhảy hoạt động đúng
            float smoothedX = Mathf.SmoothDamp(_rb.linearVelocity.x, targetVelocityX, ref _velocity.x, _groundSmoothing);

            // 2. Fix lỗi 1.4e: Gán về 0 nếu gần như đứng yên
            if (Mathf.Abs(moveInput) < 0.01f && Mathf.Abs(smoothedX) < 0.01f)
            {
                smoothedX = 0f;
                _velocity.x = 0f;
            }

            // FIX: Triệt tiêu hiện tượng giật Y (Jitter) khi chạy.
            // Nếu đang Grounded và không có vận tốc nhảy lên đáng kể (> 0.5f), 
            // ta ép vận tốc Y về 0 để tránh CircleCollider nảy trên các khe Tilemap.
            float verticalVel = _rb.linearVelocity.y;
            if (IsGrounded && verticalVel > 0.001f && verticalVel < 0.5f)
            {
                verticalVel = 0f;
            }

            _rb.linearVelocity = new Vector2(smoothedX, verticalVel);
        }
    }

    /// <summary>
    /// Khóa quyền điều khiển di chuyển trong một khoảng thời gian (dùng cho WallJump, Dash, v.v.)
    /// </summary>
    public void LockMovement(float duration)
    {
        _movementLockTimer = duration;
    }

    #region Public Setters for Attributes
    public void SetSpeed(float newSpeed)
    {
        _speed = Mathf.Max(0, newSpeed);
    }

    public void SetJumpForce(float newJumpForce)
    {
        _jumpForce = Mathf.Max(0, newJumpForce);
    }

    public void SetGravityScale(float newGravityScale)
    {
        if (_rb != null) _rb.gravityScale = newGravityScale;
    }

    public void ResetGravityScale() => SetGravityScale(_originalGravityScale);
    #endregion

    public void SetAttemptingToJumpOutOfWater(bool isAttempting)
    {
        _isAttemptingToJumpOutOfWater = isAttempting;
    }

    public void Jump(float force = -1f)
    {
        if (!_isAbilityEnabled) return;

        // Nếu không truyền force vào, sẽ dùng mặc định là _jumpForce
        float forceToApply = force > 0 ? force : _jumpForce;

        if (!IsSwimming && !IsClimbing)
        {
            // RESET VELOCITY Y trước khi nhảy để tránh cộng dồn lực (ví dụ từ lực pop của nước hoặc lực rơi)
            // Điều này đảm bảo cú nhảy luôn có độ cao ổn định.
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
            _rb.AddForce(Vector2.up * forceToApply, ForceMode2D.Impulse);
        }
    }

    /// <summary>
    /// Bắt đầu trạng thái trượt: Giảm collider, chỉnh ceiling check
    /// </summary>
    public void StartSliding(float heightRatio)
    {
        IsSliding = true;
        ResizeCollider(heightRatio, true); // Trượt: Vẫn dùng tỉ lệ vì cần giữ đáy cố định
    }

    public void StopSliding()
    {
        IsSliding = false;
        // Chỉ khôi phục nếu không đang bơi ngang (để tránh xung đột logic)
        if (!_isSwimHorizontal)
        {
            ResetCollider();
        }
    }

    // Tách logic thay đổi kích thước Collider ra hàm riêng để dùng chung cho Swim
    private void ResizeCollider(float heightRatio, bool keepBottomFixed)
    {
        if (_bodyCollider != null)
        {
            // Tắt collider chân khi trượt/bơi để bề mặt tiếp xúc phẳng hoàn toàn và tránh kẹt
            if (_feetCollider != null) _feetCollider.enabled = false;

            float newWidth, newHeight, newOffsetY;

            if (keepBottomFixed)
            {
                // LOGIC TRƯỢT: Giảm chiều cao theo tỉ lệ, phình ngang và giữ đáy sát đất
                newHeight = _originalColliderSize.y * heightRatio;
                // FIX: Không lấy chiều cao cũ làm chiều rộng, sử dụng thông số cố định để tránh kẹt khe 1 grid
                newWidth = _slideWidth; 

                float oldBottomY = _originalColliderOffset.y - (_originalColliderSize.y / 2f);
                if (_feetCollider != null)
                {
                    float feetBottom = _feetCollider.offset.y - _feetCollider.radius;
                    if (feetBottom < oldBottomY) oldBottomY = feetBottom;
                }
                newOffsetY = oldBottomY + (newHeight / 2f);
            }
            else
            {
                // LOGIC BƠI (FE2): Xoay 90 độ chuẩn quanh Pivot (Tâm)
                // Chiều rộng mới = Chiều cao cũ
                // Chiều cao mới = Chiều rộng cũ
                newWidth = _originalColliderSize.y;
                newHeight = _originalColliderSize.x;
                
                // Giữ nguyên Offset gốc vì xoay quanh tâm
                newOffsetY = _originalColliderOffset.y;
            }

            _bodyCollider.size = new Vector2(newWidth, newHeight);
            _bodyCollider.offset = new Vector2(_originalColliderOffset.x, newOffsetY);

            // Tính toán sự thay đổi vị trí theo chiều dọc của collider chính
            float deltaY = _bodyCollider.offset.y - _originalColliderOffset.y;

            // Cập nhật vị trí của điểm check trần nhà tương ứng
            if (_ceilingCheck != null)
            {
                float newTopY = newOffsetY + (newHeight / 2f);
                float gap = 0f;
                
                // Khi bơi ngang (keepBottomFixed = false)
                if (keepBottomFixed)
                {
                    // Khi trượt: Khôi phục khoảng cách (gap) gốc để check việc đứng dậy an toàn
                    float originalTopY = _originalColliderOffset.y + (_originalColliderSize.y / 2f);
                    gap = _originalCeilingCheckPos.y - originalTopY;
                    _ceilingCheckSize = _originalCeilingCheckSize;
                }
                else
                {
                    // Khi bơi ngang: Đặt tâm sát biên trên và resize theo chiều ngang mới
                    _ceilingCheckSize = new Vector2(newWidth, _originalCeilingCheckSize.y);
                }

                _ceilingCheck.localPosition = new Vector3(_originalCeilingCheckPos.x, newTopY + gap, _originalCeilingCheckPos.z);
            }

            if (_groundCheck != null)
            {
                if (keepBottomFixed)
                {
                    // 1. Khi slide: Giữ nguyên vị trí Y và kích thước Ground Check như ban đầu
                    _groundCheck.localPosition = _originalGroundCheckPos;
                    _groundCheckSize = _originalGroundCheckSize;
                }
                else
                {
                    // 2. Khi bơi ngang: Đặt tâm sát biên dưới collider và resize rộng bằng chiều dài cơ thể
                    float newBottomY = newOffsetY - (newHeight / 2f);
                    _groundCheck.localPosition = new Vector3(_originalGroundCheckPos.x, newBottomY, _originalGroundCheckPos.z);
                    _groundCheckSize = new Vector2(newWidth, _originalGroundCheckSize.y);
                }
            }

            // Cập nhật vị trí của swim trigger để nó di chuyển cùng với thân người
            if (_swimTriggerCollider != null)
            {
                // Chỉ di chuyển theo DeltaY của thân, không cần Offset thủ công
                _swimTriggerCollider.transform.localPosition = new Vector3(
                    _originalSwimTriggerPos.x,
                    _originalSwimTriggerPos.y + deltaY,
                    _originalSwimTriggerPos.z);

                // Đồng bộ xoay cho Swim Trigger nếu nó là Box
                if (_swimTriggerCollider is BoxCollider2D box)
                {
                    box.size = !keepBottomFixed 
                        ? new Vector2(_originalSwimTriggerSize.y, _originalSwimTriggerSize.x) // Xoay 90 độ
                        : new Vector2(_originalSwimTriggerSize.x, _originalSwimTriggerSize.y * heightRatio); // Thu tỉ lệ
                }
            }
        }
    }

    // Hàm khôi phục Collider về trạng thái gốc
    private void ResetCollider()
    {
        if (_bodyCollider == null) return;
        _bodyCollider.size = _originalColliderSize;
        _bodyCollider.offset = _originalColliderOffset;
        
        // Bật lại collider chân
        // Chỉ khôi phục collider chân nếu không phải đang bơi để tránh kẹt vào địa hình
        if (_feetCollider != null && !IsSwimming) _feetCollider.enabled = true;
        
        // Đảm bảo xoay người về thẳng đứng ngay lập tức khi dừng trượt
        _rb.rotation = 0f;
        
        if (_ceilingCheck != null)
        {
            _ceilingCheck.localPosition = _originalCeilingCheckPos;
            _ceilingCheckSize = _originalCeilingCheckSize; // Khôi phục kích thước gốc của ceiling check
        }

        // Khôi phục Ground Check
        if (_groundCheck != null)
        {
            _groundCheck.localPosition = _originalGroundCheckPos;
            _groundCheckSize = _originalGroundCheckSize;
        }

        // Khôi phục vị trí swim trigger
        if (_swimTriggerCollider != null)
        {
            _swimTriggerCollider.transform.localPosition = _originalSwimTriggerPos;
            // Khôi phục size cho trigger
            if (_swimTriggerCollider is BoxCollider2D box)
            {
                box.size = _originalSwimTriggerSize;
            }
        }
    }

    // Public hàm check ceiling để SlideAbility dùng
    public bool CheckForCeiling()
    {
        if (_ceilingCheck == null) return false;

        // FIX: Khi đang Slide hoặc Bơi ngang (Collider bị thu nhỏ), vị trí _ceilingCheck thực tế đã bị hạ thấp.
        // Để kiểm tra xem có thể ĐỨNG DẬY được không, ta cần quét (Sweep) từ vị trí thấp lên vị trí cao (Gốc).
        // Nếu chỉ check tại điểm đích, ta có thể bỏ qua các chướng ngại vật lơ lửng ở giữa (như platform mỏng).
        if (IsSliding || _isSwimHorizontal)
        {
            Vector2 currentPos = (Vector2)_ceilingCheck.position + (Vector2)(transform.up * -0.05f);
            // Tính toán vị trí gốc trong không gian World (bao gồm cả xoay/scale)
            Vector2 targetPos = transform.TransformPoint(_originalCeilingCheckPos);
            
            float distance = Vector2.Distance(currentPos, targetPos);
            
            // Dùng CircleCast để quét dọc theo đường đứng dậy
            Vector2 direction = (targetPos - currentPos).normalized;
            RaycastHit2D hit = Physics2D.BoxCast(currentPos, _ceilingCheckSize, _rb.rotation, direction, distance, _groundLayer);
            
            return hit.collider != null;
        }

        // Trường hợp bình thường: Check tại chỗ
        return Physics2D.OverlapBox(_ceilingCheck.position, _ceilingCheckSize, _rb.rotation, _groundLayer);
    }

    /// <summary>
    /// Kiểm tra xem có vật cản dưới chân ngăn cản việc đứng thẳng dậy không (dùng khi bơi ngang)
    /// </summary>
    public bool CheckForGroundExpansion()
    {
        if (_groundCheck == null) return false;

        // Chỉ cần kiểm tra khi đang bơi ngang (vì slide giữ chân cố định rồi)
        if (_isSwimHorizontal)
        {
            // Lùi điểm bắt đầu quét vào trong (lên trên) 0.05f để tránh kẹt biên
            Vector2 currentPos = (Vector2)_groundCheck.position + (Vector2)(transform.up * 0.05f);
            Vector2 targetPos = transform.TransformPoint(_originalGroundCheckPos);
            
            float distance = Vector2.Distance(currentPos, targetPos);
            
            // Nếu khoảng cách quá nhỏ, check tại chỗ
            if (distance < 0.01f) return Physics2D.OverlapBox(currentPos, _groundCheckSize, _rb.rotation, _groundLayer);

            Vector2 direction = (targetPos - currentPos).normalized;
            RaycastHit2D hit = Physics2D.BoxCast(currentPos, _groundCheckSize, _rb.rotation, direction, distance, _groundLayer);
            
            return hit.collider != null;
        }

        return false;
    }

    /// <summary>
    /// Bắt đầu lao xuống đất (Air Dive)
    /// </summary>
    public void StartAirDive(float speed)
    {
        IsDiving = true;
        // Set vận tốc X = 0 để lao thẳng xuống, Y = tốc độ âm
        _rb.linearVelocity = new Vector2(0f, -speed);
    }

    /// <summary>
    /// Kết thúc lao xuống đất
    /// </summary>
    public void StopAirDive()
    {
        IsDiving = false;
        // Không cần reset vận tốc, để physics tự xử lý va chạm với đất
    }

    /// <summary>
    /// Lật hướng nhân vật. Được public để các Ability (như WallJump) có thể điều khiển.
    /// </summary>
    public void Flip()
	{
        // Đảo chiều trạng thái quay mặt
        _facingRight = !_facingRight;

        if (_useScaleFlip)
        {
            Vector3 theScale = transform.localScale;
            theScale.x *= -1;
            transform.localScale = theScale;
        }
    }

    /// <summary>
    /// Cập nhật trạng thái bám tường (Gọi từ WallJumpAbility)
    /// </summary>
    public void SetClinging(bool isClinging)
    {
        IsClinging = isClinging;
    }

    /// <summary>
    /// Cập nhật trạng thái đu dây (Gọi từ ZiplineAbility)
    /// </summary>
    public void SetZiplining(bool isZiplining)
    {
        IsZiplining = isZiplining;

        if (IsZiplining)
        {
            // Nếu đang bơi mà bắt đầu đu dây, buộc thoát trạng thái bơi ngay lập tức
            // để Animation chuyển sang Zipline.
            if (IsSwimming) StopSwimming();

            // FIX: Reset visualsRoot về góc 90 độ (thế đứng thẳng mặc định) 
            // để nhân vật không bị nằm ngang khi chuyển từ bơi sang zipline.
            if (_visualsRoot != null)
                _visualsRoot.localRotation = Quaternion.Euler(0, 0, 90f);
        }
    }

    /// <summary>
    /// Cập nhật trạng thái leo thang (Gọi từ LadderClimbAbility)
    /// </summary>
    public void SetClimbing(bool isClimbing)
    {
        IsClimbing = isClimbing;
    }

    /// <summary>
    /// Cập nhật trạng thái chạm vùng thang (Gọi từ LadderClimbAbility)
    /// </summary>
    public void SetTouchingLadder(bool isTouching)
    {
        IsTouchingLadder = isTouching;
    }

    // Vẽ Gizmos để dễ dàng debug vị trí check trong Editor
    private void OnDrawGizmos()
    {
        if (_groundCheck != null)
        {
            Gizmos.color = Color.red;
            // Vẽ Box khớp hoàn toàn với vùng BoxCast. 
            // BoxCast quét từ tâm đi xuống 0.1f, nên Gizmo mô phỏng vùng quét đó.
            Vector3 gizmoCenter = _groundCheck.position + Vector3.down * 0.05f;
            Gizmos.DrawWireCube(gizmoCenter, new Vector3(_groundCheckSize.x, _groundCheckSize.y, 0.1f));
        }

        if (_ceilingCheck != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(_ceilingCheck.position, _ceilingCheckSize);
        }
    }

    /// <summary>
    /// Cập nhật trạng thái IsSubmerged.
    /// Trạng thái này đúng khi swim trigger (hoặc body) chạm vào vùng flood hiện tại.
    /// </summary>
    private void UpdateSubmergedState()
    {
        if (CurrentFlood == null)
        {
            IsSubmerged = false;
            return;
        }

        if (_floodColliders.TryGetValue(CurrentFlood, out Collider2D dominantFloodCollider))
        {
            // Logic FE2: Bơi nếu Torso chìm. 
            // Sử dụng cạnh trên (top) của Trigger làm điểm quyết định.
            Vector2 checkPoint;
            
            if (_swimTriggerCollider != null)
            {
                // Lấy điểm cao nhất của Trigger (vùng cổ)
                checkPoint = new Vector2(_swimTriggerCollider.bounds.center.x, _swimTriggerCollider.bounds.max.y);
            }
            else
            {
                // Fallback nếu không có trigger riêng: lấy vị trí khoảng 75% chiều cao thân
                checkPoint = (Vector2)_bodyCollider.bounds.center + Vector2.up * (_bodyCollider.size.y * 0.25f);
            }

            IsSubmerged = dominantFloodCollider.OverlapPoint(checkPoint);
        }
        else
        {
            IsSubmerged = false;
        }
    }

    private void UpdateCurrentFloodAndState()
    {
        // Cache lại Flood hiện tại trước khi tính toán lại, để dùng cho ExitWater nếu cần
        IFloodZone previousFlood = CurrentFlood;

        // 1. Xác định vùng flood chủ đạo
        if (_activeFloodZones.Count == 0)
        {
            CurrentFlood = null;
        }
        else
        {
            IFloodZone dominantFlood = null;
            // Ưu tiên tuyệt đối cho Lava
            foreach (var flood in _activeFloodZones)
            {
                if (flood.Type == FloodType.Lava) { dominantFlood = flood; break; }
            }

            // Nếu không có Lava, tìm vùng có AirDrainRate cao nhất
            if (dominantFlood == null)
            {
                dominantFlood = _activeFloodZones[0];
                for (int i = 1; i < _activeFloodZones.Count; i++)
                {
                    if (_activeFloodZones[i].AirDrainRate > dominantFlood.AirDrainRate)
                    {
                        dominantFlood = _activeFloodZones[i];
                    }
                }
            }
            CurrentFlood = dominantFlood;
        }

        // 2. Cập nhật trạng thái toàn cục
        _isInFloodZone = CurrentFlood != null;

        // 3. Cập nhật trạng thái bơi
        if (!_isInFloodZone && IsSwimming) // Nếu không còn ở trong bất kỳ vùng flood nào mà vẫn đang bơi
        {
            // Truyền previousFlood vào để phát âm thanh của vùng nước vừa thoát
            ExitWater(previousFlood);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // FIX: Dùng GetComponentInParent để tìm IFloodZone ngay cả khi script nằm ở object cha của Collider
        IFloodZone flood = other.GetComponentInParent<IFloodZone>();
        if (flood != null && !_activeFloodZones.Contains(flood))
        {
            _activeFloodZones.Add(flood);
            _floodColliders[flood] = other;
            UpdateCurrentFloodAndState();
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Cần thiết cho trường hợp Teleport thẳng vào vùng Flood
        IFloodZone flood = other.GetComponentInParent<IFloodZone>();
        if (flood != null && !_activeFloodZones.Contains(flood))
        {
            _activeFloodZones.Add(flood);
            _floodColliders[flood] = other;
            UpdateCurrentFloodAndState();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // FIX: Tương tự, dùng GetComponentInParent để xác định vùng vừa thoát
        IFloodZone flood = other.GetComponentInParent<IFloodZone>();
        if (flood != null && _activeFloodZones.Contains(flood))
        {
            // Kiểm tra xem có bất kỳ bộ phận nào của player còn chạm vào vùng flood này không.
            bool isBodyTouching = _bodyCollider != null && _bodyCollider.IsTouching(other);
            bool isTriggerTouching = _swimTriggerCollider != null && _swimTriggerCollider.IsTouching(other);
            bool isFeetTouching = _feetCollider != null && _feetCollider.enabled && _feetCollider.IsTouching(other);

            // Nếu vẫn còn chạm -> chưa thực sự thoát ra -> bỏ qua
            if (isBodyTouching || isTriggerTouching || isFeetTouching)
            {
                return;
            }

            // Nếu không còn chạm, đây là một lần thoát thực sự khỏi vùng flood này.
            _activeFloodZones.Remove(flood);
            _floodColliders.Remove(flood);
            UpdateCurrentFloodAndState();
        }
    }

    private void ExitWater(IFloodZone floodOverride = null)
    {
        StopSwimming();
        
        // Nếu đang bơi ngang mà thoát nước, xử lý collider.
        // CHỈ khôi phục nếu không đang trượt. Nếu đang trượt, SlideAbility sẽ lo việc khôi phục.
        if (_isSwimHorizontal && !IsSliding)
        {
            ResetCollider();
        }
        // Dù có reset collider hay không, trạng thái bơi ngang phải được tắt khi ra khỏi nước.
        _isSwimHorizontal = false;

        // FIX: Ưu tiên dùng floodOverride (nếu có) để phát tiếng khi CurrentFlood đã bị null do ra khỏi trigger
        IFloodZone floodToPlay = floodOverride != null ? floodOverride : CurrentFlood;

        if (floodToPlay != null)
        {
            PlaySound(floodToPlay.SplashSound);
        }

        // Luôn áp dụng một lực đẩy cố định khi thoát khỏi nước để tạo cảm giác "pop" ra ngoài
        // Việc reset vận tốc Y về 0 đảm bảo lực đẩy luôn có độ mạnh đồng nhất bất kể vận tốc bơi trước đó
        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, 0f);
        _rb.AddForce(Vector2.up * _exitFloodForce, ForceMode2D.Impulse);

        // Kích hoạt sự kiện để báo cho Controller biết
        OnExitWaterEvent?.Invoke();
    }

    private void StartSwimming()
    {
        // FIX: Nếu đang Zipline, không chuyển sang trạng thái bơi dù có chạm nước
// Nếu đang Zipline, tuyệt đối không được chuyển sang trạng thái bơi
        if (IsZiplining) return;
        IsSwimming = true;

        // Vô hiệu hóa collider chân khi bơi để tránh kẹt vào các góc hẹp của CompositeCollider
        if (_feetCollider != null) _feetCollider.enabled = false;

        // Fix bug: Nếu đang Dive mà chạm nước, hủy trạng thái Dive và hãm tốc độ
        if (IsDiving)
        {
            IsDiving = false;
            // FIX: Thay vì hãm cứng về -_swimSpeed, nhân với hệ số để giữ lại quán tính (Plunge)
            // 0.6f nghĩa là giữ lại 60% tốc độ lao xuống -> Lao càng nhanh lặn càng sâu.
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.y * 0.6f);
        }
        else
        {
            // Nếu rơi tự do bình thường (không Dive), cũng hãm bớt nhưng ít quán tính hơn (30%)
            // Để tránh việc rơi tự do bình thường mà bị chìm quá sâu
            if (_rb.linearVelocity.y < -_speed)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.y * 0.3f);
            }
        }

        _movementLockTimer = 0f; // Reset khóa di chuyển nếu rơi xuống nước để điều khiển được ngay
        // Phát âm thanh từ FloodController hiện tại
        if (CurrentFlood != null)
        {
            PlaySound(CurrentFlood.SplashSound);
        }
        _rb.gravityScale = 0f; // Tắt trọng lực để đứng yên tại chỗ
    }

    private void StopSwimming()
    {
        IsSwimming = false;

        // Khôi phục collider chân khi thoát khỏi trạng thái bơi
        if (_feetCollider != null) _feetCollider.enabled = true;

        // Chỉ khôi phục trọng lực nếu KHÔNG phải đang đu dây (Zipline cần gravity = 0)
        if (!IsZiplining)
            _rb.gravityScale = _originalGravityScale; 
    }

    /// <summary>
    /// Hàm public để các Ability khác (WallJump, Slide...) gọi phát âm thanh qua AudioSource của Player.
    /// </summary>
    public void PlaySound(AudioClip clip)
    {
        // Chỉ phát âm thanh nếu đã gán AudioSource và clip hợp lệ
        if (_sfxAudioSource != null && clip != null)
        {
            // Lấy âm lượng SFX từ SettingsManager
            float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
            _sfxAudioSource.PlayOneShot(clip, volume);
        }
    }

    /// <summary>
    /// Buộc thoát khỏi trạng thái bơi, được gọi bởi một Ability khác (như LadderClimb).
    /// Hàm này sẽ không tự động khôi phục trọng lực, cho phép Ability gọi nó tự quản lý.
    /// </summary>
    public void ForceExitSwimming()
    {
        if (!IsSwimming) return;

        IsSwimming = false;

        // Khôi phục collider chân khi buộc phải thoát bơi (ví dụ: bám thang)
        if (_feetCollider != null) _feetCollider.enabled = true;

        // Khôi phục collider nếu đang ở trạng thái bơi ngang, trừ khi đang trượt.
        if (_isSwimHorizontal && !IsSliding && !CheckForCeiling())
        {
            ResetCollider();
            _isSwimHorizontal = false;
        }
    }
}
