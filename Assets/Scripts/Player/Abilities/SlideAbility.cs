using UnityEngine;

/// <summary>
/// Khả năng trượt (Slide) và lao xuống (Air Dive) của người chơi.
/// </summary>
public class SlideAbility : MonoBehaviour, IPlayerAbility
{
    [Header("Settings")]
    [SerializeField] private float _slideSpeed = 12f;
    [SerializeField] private float _maxSlideTime = 1.0f;
    [SerializeField] private float _slideCooldown = 0.5f;
    
    [Header("Collider Settings")]
    [Tooltip("Tỉ lệ chiều cao khi trượt (0.5 là giảm một nửa)")]
    [SerializeField] private float _heightReductionRatio = 0.5f;

    [Header("Air Dive Settings")]
    [SerializeField] private float _diveSpeed = 20f;
    [Tooltip("Thời gian chờ (delay) trước khi trượt nếu vẫn giữ phím Q khi tiếp đất từ cú Air Dive.")]
    [SerializeField] private float _postDiveSlideDelay = 0.15f;
    
    [Header("Audio")]
    [SerializeField] private AudioClip _slideSound;
    [SerializeField] private AudioClip _diveSound; // Âm thanh khi bắt đầu lao xuống

    [Header("Visual Effects")]
    [Tooltip("Hệ thống hạt (khói) khi trượt trên mặt đất.")]
    [SerializeField] private ParticleSystem _slideParticles;

    private PlayerInputHandler _input;
    private PlayerMotor _motor;
    private Rigidbody2D _rb;

    private bool _isSliding = false;
    private bool _isDiving = false;
    private float _slideTimer;
    private float _cooldownTimer;
    private bool _isAbilityEnabled = true;
    private bool _waitForSlideRelease = false; // Cờ chặn trượt liên tục khi giữ phím
    
    // Trạng thái chờ trượt sau khi dive
    private bool _isWaitingToSlide = false;
    private float _postDiveTimer;

    // Lưu hướng trượt để giữ nguyên hướng dù người chơi thả phím di chuyển
    private float _slideDirection;
    private float _particleWorldDirection; // Hướng cố định cho hiệu ứng khói khi trượt

    private void Awake()
    {
        _input = GetComponent<PlayerInputHandler>();
        _motor = GetComponent<PlayerMotor>();
        _rb = GetComponent<Rigidbody2D>();
    }

    public void EnableAbility() => _isAbilityEnabled = true;
    public void DisableAbility()
    {
        _isAbilityEnabled = false;
        if (_isSliding) StopSlide();
        if (_isDiving) StopDive();
        _isWaitingToSlide = false;
    }

    private void Update()
    {
        if (!_isAbilityEnabled) return;

        // Nếu người chơi đã thả phím, reset cờ chặn để cho phép trượt lần sau
        if (!_input.SlideInput)
        {
            _waitForSlideRelease = false;
        }

        // Luôn đồng bộ hướng của Particle theo hướng nhìn của Player mọi lúc
        SyncParticleRotation();

        // Giảm cooldown
        if (_cooldownTimer > 0) _cooldownTimer -= Time.deltaTime;

        // Logic Input: Nhấn Q -> Chỉ kích hoạt nếu không trong trạng thái chờ thả phím
        // FIX: Thêm điều kiện !_motor.IsClinging để chặn slide/dive khi đang bám tường
        if (_input.SlideInput && !_motor.IsSwimming && !_motor.IsZiplining && !_motor.IsClimbing && !_motor.IsClinging && _cooldownTimer <= 0 && !_waitForSlideRelease && !_isWaitingToSlide)
        {
            // 1. Nếu đang ở dưới đất -> Slide
            if (_motor.IsGrounded && !_isSliding && !_isDiving)
            {
                StartSlide();
            }
            // 2. Nếu đang ở trên không -> Air Dive
            else if (!_motor.IsGrounded && !_isDiving && !_isSliding)
            {
                StartDive();
            }
        }

        if (_isDiving)
        {
            HandleDiving();
        }

        if (_isWaitingToSlide)
        {
            HandlePostDiveWait();
        }
    }

    private void SyncParticleRotation()
    {
        if (_slideParticles == null) return;

        // Quy tắc: Khói luôn phun về phía sau lưng.
        // Prefab mặc định phun sang Phải (Y=90).
        // Nếu nhìn Phải -> Cần phun sang Trái -> Xoay 180 độ.
        // Nếu nhìn Trái -> Cần phun sang Phải -> Xoay 0 độ.
        float targetY = _motor.IsFacingRight ? 180f : 0f;
        _slideParticles.transform.localRotation = Quaternion.Euler(0, targetY, 0);
    }

    private void FixedUpdate()
    {
        // Di chuyển logic trượt sang FixedUpdate để vật lý ổn định hơn, tránh kẹt khi chui qua khe hẹp
        if (_isAbilityEnabled && _isSliding)
        {
            HandleSliding();
        }
    }

    private void StartSlide()
    {
        _isSliding = true;
        _slideTimer = _maxSlideTime;
        
        // Xác định hướng trượt theo hướng mặt hiện tại
        _slideDirection = _motor.IsFacingRight ? 1f : -1f;

        // Gọi Motor để thay đổi Collider và Animator
        _motor.StartSliding(_heightReductionRatio);

        // Phát âm thanh trượt
        _motor.PlaySound(_slideSound);

        // Bật hiệu ứng khói
        if (_slideParticles != null)
        {
            // Lưu hướng thế giới dựa trên hướng trượt lúc bắt đầu
            _particleWorldDirection = _slideDirection;
            _slideParticles.Play();
        }
    }

