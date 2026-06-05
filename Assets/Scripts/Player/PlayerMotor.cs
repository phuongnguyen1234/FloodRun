using UnityEngine;
using UnityEngine.Events;
using Core.Interfaces;
using Core;
using UnityEngine.U2D.Animation;
using Unity.Netcode;
using Core.Events;

/// <summary>
/// PlayerMotor chịu trách nhiệm quản lý tất cả logic liên quan đến di chuyển
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMotor : NetworkBehaviour, IPlayerAbility, IPlayerMotorAttributes
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
    [SerializeField] public Transform _visualsRoot; // Public để PlayerAnimator đọc cho proxy
    [Tooltip("Lực đẩy riêng khi thực hiện nhảy vô hạn trên không")]
    [SerializeField] private float _infJumpForce = 12f;

    [Tooltip("Chiều rộng của Collider khi đang Slide. Nên nhỏ hơn hoặc bằng 1 để chui vừa khe 1 grid.")]
    [SerializeField] private float _slideWidth = 0.8f;
    
    [Tooltip("Tỉ lệ giảm chiều cao khi trượt (Đồng bộ từ SlideAbility).")]
    [SerializeField] private float _slideHeightRatio = 0.5f;

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
    private const float MAX_SLOPE_ANGLE = 60f;                                  // Độ dốc tối đa cho phép di chuyển bình thường
    private float _currentSlopeAngle = 0f;                                       // Lưu angle hiện tại
    
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
    [SerializeField] public bool _useScaleFlip = false; // Public để PlayerAnimator đọc cho proxy

    private SpriteResolver[] _spriteResolvers;
    private string _currentLabel;

    private Rigidbody2D _rb;
    private float _originalSpeed;
    private float _originalJumpForce;

    // Đồng bộ các trạng thái quan trọng cho Visuals
    private NetworkVariable<bool> _facingRight = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _facingRightLocal = true;
    public bool IsFacingRight => IsSpawned ? _facingRight.Value : _facingRightLocal;

    // Đồng bộ rotation của visuals khi bơi cho tất cả player (proxy)
    private NetworkVariable<float> _visualsRotationZ = new NetworkVariable<float>(90f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public float VisualsRotationZ => IsSpawned ? _visualsRotationZ.Value : 90f;

    private NetworkVariable<bool> _isSwimming = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isSwimmingLocal = false;
    private NetworkVariable<bool> _isClimbing = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isClimbingLocal = false;
    private NetworkVariable<bool> _isGrounded = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isGroundedLocal = false;
    private NetworkVariable<bool> _isClinging = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isClingingLocal = false;
    private NetworkVariable<bool> _isSliding = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isSlidingLocal = false;
    private NetworkVariable<bool> _isDiving = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isDivingLocal = false;
    private NetworkVariable<bool> _isZiplining = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private bool _isZipliningLocal = false;

    private Vector3 _velocity = Vector3.zero;
    public float OriginalGravityScale => _originalGravityScale;
    private float _originalGravityScale;
    private bool _isInFloodZone = false; // Đang nằm trong vùng trigger của Flood nói chung
    private bool _isAttemptingToJumpOutOfWater = false;
    private bool _isSwimHorizontal = false; // Trạng thái bơi ngang
    private bool _isAbilityEnabled = true;
    private float _movementLockTimer = 0f; // Timer khóa di chuyển
    private Vector2 _groundNormal = Vector2.up; // Vector pháp tuyến của mặt đất hiện tại
    private float _groundingCooldown = 0f; // Cooldown ngăn chặn nhận diện mặt đất ngay sau khi nhảy/phóng

    public float Speed => _speed;

    // FIX: Hỗ trợ nhiều vùng flood chồng nhau
    private readonly System.Collections.Generic.List<IFloodZone> _activeFloodZones = new System.Collections.Generic.List<IFloodZone>();
    private readonly System.Collections.Generic.Dictionary<IFloodZone, Collider2D> _floodColliders = new System.Collections.Generic.Dictionary<IFloodZone, Collider2D>();

    public IFloodZone CurrentFlood { get; private set; }

    public bool IsGrounded 
    { 
        get => IsSpawned ? _isGrounded.Value : _isGroundedLocal; 
        private set { if (IsSpawned && IsOwner) _isGrounded.Value = value; _isGroundedLocal = value; } 
    }
    public float JumpForce => _jumpForce;
    public float InfJumpForce => _infJumpForce;
    public bool IsSwimming 
    { 
        get => IsSpawned ? _isSwimming.Value : _isSwimmingLocal; 
        private set { if (IsSpawned && IsOwner) _isSwimming.Value = value; _isSwimmingLocal = value; } 
    }
    public bool IsSubmerged { get; private set; } // True khi player ngập trong nước/flood (dựa vào swim trigger)
    public bool IsClinging 
    { 
        get => IsSpawned ? _isClinging.Value : _isClingingLocal; 
        private set { if (IsSpawned && IsOwner) _isClinging.Value = value; _isClingingLocal = value; } 
    }
    public bool IsSliding 
    { 
        get => IsSpawned ? _isSliding.Value : _isSlidingLocal; 
        private set { if (IsSpawned && IsOwner) _isSliding.Value = value; _isSlidingLocal = value; } 
    }
    public bool IsDiving 
    { 
        get => IsSpawned ? _isDiving.Value : _isDivingLocal; 
        private set { if (IsSpawned && IsOwner) _isDiving.Value = value; _isDivingLocal = value; } 
    }
    public bool IsZiplining 
    { 
        get => IsSpawned ? _isZiplining.Value : _isZipliningLocal; 
        set { if (IsSpawned && IsOwner) _isZiplining.Value = value; _isZipliningLocal = value; } 
    }
    public bool IsTouchingLadder { get; private set; } // Trạng thái đang chạm vào vùng thang (Trigger)
    public bool IsClimbing 
    { 
        get => IsSpawned ? _isClimbing.Value : _isClimbingLocal; 
        private set { if (IsSpawned && IsOwner) _isClimbing.Value = value; _isClimbingLocal = value; } 
    }
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
    public event System.Action OnJumpTriggered; // Sự kiện báo hiệu cú nhảy
    public event System.Action OnTeleported;   // Sự kiện báo hiệu dịch chuyển tức thời

    private Vector2 _inputMoveDirection;
    public Vector2 InputMoveDirection => _inputMoveDirection;

    public bool JumpInput { get; set; } // Lưu trữ trạng thái nút nhảy

    /// <summary>
    /// Cập nhật nhãn (Label) cho toàn bộ các bộ phận cơ thể dựa trên trạng thái.
    /// </summary>
    public void UpdateSpriteLabels()
    {
        string targetLabel = _idleLabel;
        float speed = Mathf.Abs(_rb.linearVelocity.x);

        // FIX: Thêm IsClinging vào điều kiện chọn nhãn Side để quay mặt ra ngoài tường dù speed = 0
        if (IsClimbing)
        {
            targetLabel = _climbLabel;
        }
        else if (speed > 0.1f || IsSwimming || IsClinging)
        {
            targetLabel = IsFacingRight ? _rightLabel : _leftLabel;
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
        
        // Reset các trạng thái vật lý và dọn dẹp flood
        _activeFloodZones.Clear();
        _floodColliders.Clear();
        
        // Reset tất cả các flags trạng thái để tránh lỗi animation khi hồi sinh
        IsSwimming = false;
        IsSubmerged = false;
        IsClinging = false;
        IsSliding = false;
        IsDiving = false;
        IsClimbing = false;
        IsZiplining = false;
        
        ResetCollider(); // Đảm bảo collider và rotation trở về mặc định
        CurrentFlood = null;
    }

    public void ResetAttributes()
    {
        _speed = _originalSpeed;
        _jumpForce = _originalJumpForce;
        ResetGravityScale();
    }
    #endregion

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        
        // Đảm bảo ban đầu luôn là Dynamic để Singleplayer hoạt động bình thường
        _originalSpeed = _speed;
        _originalJumpForce = _jumpForce;
        _rb.bodyType = RigidbodyType2D.Dynamic;
        
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

    public override void OnNetworkSpawn()
    {
        // Nếu là chủ sở hữu (hoặc Singleplayer): Dynamic để tính vật lý
        // Nếu là máy khách khác: Kinematic để máy đó không tự tính vật lý cho nhân vật của mình
        _rb.bodyType = IsOwner ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        
        if (IsOwner)
        {
            _rb.gravityScale = _originalGravityScale;
            
            // Đồng bộ trạng thái hiện tại (nếu có) lên mạng ngay khi spawn
            _facingRight.Value = _facingRightLocal;
            _isSwimming.Value = _isSwimmingLocal;
            _isClimbing.Value = _isClimbingLocal;
            _isGrounded.Value = _isGroundedLocal;
            _isClinging.Value = _isClingingLocal;
            _isSliding.Value = _isSlidingLocal;
            _isDiving.Value = _isDivingLocal;
            _isZiplining.Value = _isZipliningLocal;
            _visualsRotationZ.Value = 90f; // Rotation mặc định
        }

        // Đăng ký callback để đồng bộ Collider cho Proxy
        _isSliding.OnValueChanged += (oldVal, newVal) => {
            if (!IsOwner) { if (newVal) ResizeCollider(_slideHeightRatio, true); else ResetCollider(); }
        };
        _isSwimming.OnValueChanged += (oldVal, newVal) => {
            if (!IsOwner) { if (newVal) ResizeCollider(0f, false); else ResetCollider(); }
        };

        // Thông báo cho MultiplayerManager biết Local Player đã được spawn thành công
        if (IsOwner && TryGetComponent<IPlayer>(out var player))
        {
            GameplayEvents.TriggerLocalPlayerSpawned(player);
        }
    }

    void FixedUpdate()
    {
        // Tránh việc các máy khách (Proxy) tự tính toán va chạm/mặt đất cho nhân vật của người khác
        if (IsSpawned && !IsOwner) return;

        // FIX: Đảm bảo Singleplayer luôn là Dynamic để chuyển động và nhảy bình thường
        // NetworkRigidbody2D có thể auto-set kinematic, nên phải override nó mỗi frame cho singleplayer
        // NHƯNG: Nếu đang bám tường (IsClinging), thì WallJumpAbility sẽ set Kinematic, đừng override nó
        if (!IsSpawned && !IsClinging)
            _rb.bodyType = RigidbodyType2D.Dynamic;

        if (!_isAbilityEnabled) return;

        UpdateGroundNormal(); // Cập nhật pháp tuyến mặt đất mỗi frame vật lý

        // Giảm cooldown chặn mặt đất
        if (_groundingCooldown > 0) _groundingCooldown -= Time.fixedDeltaTime;

        // CẢI TIẾN: Cập nhật lại vùng Flood chủ đạo mỗi frame vật lý nếu đang chạm nhiều vùng nước.
        // Điều này đảm bảo nếu một trong các vùng nước thay đổi loại (ví dụ: từ Water sang Acid), 
        // người chơi sẽ nhận diện được sự thay đổi về Drain Rate ngay lập tức.
        if (_activeFloodZones.Count > 1)
        {
            UpdateCurrentFloodAndState();
        }

        bool wasGrounded = IsGrounded;
        IsGrounded = false;

        // CẢI TIẾN: Nếu đang bám tường, leo thang, bơi hoặc đu dây, 
        // ta mặc định là không chạm đất để tránh xung đột logic.
        if (IsClinging || IsClimbing || IsSwimming || IsZiplining)
        {
            IsGrounded = false;
        }
        // CẢI TIẾN: Disable hoàn toàn việc quét mặt đất khi đang bơi để tiết kiệm hiệu năng
        // và tránh các logic đáp đất nhầm lẫn khi đang ở trong nước.
        else if (_groundCheck != null && !IsSwimming)
        {
            // CẢI TIẾN QUAN TRỌNG: Bắt đầu quét từ một điểm cao hơn _groundCheck (0.1f) 
            // để đảm bảo nếu chân bị lún sâu vào sàn (do Snap), BoxCast vẫn phát hiện được mặt sàn.
            Vector2 castOrigin = (Vector2)_groundCheck.position + Vector2.up * 0.1f;
            
            float baseDist = 0.4f;
            float totalCheckDist = baseDist + 0.1f; // Tăng thêm một chút để nhận diện mặt đất tốt hơn khi lên dốc

            // FIX: Sử dụng 0f thay vì _rb.rotation để hộp kiểm tra luôn thẳng đứng so với thế giới.
            // Điều này giúp việc phát hiện mặt đất ổn định hơn khi nhân vật bị nghiêng theo dốc.
            RaycastHit2D hit = Physics2D.BoxCast(castOrigin, _groundCheckSize, 0f, Vector2.down, totalCheckDist, _groundLayer);

            // CẢI TIẾN: Chỉ cho phép IsGrounded = true nếu không trong thời gian cooldown nhảy/phóng (_groundingCooldown).
            // Điều này thay thế hoàn toàn việc kiểm tra vận tốc Y, giúp nhân vật lên dốc (vY > 0) vẫn Grounded bình thường.
            bool isGroundedThisFrame = hit.collider != null && !hit.collider.isTrigger && hit.normal.y > 0.45f && _groundingCooldown <= 0;

            IsGrounded = isGroundedThisFrame;
        }

        if (IsGrounded && !wasGrounded)
            OnLandEvent.Invoke();

        // Xử lý xoay người theo dốc khi đang trượt (Slide)
        // Chỉ xoay khi đang ở dưới đất để tránh xoay loạn xạ trên không
        if (IsSliding && IsGrounded) HandleSlopeRotation();
        else if (IsSwimming && !IsZiplining) HandleSwimmingRotation();

        // RESET ROTATION & COLLIDER STATE
        // FIX: Khi đang Clinging, không được reset rotation hoặc bật lại chân vì WallJump đang quản lý
        if (!IsSliding && !IsZiplining && !IsClinging && !IsSwimming)
        {
            SetTargetVisualsRotation(90f); // Đặt lại góc xoay visual về 90 độ (đứng thẳng)
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

            SetTargetVisualsRotation(targetAngle);
        }
        else
        {
            SetTargetVisualsRotation(90f); // Về tư thế đứng thẳng khi không di chuyển
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
            // Tính angle: 0° = flat, 90° = vertical wall
            _currentSlopeAngle = Vector2.Angle(_groundNormal, Vector2.up);
        }
        else
        {
            _groundNormal = Vector2.up;
            _currentSlopeAngle = 0f;
        }
    }

    /// <summary>
    /// Lấy góc độ dốc hiện tại (0° = mặt phẳng, 90° = tường đứng)
    /// </summary>
    public float GetCurrentSlopeAngle() => _currentSlopeAngle;

    /// <summary>
    /// Lấy vector pháp tuyến của sàn hiện tại
    /// </summary>
    public Vector2 GetCurrentSlopeNormal() => _groundNormal;

    // Xoay Rigidbody để khớp với độ nghiêng của dốc (chỉ nếu slope <= 60°)
    private void HandleSlopeRotation()
    {
        // FIX: Nếu dốc > 60°, không xoay và không cho phép di chuyển bình thường
        if (_currentSlopeAngle > MAX_SLOPE_ANGLE)
        {
            // Khóa rigidbody tại 0 độ
            _rb.SetRotation(0f);
            return;
        }

        // Tính góc xoay từ vector pháp tuyến (Normal) so với vector lên (Up)
        float angle = Vector2.SignedAngle(Vector2.up, _groundNormal);
        
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
        // CẢI TIẾN: Loại bỏ IsDiving khỏi danh sách chặn để cho phép Air Control (di chuyển ngang) khi đang lao xuống.
        if (IsZiplining || IsSliding || IsClimbing || IsClinging)
        {
            return;
        }

        // XỬ LÝ FLIP:
        if (IsSwimming)
        {
            // CẢI TIẾN: Khi bơi, ưu tiên Flip theo vận tốc thực tế của Rigidbody.
            // Nếu bơi chạm dốc và bị đẩy lùi, nhân vật sẽ tự động quay mặt về hướng trượt.
            float flipReference = Mathf.Abs(_rb.linearVelocity.x) > 0.5f ? _rb.linearVelocity.x : input.x;
            // FIX: Dùng IsFacingRight property thay vì _facingRight.Value để đồng nhất với singleplayer
            if (flipReference > 0.01f && !IsFacingRight) Flip();
            else if (flipReference < -0.01f && IsFacingRight) Flip();
        }
        else
        {
            // Trên cạn: Quay mặt theo phím nhấn để phản hồi tức thì.
            // FIX: Dùng IsFacingRight property thay vì _facingRight.Value để đồng nhất với singleplayer
            if (input.x > 0 && !IsFacingRight) Flip();
            else if (input.x < 0 && IsFacingRight) Flip();
        }
        
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

            // Bơi: Luôn giữ Collider hình vuông, không cần check input để thay đổi trạng thái bơi ngang/đứng nữa.
            // Logic resize đã được đưa vào StartSwimming().

            return; // Kết thúc hàm Move, bỏ qua logic đi bộ
        }

        float moveInput = input.x; // Khi đi bộ chỉ lấy trục X

        // FIX: Kiểm tra slope angle - quyết định cách xử lý di chuyển
        bool isSlopeTooDrastic = IsGrounded && _currentSlopeAngle > MAX_SLOPE_ANGLE;

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

            // FIX: Chỉ triệt tiêu vận tốc khi vận tốc dọc (Y) đã thực sự gần bằng 0 (đã chạm sàn vật lý)
            // Điều này ngăn việc nhân vật bị "khựng" giữa không trung khi BoxCast chạm đất sớm.
            float verticalVel = _rb.linearVelocity.y;
            if (IsGrounded)
            {
            // CẢI TIẾN: Nếu đứng yên trên dốc, ta triệt tiêu trọng lực để chống trượt (Creep).
            // Với Gravity = 3, lực kéo xuống rất mạnh nên friction alone không đủ để giữ Dynamic Body.
            // Tăng ngưỡng vận tốc Y từ 0.2 lên 0.5 để bù đắp cho gia tốc rơi lớn (Gravity = 3).
            if (moveInput == 0 && Mathf.Abs(_rb.linearVelocity.x) < 0.05f && Mathf.Abs(verticalVel) < 0.1f)
                {
                    // FIX: Khóa vận tốc và gravity khi đứng yên để tuyệt đối không trượt
                    _rb.linearVelocity = Vector2.zero;
                    verticalVel = 0f;
                    _rb.gravityScale = 0f; // Khóa trọng lực khi đứng yên để không bị trượt dốc
                }
            else 
            {
                // Khôi phục trọng lực khi bắt đầu di chuyển hoặc nhảy
                if (!IsSwimming && !IsClinging && !IsZiplining)
                    _rb.gravityScale = _originalGravityScale;

                // FIX: Chỉ triệt tiêu vận tốc Y khi đang ở trên mặt phẳng (slope < 5 độ).
                // Khi lên dốc, verticalVel > 0 là cần thiết để Rigidbody trượt mượt mà theo bề mặt dốc.
                // Nếu ép về 0, nhân vật sẽ bị khựng và nảy khỏi mặt đất (làm IsGrounded = false).
                if (_currentSlopeAngle < 5f)
                {
                    if (verticalVel > 0.01f && verticalVel < 1.0f) verticalVel = 0f;
                }
            }
        }
        else if (!IsSwimming && !IsClinging && !IsZiplining)
        {
            // Đảm bảo trọng lực được trả lại khi đang ở trên không
            _rb.gravityScale = _originalGravityScale;
            }

            _rb.linearVelocity = new Vector2(smoothedX, verticalVel);
        }
        else if (isSlopeTooDrastic)
        {
            // Slope > 60°: Dùng slippery material và gravity bình thường để player trượt xuống
            if (_bodyCollider != null) _bodyCollider.sharedMaterial = _slipperyMaterial;
            if (_feetCollider != null) _feetCollider.sharedMaterial = _slipperyMaterial;
            
            // Khôi phục gravity bình thường để trượt xuống
            if (!IsSwimming && !IsClinging && !IsZiplining)
                _rb.gravityScale = _originalGravityScale;
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

    /// <summary>
    /// Gọi khi Player bị dịch chuyển bởi Dev Tool hoặc Event để các Ability tự hủy trạng thái.
    /// </summary>
    public void NotifyTeleported()
    {
        _groundingCooldown = 0.15f;
        OnJumpTriggered?.Invoke(); // Reset animator buffer
        OnTeleported?.Invoke();    // Thông báo cho các Ability thoát trạng thái (Climb, Cling, Zipline)
    }

    public void ResetGravityScale() => SetGravityScale(_originalGravityScale);
    #endregion

    /// <summary>
    /// Thông báo cho Animator và các hệ thống khác rằng một cú nhảy đã xảy ra.
    /// Dùng cho các Ability tự áp dụng lực nhảy riêng (như WallJump).
    /// </summary>
    public void NotifyJumpTriggered()
    {
        if (IsSpawned && IsOwner)
        {
            NotifyJumpTriggeredServerRpc();
        }
        else if (!IsSpawned) // Singleplayer
        {
            _groundingCooldown = 0.15f;
            OnJumpTriggered?.Invoke();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void NotifyJumpTriggeredServerRpc()
    {
        _groundingCooldown = 0.15f; // Khóa nhận diện mặt đất trong 0.15s
        NotifyJumpTriggeredClientRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void NotifyJumpTriggeredClientRpc()
    {
        // This will run on all clients (including the owner)
        OnJumpTriggered?.Invoke();
    }

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

            _groundingCooldown = 0.15f; // Khóa nhận diện mặt đất khi nhảy

            // FIX: Không gán IsGrounded = false ở đây nữa. 
            // Hãy để IsGrounded duy trì trạng thái 'true' cho đến hết frame này 
            // để các hệ thống khác (như SlideAbility) có thể nhận diện được lần chạm đất này.
            // IsGrounded sẽ tự động về false ở FixedUpdate tiếp theo nhờ _groundingCooldown.

            // Kích hoạt sự kiện để Animator lắng nghe
            OnJumpTriggered?.Invoke();
        }
    }

    /// <summary>
    /// Bắt đầu trạng thái trượt: Giảm collider, chỉnh ceiling check
    /// </summary>
    public void StartSliding(float heightRatio)
    {
        IsSliding = true;
        ResetGravityScale(); // Đảm bảo trọng lực hoạt động khi bắt đầu trượt
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
                // LOGIC TRƯỢT (Slide): Giảm chiều cao theo tỉ lệ, phình ngang và giữ đáy sát đất
                newHeight = _originalColliderSize.y * heightRatio;
                newWidth = _slideWidth; 

                float oldBottomY = _originalColliderOffset.y - (_originalColliderSize.y / 2f);
                if (_feetCollider != null)
                {
                    float feetBottom = _feetCollider.offset.y - _feetCollider.radius;
                    if (feetBottom < oldBottomY) oldBottomY = feetBottom;
                }
                newOffsetY = oldBottomY + (newHeight / 2f);

                // TRƯỢT: Cần cập nhật vị trí Ceiling Check để kiểm tra việc đứng dậy an toàn
                if (_ceilingCheck != null)
                {
                    float newTopY = newOffsetY + (newHeight / 2f);
                    float originalTopY = _originalColliderOffset.y + (_originalColliderSize.y / 2f);
                    float gap = _originalCeilingCheckPos.y - originalTopY;
                    _ceilingCheck.localPosition = new Vector3(_originalCeilingCheckPos.x, newTopY + gap, _originalCeilingCheckPos.z);
                }
                
                if (_groundCheck != null)
                {
                    _groundCheck.localPosition = _originalGroundCheckPos;
                    _groundCheckSize = _originalGroundCheckSize;
                }
            }
            else
            {
                newWidth = _originalColliderSize.x;
                newHeight = _originalColliderSize.x;
                newOffsetY = _originalColliderOffset.y;
                 // BƠI: KHÔNG thay đổi vị trí Ceiling/Ground Check. 
                // Chúng giữ nguyên ở vị trí "Đầu" và "Chân" gốc để check xem có thể đứng thẳng dậy không.
                if (_ceilingCheck != null) _ceilingCheck.localPosition = _originalCeilingCheckPos;
                if (_groundCheck != null) _groundCheck.localPosition = _originalGroundCheckPos;
            }

            _bodyCollider.size = new Vector2(newWidth, newHeight);
            _bodyCollider.offset = new Vector2(_originalColliderOffset.x, newOffsetY);

            // Cập nhật vị trí của swim trigger để nó di chuyển cùng với thân người
            if (_swimTriggerCollider != null)
            {
                float deltaY = newOffsetY - _originalColliderOffset.y;

                // Chỉ di chuyển theo DeltaY của thân, không cần Offset thủ công
                _swimTriggerCollider.transform.localPosition = new Vector3(
                    _originalSwimTriggerPos.x,
                    _originalSwimTriggerPos.y + deltaY,
                    _originalSwimTriggerPos.z);

                // Đồng bộ xoay cho Swim Trigger nếu nó là Box
                if (_swimTriggerCollider is BoxCollider2D box)
                {
                    // Bơi Square: Giữ Trigger hình vuông đồng nhất với Body để check ngập nước ổn định
                    box.size = !keepBottomFixed
                        ? new Vector2(newWidth, _originalSwimTriggerSize.y) 
                        : new Vector2(_originalSwimTriggerSize.x, _originalSwimTriggerSize.y * heightRatio);
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
        if (_bodyCollider == null) return false;

        // Slide: Vị trí _ceilingCheck thực tế đã bị hạ thấp.
        // Để kiểm tra xem có thể ĐỨNG DẬY được không, ta cần quét (Sweep) từ vị trí thấp lên vị trí cao (Gốc).
        if (IsSliding)
        {
            // 1. Tính toán đỉnh của body collider hiện tại (khi đang slide/bơi ngang)
            Vector2 currentBodyTopWorld = (Vector2)transform.position + _bodyCollider.offset + Vector2.up * (_bodyCollider.size.y / 2f);

            // 2. Tính toán đỉnh của body collider khi đứng thẳng (original)
            Vector2 originalBodyTopWorld = (Vector2)transform.position + _originalColliderOffset + Vector2.up * (_originalColliderSize.y / 2f);

            // 3. Khoảng cách cần quét là từ đỉnh body hiện tại đến đỉnh body gốc
            float sweepDistance = originalBodyTopWorld.y - currentBodyTopWorld.y;

            // Nếu khoảng cách quá nhỏ hoặc âm, không cần quét (đã đứng thẳng hoặc cao hơn)
            if (sweepDistance <= 0.01f) return false;
            
            // 4. Kích thước hộp quét: Chiều rộng bằng body collider, chiều cao rất nhỏ (làm sensor)
            Vector2 sweepBoxSize = new Vector2(_bodyCollider.size.x * 0.8f, 0.05f); // Hơi hẹp hơn body để tránh kẹt cạnh

            // 5. Thực hiện BoxCast từ đỉnh body hiện tại, hướng lên trên, với khoảng cách sweepDistance
            RaycastHit2D hit = Physics2D.BoxCast(currentBodyTopWorld, sweepBoxSize, _rb.rotation, Vector2.up, sweepDistance, _groundLayer);
            
            return hit.collider != null;
        }

        // Trường hợp bình thường: Check tại chỗ
        return Physics2D.OverlapBox(_ceilingCheck.position, _originalCeilingCheckSize, _rb.rotation, _groundLayer); // Luôn dùng kích thước gốc cho check bình thường
    }

    /// <summary>
    /// Kiểm tra xem có vật cản dưới chân ngăn cản việc đứng thẳng dậy không (dùng khi bơi ngang)
    /// </summary>
    public bool CheckForGroundExpansion()
    {
        if (_groundCheck == null) return false;
        // Vì khi bơi điểm check không đổi, chỉ cần kiểm tra tại vị trí gốc xem có bị chặn không
        return Physics2D.OverlapBox(_groundCheck.position, _originalGroundCheckSize, _rb.rotation, _groundLayer);
    }

    /// <summary>
    /// Bắt đầu lao xuống đất (Air Dive)
    /// </summary>
    public void StartAirDive(float speed)
    {
        IsDiving = true;
        ResetGravityScale(); // Đảm bảo trọng lực hoạt động khi lao xuống
        // CẢI TIẾN: Giữ nguyên vận tốc X hiện tại thay vì ép về 0 để không bị khựng ngang đột ngột khi bắt đầu Dive
        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, -speed);
    }

    /// <summary>
    /// Thiết lập hướng mặt cụ thể cho Player.
    /// </summary>
    public void SetFacing(bool faceRight)
    {
        if (IsFacingRight != faceRight) Flip();
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
        // FIX: Thêm guard để tránh flip lặp lại nếu đã facing hướng đó
        // Kiểm tra xem direction có thực sự cần đổi không
        bool newVal = !IsFacingRight;
        if (IsSpawned && _facingRight.Value == newVal) return; // Đã facing hướng này rồi, bỏ qua
        if (!IsSpawned && _facingRightLocal == newVal) return;

        // FIX: Kiểm tra quyền ghi trước (chỉ Owner mới được phép set network variable)
        if (IsSpawned && !IsOwner) return; // Proxy không được flip
        
        // Đảo chiều trạng thái quay mặt
        if (IsSpawned) _facingRight.Value = newVal;
        _facingRightLocal = newVal;

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
        // FIX: Kiểm tra quyền ghi (chỉ Owner mới được phép)
        if (IsSpawned && !IsOwner) return;
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
            SetTargetVisualsRotation(90f);        }    }

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

    /// <summary>
    /// Đặt góc xoay Z mục tiêu cho VisualsRoot. Chỉ Owner mới có quyền ghi lên NetworkVariable.
    /// </summary>
    public void SetTargetVisualsRotation(float zRotation)
    {
        // Chỉ Owner mới có quyền ghi vào NetworkVariable
        if (IsSpawned && !IsOwner) return;

        _visualsRotationZ.Value = zRotation;
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

        // CẢI TIẾN: Thay vì chỉ kiểm tra vùng Flood "chủ đạo", ta kiểm tra tất cả các vùng đang chạm.
    // Điều này giúp việc bơi qua 2 khối nước đặt cạnh nhau mượt mà hơn, tránh bị ngắt bơi ở khe tiếp giáp.
    bool submergedInAny = false;

    // Xác định điểm kiểm tra (checkPoint)
    Vector2 checkPoint;
    if (_swimTriggerCollider != null)
        {
            // Lấy điểm cao nhất của Trigger (vùng cổ/đầu)
        checkPoint = new Vector2(_swimTriggerCollider.bounds.center.x, _swimTriggerCollider.bounds.max.y);
        }
        else
        {
        checkPoint = (Vector2)_bodyCollider.bounds.center + Vector2.up * (_bodyCollider.size.y * 0.25f);
        }

        foreach (var flood in _activeFloodZones)
    {
        if (_floodColliders.TryGetValue(flood, out Collider2D col))
        {
            if (col != null && col.OverlapPoint(checkPoint))
            {
                submergedInAny = true;
                break;
            }
        }
    }

    IsSubmerged = submergedInAny;
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
        // Chỉ chủ sở hữu mới có quyền tính toán và cập nhật trạng thái bơi lên mạng
        if (IsSpawned && !IsOwner) return;

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
        if (IsSpawned && !IsOwner) return;

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
        if (IsSpawned && !IsOwner) return;

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
        
        // Khôi phục collider về trạng thái đứng thẳng nếu không đang trượt
        if (!IsSliding)
            ResetCollider();

        // Dù có reset collider hay không, trạng thái bơi ngang phải được tắt khi ra khỏi nước.
        _isSwimHorizontal = false;
        
        // Reset visualsRoot về tư thế đứng thẳng (90 độ) khi lên khỏi nước
        SetTargetVisualsRotation(90f);

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

        // FIX: Sử dụng NotifyJumpTriggered thay vì invoke trực tiếp để kích hoạt groundingCooldown (0.15s).
        // Điều này ngăn việc BoxCast quét trúng mặt nước ngay khi vừa "pop" lên.
        NotifyJumpTriggered();

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

        // Chuyển Collider sang hình vuông ngay khi bắt đầu bơi
        ResizeCollider(0f, false);
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

        // Khôi phục collider đứng thẳng
        if (!IsSliding) ResetCollider();
        
        _isSwimHorizontal = false;
    }
}