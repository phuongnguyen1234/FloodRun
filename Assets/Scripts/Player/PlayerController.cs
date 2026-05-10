using UnityEngine;
using Core.Interfaces; // Namespace chứa IPlayer mới tạo
using System.Linq;
using Core.Events;
using Core;
using TMPro;

/// <summary>
/// PlayerController là script trung tâm quản lý tất cả các khía cạnh của Player: Di chuyển, Air System, Trạng thái (chết, bơi, leo thang), và tương tác với các Ability khác (Ladder, Zipline, v.v.).
/// Nó kết hợp dữ liệu từ PlayerMotor (vật lý và trạng thái) và Player
/// </summary>
[RequireComponent(typeof(PlayerInputHandler), typeof(PlayerMotor))]
public class PlayerController : MonoBehaviour, IPlayer, IAirRefillable, IPlayerControllerAttributes
{
    [System.Serializable]
    public struct GibMapping
    {
        public Transform BoneTransform;
        public Sprite PartSprite;
    }

    private PlayerInputHandler _input;
    private PlayerMotor _motor;
    private PlayerAnimator _playerAnimator;
    private Rigidbody2D _rb;
    private Collider2D _collider;
    private IPlayerAbility[] _abilities;

    [Header("UI & Name Tag")]
    [SerializeField] private TMP_Text _nameTagText;
    [SerializeField] private bool _keepNameTagReadable = true;

    [Header("Air System")]
    [SerializeField] private float _maxAir = 100f;
    [Tooltip("Âm thanh khi bình dưỡng khí (bonus air) cạn kiệt")]
    [SerializeField] private AudioClip _bonusAirEmptySound;

    [Header("Death Effects")]
    [Tooltip("Âm thanh tiếng kêu hoặc tiếng va chạm khi Player chết")]
    [SerializeField] private AudioClip _playerDeathSound;
    [Tooltip("Âm thanh nổ hoặc va chạm mạnh kèm theo")]
    [SerializeField] private AudioClip _deathExplosionSound;

    [Header("Gib Settings (Part Explosion)")]
    [SerializeField] private GameObject _gibPrefab;
    [SerializeField] private GibMapping[] _gibMappings;
    [Tooltip("Layer dành riêng cho mảnh vỡ (ví dụ: PlayerGibs). Giúp các mảnh vỡ xuyên qua nhau nhưng vẫn chạm đất.")]
    [SerializeField] private LayerMask _gibLayer;

    private float _baseAir;   // Lượng khí cơ bản (tự hồi phục)
    private float _bonusAir;  // Lượng khí bổ sung từ Bubble (dùng trước, không tự hồi)
    private float _bonusAirMaxCap; // Dung tích tối đa của bình khí bonus hiện tại
    private float _originalGravityScale;
    private float _outOfFloodTimer = 0f; // Timer đếm thời gian khi ra khỏi nước
    private bool _isDead = false;
    private bool _isAbilityEnabled = true;
    private bool _isInfiniteAir = false;
    private bool _isInfiniteJump = false;
    private bool _isInvincible = false;
    private float _jumpCooldown = 0f; // Cooldown để ngăn spam lệnh nhảy khi giữ phím
    private DeathReason _lastDeathReason = DeathReason.Drowned; // Mặc định là chết đuối
    private float _currentAirChangeRate = 0f; // Biến lưu tốc độ thay đổi khí hiện tại

    private Vector2 _currentMoveInput;
    private bool _isTryingToJumpOutOfWater;

    // Thực thi interface IPlayer: Cho phép bên ngoài đọc trạng thái chết
    public bool IsDead => _isDead;

    public bool IsZiplining => _motor != null && _motor.IsZiplining;
    public DeathReason LastDeathReason => _lastDeathReason;
    public bool IsClinging => _motor != null && _motor.IsClinging;
    public bool IsSwimming => _motor != null && _motor.IsSwimming; // Trạng thái bơi được lấy từ PlayerMotor
    public bool IsSubmerged => _motor != null && _motor.IsSubmerged; // Trạng thái ngập trong nước
    public bool IsClimbing => _motor != null && _motor.IsClimbing; // Trạng thái leo thang