    private void HandleSliding()
    {
        // FIX: Nếu phát hiện đang bơi (IsSwimming = true), dừng trượt ngay lập tức.
        // Điều này ngăn việc trượt dưới đáy nước hoặc trượt khi nước dâng.
        // FIX: Thêm điều kiện IsClimbing. Nếu bắt đầu leo thang khi đang trượt (do giữ nút hoặc va chạm), phải hủy trượt ngay.
        // FIX: Nếu motor báo IsSliding = false (do bị Controller ngắt để thực hiện Jump), 
        // chúng ta phải dừng ngay để tránh việc gán linearVelocity đè lên lực nhảy.
        if (_motor.IsSwimming || _motor.IsClimbing || _motor.IsClinging || !_motor.IsSliding)
        {
            StopSlide();
            return;
        }

        // Xử lý hiệu ứng hạt: Giữ hướng khói cố định theo hướng trượt ban đầu
        if (_slideParticles != null)
        {
            // Chỉ phát hạt khi đang thực sự chạm đất
            var emission = _slideParticles.emission;
            if (emission.enabled != _motor.IsGrounded) emission.enabled = _motor.IsGrounded;
        }

        _slideTimer -= Time.fixedDeltaTime;

        // Xử lý di chuyển vật lý
        // Nếu đang ở dưới đất, tính toán vận tốc bám theo bề mặt dốc
        if (_motor.IsGrounded)
        {
            Vector2 directionVector = new Vector2(_slideDirection, 0);
            Vector2 slopeVelocity = _motor.GetSlopeVelocity(directionVector, _slideSpeed);
            _rb.linearVelocity = slopeVelocity;
        }
        else
        {
            // Nếu lỡ bị bay lên không (Air Slide), giữ vận tốc ngang và cho phép rơi tự do (Y)
            _rb.linearVelocity = new Vector2(_slideDirection * _slideSpeed, _rb.linearVelocity.y);
        }
        
        // Khóa di chuyển thông thường của Motor để không bị ghi đè vận tốc
        _motor.LockMovement(Time.fixedDeltaTime * 2); 

        // Kiểm tra điều kiện dừng trượt
        bool timeExpired = _slideTimer <= 0;
        bool keyReleased = !_input.SlideInput;
        
        // Nếu hết giờ HOẶC thả phím -> Cố gắng dừng
        if (timeExpired || keyReleased)
        {
            AttemptStopSlide();
        }
    }

    private void AttemptStopSlide()
    {
        // Kiểm tra xem có vật cản trên đầu không (Ceiling Check)
        // Nếu có trần, bắt buộc phải tiếp tục trượt cho đến khi ra khỏi vật cản
        if (_motor.CheckForCeiling())
        {
            // Reset timer một chút để tiếp tục trượt thêm frame tiếp theo
            _slideTimer = 0.1f; 
            return;
        }

        StopSlide();
    }

    private void StopSlide()
    {
        _isSliding = false;
        _cooldownTimer = _slideCooldown;

        // Khôi phục Collider và Animator
        _motor.StopSliding();

        // Tắt hiệu ứng khói
        if (_slideParticles != null) _slideParticles.Stop();

        // Nếu phím vẫn đang được giữ khi kết thúc trượt, chặn kích hoạt lại ngay lập tức
        if (_input.SlideInput)
        {
            _waitForSlideRelease = true;
        }
    }

    private void StartDive()
    {
        _isDiving = true;
        _motor.StartAirDive(_diveSpeed);
        _motor.PlaySound(_diveSound);
    }

    private void HandleDiving()
    {
        // Nếu chạm đất HOẶC chạm nước thì dừng Dive
        // FIX: Nếu bám tường (IsClinging) cũng phải dừng Dive ngay
        if (_motor.IsGrounded || _motor.IsSwimming || _motor.IsClinging)
        {
            // 1. Dừng vật lý lao xuống của Motor trước
            _motor.StopAirDive();
            _isDiving = false;

            // 2. Kiểm tra: Nếu vẫn giữ phím Slide
            // - Nếu chạm đất: Luôn cho phép Slide tiếp (Ground Slide)
            // - Nếu chạm nước: Chỉ Slide nếu người chơi đang bấm di chuyển ngang. Nếu không (đứng yên), ưu tiên lặn sâu (Plunge).
            // FIX: Không cho phép chuyển sang Slide nếu đang bám tường
            if (!_motor.IsClinging && _input.SlideInput && (_motor.IsGrounded || Mathf.Abs(_input.MoveInput.x) > 0.01f))
            {
                if (_motor.IsGrounded)
                {
                    // Thay vì trượt ngay, chuyển sang trạng thái chờ
                    _isWaitingToSlide = true;
                    _postDiveTimer = _postDiveSlideDelay;
                }
                else
                {
                    StartSlide(); // Dưới nước thì vẫn trượt ngay (hoặc bơi nhanh)
                }
            }
            else
            {
                // 3. Nếu không giữ phím -> Dừng hẳn và tính cooldown
                _cooldownTimer = _slideCooldown;
            }
        }
    }

    private void StopDive()
    {
        _isDiving = false;
        _motor.StopAirDive();
        _cooldownTimer = _slideCooldown; // Áp dụng cooldown chung
    }

    private void HandlePostDiveWait()
    {
        // Nếu người chơi nhả phím trong lúc đang chờ -> Hủy lệnh trượt
        if (!_input.SlideInput)
        {
            _isWaitingToSlide = false;
            _cooldownTimer = _slideCooldown;
            return;
        }

        _postDiveTimer -= Time.deltaTime;
        if (_postDiveTimer <= 0)
        {
            _isWaitingToSlide = false;
            StartSlide();
        }
    }
}