    // Triển khai CurrentFlood từ IPlayer: Trả về vùng nước mà Motor đang ghi nhận
    public IFloodZone CurrentFlood => _motor != null ? _motor.CurrentFlood : null;

    // Public getters cho MapManager đọc thông số hiển thị UI
    public float CurrentBaseAir => _baseAir;
    public float CurrentBonusAir => _bonusAir;
    public float CurrentBonusAirMax => _bonusAirMaxCap;
    public float CurrentAirChangeRate => _currentAirChangeRate;

    void Awake()
    {
        _input = GetComponent<PlayerInputHandler>();
        _motor = GetComponent<PlayerMotor>();
        _playerAnimator = GetComponent<PlayerAnimator>();
        _rb = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        
        _baseAir = _maxAir;
        _bonusAir = 0f;
        _bonusAirMaxCap = 0f;
        
        if (_rb != null) _originalGravityScale = _rb.gravityScale;

        // Lấy tất cả các component có implement IPlayerAbility (Motor, Input, v.v.)
        _abilities = GetComponents<IPlayerAbility>();
    }

    private void Start()
    {
        UpdateNameTag();
    }

    /// <summary>
    /// Đồng bộ tên từ dữ liệu lưu trữ lên UI trên đầu Player.
    /// </summary>
    public void UpdateNameTag()
    {
        if (_nameTagText == null) return;

        // Lấy tên từ DataManager (Singleton giữ PlayerProfile)
        if (DataManager.Instance != null && DataManager.Instance.Profile != null)
        {
            _nameTagText.text = DataManager.Instance.Profile.PlayerName;
        }
        else
        {
            _nameTagText.text = "Player"; // Fallback nếu data chưa load
        }
    }

    #region IPlayerAbility Implementation
    public void EnableAbility()
    {
        _isAbilityEnabled = true;
        foreach (var ability in _abilities)
        {
            if (ability != this) ability.EnableAbility();
        }
        if (_motor != null) _motor.ResetGravityScale();
    }

    public void DisableAbility()
    {
        _isAbilityEnabled = false;
        foreach (var ability in _abilities)
        {
            if (ability != this) ability.DisableAbility();
        }
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
        if (_motor != null) _motor.SetGravityScale(0f);
    }
    // Thực thi logic bất tử
    public void SetInvincible(bool isInvincible) => _isInvincible = isInvincible;

    public void SetInfiniteAir(bool enabled) => _isInfiniteAir = enabled;

    public void SetInfiniteJump(bool enabled) => _isInfiniteJump = enabled;

    public void Teleport(Vector3 position)
    {
        // 1. Tạm thời vô hiệu hóa Ability để thoát các trạng thái bám tường, đu dây, trượt...
        // Đặc biệt là Motor.DisableAbility() giờ sẽ xóa sạch danh sách vùng Flood cũ.
        DisableAbility();

        // 2. Dịch chuyển vị trí
        // Ta thêm một khoảng Offset nhẹ (ví dụ 0.5f lên trên) để tránh việc chân player bị kẹt vào collider của nút
        transform.position = position + Vector3.up * 0.5f;
        
        // 3. Reset các trạng thái vận tốc và thông báo Teleport
        _rb.linearVelocity = Vector2.zero; // Triệt tiêu hoàn toàn vận tốc cũ
        _motor.NotifyTeleported();

        // 4. Reset các timer nội bộ
        _jumpCooldown = 0f;
        
        // 4. Bật lại Ability
        // Hàm này sẽ khôi phục trọng lực và cho phép các Ability lắng nghe input trở lại
        EnableAbility();
        
        Debug.Log($"[DevTool] Player teleported to {position}");
    }
    #endregion

    private void OnEnable()
    {
        if (_motor != null) _motor.OnExitWaterEvent.AddListener(ResetJumpCooldown);
    }
    
    private void OnDisable()
    {
        if (_motor != null) _motor.OnExitWaterEvent.RemoveListener(ResetJumpCooldown);
    }

    void Update()
    {
        if (_isDead || !_isAbilityEnabled) return;

        // Giảm cooldown nhảy
        if (_jumpCooldown > 0) _jumpCooldown -= Time.deltaTime;

        // Đồng bộ trạng thái nhấn phím nhảy sang Motor để Animator có thể sử dụng
        _motor.JumpInput = _input.JumpInput;

        Vector2 input = _input.MoveInput;
        _isTryingToJumpOutOfWater = false;

        // Nếu đang bơi, sử dụng Space để bơi lên và Shift để lặn xuống
        if (_motor.IsSwimming)
        {
            float vertical = 0f;
            if (_input.JumpInput)
            {
                vertical += 1f; // Space -> Bơi lên (Up)
                _isTryingToJumpOutOfWater = true;
            }
            if (_input.DiveInput) vertical -= 1f; // Shift -> Lặn xuống (Down)
            input.y = vertical; // Ghi đè trục Y của MoveInput (W/S)
        }
        else
        {
            // Logic nhảy (bao gồm Infinite Jump)
            // Điều kiện nhảy: Giữ phím + Hết cooldown + (Chạm đất HOẶC bật InfJump)
            // Không cho phép nhảy khi: Đang bơi, Đang leo thang, Đang đu dây (để tránh phá vỡ logic vật lý riêng của các ability này)
            bool canJump = (_motor.IsGrounded || _isInfiniteJump) && !_motor.IsClinging; // Thêm điều kiện không đang bám tường
            if (_input.JumpInput && _jumpCooldown <= 0f && canJump && !_motor.IsClimbing && !_motor.IsSwimming && !_motor.IsZiplining)
            {
                // CẢI TIẾN: Nếu đang trượt, chỉ cho phép nhảy nếu không bị vướng trần (để có thể đứng thẳng dậy an toàn)
                if (_motor.IsSliding && _motor.CheckForCeiling()) return;

                // Nếu đang trượt hoặc đang lao xuống (Dive), việc nhảy sẽ ngắt các trạng thái này
                if (_motor.IsSliding) _motor.StopSliding();
                if (_motor.IsDiving) _motor.StopAirDive();

                // Nếu đang trên không và đang bật chế độ Inf Jump, dùng lực InfJumpForce. 
                // Nếu đang ở dưới đất, dùng lực JumpForce bình thường.
                float force = (_isInfiniteJump && !_motor.IsGrounded) ? _motor.InfJumpForce : _motor.JumpForce;
                _motor.Jump(force);
                
                _jumpCooldown = 0.2f; // Đặt cooldown ngắn để tránh cộng dồn lực (Bunny Hop)
            }
        }

        _currentMoveInput = input;
        HandleAirSystem();
        
        // Đảm bảo Name Tag không bị lật ngược khi Player lật localScale
        HandleNameTagReadability();
    }

    private void HandleNameTagReadability()
    {
        if (!_keepNameTagReadable || _nameTagText == null) return;

        // Nếu localScale của cha bị âm (do Flip), ta đảo ngược localScale của Text 
        // để nó luôn có Scale tuyệt đối là dương so với World Space.
        Vector3 currentScale = _nameTagText.transform.localScale;
        float parentDirection = transform.localScale.x;
        _nameTagText.transform.localScale = new Vector3(Mathf.Abs(currentScale.x) * (parentDirection > 0 ? 1 : -1), currentScale.y, currentScale.z);
    }

    void FixedUpdate()
    {
        if (_isDead || !_isAbilityEnabled) return;

        _motor.SetAttemptingToJumpOutOfWater(_isTryingToJumpOutOfWater);

        // Di chuyển vật lý nên được xử lý trong FixedUpdate để tránh jitter
        _motor.Move(_currentMoveInput);
    }

    /// <summary>
    /// Hàm nhận Air từ AirBubble. Cộng thẳng vào Bonus Air (đóng vai trò như bình dưỡng khí phụ).
    /// </summary>
    public bool AddBonusAir(float amount)
    {
        // Logic mới: Chỉ nhận bubble mới nếu "dung tích" của nó lớn hơn dung tích hiện tại.
        // Nếu dung tích hiện tại là 0 (đã dùng hết), có thể nhặt bất kỳ bubble nào.
        if (amount > _bonusAirMaxCap)
        {
            _bonusAirMaxCap = amount; // Đặt dung tích mới
            _bonusAir = amount;       // Nạp đầy bonus air theo dung tích mới

            // Tìm UI Manager thông qua Interface để tránh lỗi tham chiếu chéo giữa các Assembly (.asmdef)
            var uiManager = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IGameplayUIManager>().FirstOrDefault();
            uiManager?.ShowNotification($"Got {amount:F0} Air!", new Color(0.3f, 0.8f, 1f)); // Màu xanh dương nhạt

            return true; // Đã nhận khí thành công
        }
        return false; // Không nhận vì dung tích của bubble mới nhỏ hơn hoặc bằng dung tích hiện tại
    }

    #region Public Setters for Attributes
    public void SetMaxAir(float newMaxAir)
    {
        _maxAir = Mathf.Max(0, newMaxAir);
        // Đảm bảo air hiện tại không vượt quá max air mới
        _baseAir = Mathf.Min(_baseAir, _maxAir);
    }

    /// <summary>
    /// Thêm (hoặc bớt) một lượng air vào thanh air cơ bản.
    /// </summary>
    public void AddAir(float amount)
    {
        _baseAir += amount;
    }
    #endregion

    /// <summary>
    /// Hàm để các Ability (như Ladder) gọi khi chúng tự xử lý việc nhảy.
    /// Giúp reset cooldown nhảy của Controller để tránh double jump (lỗi super jump).
    /// </summary>
    public void ResetJumpCooldown()
    {
        // Tăng lên 0.3s để đảm bảo chặn hoàn toàn lệnh nhảy từ phím Space đang được giữ
        // khi người chơi vừa "pop" từ mặt nước lên hoặc nhảy thoát khỏi thang.
        _jumpCooldown = 0.3f;
    }

    public void PlaySound(AudioClip clip)
    {
        _motor.PlaySound(clip);
    }

    private void HandleAirSystem()
    {
        // Nếu đang bất tử hoặc bật Infinite Air, không tính toán trừ khí
        if (_isInvincible || _isInfiniteAir)
        {
            if (_isInfiniteAir)
            {
                _baseAir = _maxAir;
                _currentAirChangeRate = 0f;
            }
            return;
        }

        _currentAirChangeRate = 0f; // Reset mỗi frame
        IFloodZone currentFlood = _motor.CurrentFlood;

        if (_motor.IsSubmerged && currentFlood != null)
        {
            _outOfFloodTimer = 0f;
            ProcessAirLoss(currentFlood);
        }
        else
        {
            // Logic khi ra khỏi Flood
            _outOfFloodTimer += Time.deltaTime;

            // Ra khỏi flood được 1 giây thì hồi air
            // CHỈ HỒI BASE AIR, KHÔNG HỒI BONUS AIR
            if (_outOfFloodTimer >= 1.0f && _baseAir < _maxAir)
            {
                _currentAirChangeRate = 16f;
                _baseAir += _currentAirChangeRate * Time.deltaTime;
            }
        }

        // Clamp Air trong khoảng 0 - Max
        // Base Air không được vượt quá MaxAir gốc
        _baseAir = Mathf.Clamp(_baseAir, 0f, _maxAir);
        // Bonus Air không bao giờ âm
        _bonusAir = Mathf.Max(0f, _bonusAir);

        float totalAir = _baseAir + _bonusAir;

        // Kiểm tra chết
        if (totalAir <= 0f)
        {
            Die();
        }
    }

    private void ProcessAirLoss(IFloodZone flood)
    {
        if (flood.Type == FloodType.Lava)
        {
            _baseAir = 0; _bonusAir = 0;
            return;
        }

        _currentAirChangeRate = -flood.AirDrainRate;
        if (flood.ApplyDepthMultiplier)
            _currentAirChangeRate *= flood.GetDepthMultiplier(transform.position.y);

        float delta = _currentAirChangeRate * Time.deltaTime;

        if (delta < 0) // Mất khí
        {
            float drain = Mathf.Abs(delta);
            if (_bonusAir > 0)
            {
                _bonusAir -= drain;
                if (_bonusAir <= 0)
                {
                    _motor.PlaySound(_bonusAirEmptySound);
                    _bonusAirMaxCap = 0;
                    if (_bonusAir < 0) 
                    {
                        _baseAir += _bonusAir;
                        _bonusAir = 0;
                    }
                }
            }
            else _baseAir -= drain;
        }
        else _baseAir += delta; // Hồi khí trong nước (nếu có vùng đặc biệt)
        
    }

    public void Die(DeathReason reason = DeathReason.Drowned)
    {
        if (_isDead) return;
        _lastDeathReason = reason;
        _isDead = true;

        // 1. Lưu vận tốc cuối cùng để tạo quán tính (đang chạy mà chết thì xác phải văng đi)
        Vector2 deathVelocity = _rb != null ? _rb.linearVelocity : Vector2.zero;

        // 2. Tắt Animator - CỰC KỲ QUAN TRỌNG
        // Nếu không tắt, Animator sẽ "giằng co" với vật lý, khiến xương bị giật.
        if (_playerAnimator != null) _playerAnimator.enabled = false;

        // 3. Xử lý Rigidbody tổng (Cha)
        if (_rb != null)
        {
            _rb.simulated = false; 
        }

        // 4. Tắt va chạm
        foreach (var col in GetComponents<Collider2D>()) 
        {
            col.enabled = false;
        }

        // Cập nhật thống kê chết và kiểm tra thành tựu
        DataManager.Instance.Profile.RegisterDeath();
        GameplayEvents.TriggerPlayerDied();

        // 5. Hiệu ứng nổ mảnh vụn (Gibs)
        if (_gibPrefab != null && _gibMappings != null)
        {
            // Lấy index của layer từ LayerMask để gán cho GameObject
            int layerIndex = 0;
            int mask = _gibLayer.value;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    layerIndex = i;
                    break;
                }
            }

            for (int i = 0; i < _gibMappings.Length; i++)
            {
                var mapping = _gibMappings[i];
                if (mapping.BoneTransform == null || mapping.PartSprite == null) continue;

                // Tạo mảnh vụn tại đúng vị trí và góc quay của xương hiện tại
                GameObject gib = Instantiate(_gibPrefab, mapping.BoneTransform.position, mapping.BoneTransform.rotation);
                gib.layer = layerIndex;
                
                if (gib.TryGetComponent(out SpriteRenderer sr))
                {
                    sr.sprite = mapping.PartSprite;
                    // Tăng nhẹ sortingOrder theo chỉ số i để các bộ phận chồng lên nhau theo thứ tự xác định, tránh nhấp nháy (Z-fighting)
                    sr.sortingOrder = 10 + i; 

                    // 1. Tự động điều chỉnh BoxCollider2D vừa khít với kích thước của Sprite part
                    if (gib.TryGetComponent(out BoxCollider2D boxCol))
                    {
                        // bounds.size là kích thước thực tế của sprite trong Unity units
                        boxCol.size = sr.sprite.bounds.size;
                        // bounds.center giúp căn chỉnh collider đúng vị trí nếu Pivot của sprite bị lệch
                        boxCol.offset = sr.sprite.bounds.center;
                    }
                }

                if (gib.TryGetComponent(out Rigidbody2D gibRb))
                {
                    // CẤU HÌNH QUAN TRỌNG: Bỏ qua va chạm với các object cùng Layer mảnh vỡ
                    gibRb.excludeLayers = _gibLayer;

                    // 2. Chỉ lấy vận tốc hiện tại của Player, không thêm lực bắn phụ
                    gibRb.linearVelocity = deathVelocity;
                    gibRb.AddTorque(Random.Range(-360f, 360f)); // Cho mảnh vụn xoay tít
                }
            }
        }

        // Phát các âm thanh khi chết
        if (_deathExplosionSound != null) PlaySound(_deathExplosionSound);
        if (_playerDeathSound != null) PlaySound(_playerDeathSound);

        // Ẩn tất cả các bộ phận của Player (Skinning Mesh)
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in renderers) sr.enabled = false;

        // 6. Ngắt các script điều khiển khác
        foreach (var ability in _abilities)
        {
            if (ability != (IPlayerAbility)this) ability.DisableAbility();
        }

        this.enabled = false; // Ngắt chính script PlayerController này
    }
}
