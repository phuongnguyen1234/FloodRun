using UnityEngine;
using System.Collections;
using Core;

/// <summary>
/// Khả năng Zipline cho phép Player bám vào dây và trượt theo đường cong Bézier được định nghĩa bởi IZipline.
/// </summary>
public class ZiplineAbility : MonoBehaviour, IPlayerAbility
{
    [Header("Settings")]
    [SerializeField] private LayerMask _ziplineLayer;
    [SerializeField] private float _launcherLockTime = 0.3f; // Thời gian khóa di chuyển sau khi phóng
    
    [Header("Anchor Settings")]
    [Tooltip("Object con trên Player dùng để làm điểm treo dây. Nếu để trống sẽ dùng vị trí Player.")]
    [SerializeField] private Transform _ziplineAnchor;

    [Header("Effects")]
    [Tooltip("Hệ thống hạt (tia lửa) tại vị trí Anchor. Nên đặt làm con của ZiplineAnchor, Play On Awake = false, Loop = true.")]
    [SerializeField] private ParticleSystem _slideEffect;

    [Header("Audio")]
    [SerializeField] private AudioClip _ziplineConnectSound;
    [SerializeField] private AudioClip _ziplineSlideLoopSound;
    [SerializeField] private AudioClip _ziplineDetachSound;

    private PlayerMotor _motor;
    private PlayerInputHandler _input;
    private Rigidbody2D _rb;
    private Collider2D[] _colliders; // Chuyển thành mảng để quản lý cả Box và Circle

    private bool _isZiplining = false;
    private bool _isAbilityEnabled = true;
    private float _ziplineProgress; // Tiến trình trên dây (0 đến 1)
    private float _reattachCooldown; // Timer ngăn việc bám lại dây ngay sau khi rời ra
    private float _ziplineCurveLength; // Chiều dài ước tính của đường cong
    private IZipline _currentZipline;
    private AudioSource _slideAudioSource;

    private void Awake()
    {
        _motor = GetComponent<PlayerMotor>();
        _input = GetComponent<PlayerInputHandler>();
        _rb = GetComponent<Rigidbody2D>();
        _colliders = GetComponents<Collider2D>();
        
        if (_ziplineAnchor == null) _ziplineAnchor = transform;

        // Tạo AudioSource riêng cho âm thanh trượt (Loop) để không ảnh hưởng đến SFX khác
        _slideAudioSource = gameObject.AddComponent<AudioSource>();
        _slideAudioSource.playOnAwake = false;
        _slideAudioSource.loop = true;
        _slideAudioSource.spatialBlend = 0f; // 2D Sound

        // Gán clip ngay nếu có
        if (_ziplineSlideLoopSound != null) _slideAudioSource.clip = _ziplineSlideLoopSound;
    }

    public void EnableAbility() => _isAbilityEnabled = true;
    public void DisableAbility()
    {
        // Khi ability bị disable, đảm bảo âm thanh looping cũng dừng
        if (_slideAudioSource != null && _slideAudioSource.isPlaying)
        {
            _slideAudioSource.Stop();
        }
        _isAbilityEnabled = false;
        if (_isZiplining) DetachZipline(false); // Mặc định coi như không phải launcher khi bị disable đột ngột
    }

    private void OnEnable()
    {
        if (_motor != null) _motor.OnTeleported += HandleTeleport;

        // Đăng ký lắng nghe sự kiện thay đổi cài đặt để cập nhật âm lượng looping SFX
        if (SettingsManager.Instance != null) SettingsManager.Instance.OnSettingsApplied += UpdateLoopingSfxVolume;
        UpdateLoopingSfxVolume(); // Cập nhật ngay khi bật
    }

    private void OnDisable()
    {
        if (_motor != null) _motor.OnTeleported -= HandleTeleport;

        if (SettingsManager.Instance != null) SettingsManager.Instance.OnSettingsApplied -= UpdateLoopingSfxVolume;
    }

    private void HandleTeleport() => DetachZipline(false);

    private void Update()
    {
        if (!_isAbilityEnabled) return;

        if (_reattachCooldown > 0)
        {
            _reattachCooldown -= Time.deltaTime;
        }
    }

    private void FixedUpdate()
    {
        if (_isAbilityEnabled && _isZiplining)
        {
            HandleZiplineMovement();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Chỉ kích hoạt khi chạm vào Trigger của điểm Start (Trigger này phải nằm trong object con của Zipline)
        if (!_isZiplining && _isAbilityEnabled && _reattachCooldown <= 0f)
        {
            // Tìm IZipline từ object cha của collider va chạm (vì collider nằm ở object Start con)
            IZipline zipline = other.GetComponentInParent<IZipline>();
            
            // FIX: Kiểm tra chặt chẽ xem collider va chạm có đúng là của object StartPoint không
            // Bằng cách so sánh vị trí của object chứa collider (other.transform) với vị trí StartPoint của hệ thống
            // Nới lỏng khoảng cách lên 3.0f để chấp nhận offset giữa chân cột (Collider) và đầu dây (LineStart)
            if (zipline != null && 
                Vector3.Distance(other.transform.position, zipline.GetStartPoint()) < 3.0f)
            {
                AttachZipline(zipline);
            }
        }
    }

    private void AttachZipline(IZipline zipline)
    {
        _currentZipline = zipline;
        _isZiplining = true;
        _motor.SetZiplining(true);
        _ziplineProgress = 0f;
        _ziplineCurveLength = CalculateCurveLength(zipline);

        // 1. Tắt vật lý trọng lực
        _rb.gravityScale = 0f;
        _rb.linearVelocity = Vector2.zero;
        
        // FIX: Tắt WallJump để tránh vô tình bám tường khi đang trượt
        // (Map Designer có thể đặt Zipline xuyên qua khu vực WallJump)
        _motor.SetClinging(false);

        // 3. Chuyển Collider sang Trigger để xuyên qua địa hình (Ground/Wall) khi trượt
        if (_colliders != null)
        {
            foreach (var col in _colliders) col.isTrigger = true;
        }

        // --- XỬ LÝ SNAP VỊ TRÍ BAN ĐẦU ---
        // 1. Xác định hướng và góc xoay ngay tại điểm bắt đầu
        Vector3 p0 = zipline.GetStartPoint();
        Vector3 p1 = zipline.GetControlPoint1();
        Vector3 p2 = zipline.GetControlPoint2();
        Vector3 p3 = zipline.GetEndPoint();
        Vector3 startTangent = CalculateCubicBezierTangent(0, p0, p1, p2, p3);

        if (startTangent.x > 0.01f && !_motor.IsFacingRight) _motor.Flip();
        else if (startTangent.x < -0.01f && _motor.IsFacingRight) _motor.Flip();

        float startAngle = Mathf.Atan2(startTangent.y, startTangent.x) * Mathf.Rad2Deg;
        if (!_motor.IsFacingRight) startAngle += 180f;
        _rb.SetRotation(startAngle);

        // 2. Snap vị trí Player sao cho Anchor nằm đúng điểm bắt đầu (dùng offset động)
        transform.position = zipline.GetStartPoint() - GetCurrentAnchorWorldOffset(startAngle);

        // Bật hiệu ứng tia lửa
        if (_slideEffect != null)
        {           
            // FIX: Đồng bộ scale với Player để World Scale luôn là 1 (không bị âm)
            float counterFlip = transform.localScale.x < 0 ? -1f : 1f;
            _slideEffect.transform.localScale = new Vector3(counterFlip, 1f, 1f);

            // Sử dụng Play(true) để kích hoạt cả các Particle System con
            _slideEffect.Play(true);
        }

        // Audio: Connect (One-shot)
        _motor.PlaySound(_ziplineConnectSound);

        // Audio: Loop Slide (Chỉ start nếu chưa chạy để mượt mà khi chuyển dây)
        if (_ziplineSlideLoopSound != null && !_slideAudioSource.isPlaying)
        {
            _slideAudioSource.clip = _ziplineSlideLoopSound;
            // Cập nhật âm lượng từ SettingsManager
            UpdateLoopingSfxVolume();
            _slideAudioSource.Play();
        }
    }

    private void UpdateLoopingSfxVolume()
    {
        if (_slideAudioSource != null && SettingsManager.Instance != null)
            _slideAudioSource.volume = SettingsManager.Instance.SfxVolume;
    }

    /// <summary>
    /// Tính toán vector từ tâm Player đến Anchor trong không gian thế giới,
    /// có tính đến hướng nhìn (Flip) và góc xoay hiện tại.
    /// </summary>
    private Vector3 GetCurrentAnchorWorldOffset(float currentRotation)
    {
        if (_ziplineAnchor == null || _ziplineAnchor == transform) return Vector3.zero;

        Vector3 localPos = _ziplineAnchor.localPosition;
        // Nhân với localScale để xử lý việc lật (Flip) nhân vật
        Vector3 scaledOffset = new Vector3(localPos.x * transform.localScale.x, localPos.y * transform.localScale.y, 0);

        // Xoay offset theo góc của Rigidbody để khớp với độ nghiêng của dây
        return Quaternion.Euler(0, 0, currentRotation) * scaledOffset;
    }

    private float CalculateCurveLength(IZipline zipline)
    {
        float length = 0f;
        int resolution = 30; // Độ phân giải càng cao, tính toán càng chính xác
        Vector3 p0 = zipline.GetStartPoint();
        Vector3 p1 = zipline.GetControlPoint1();
        Vector3 p2 = zipline.GetControlPoint2();
        Vector3 p3 = zipline.GetEndPoint();

        Vector3 previousPoint = p0;
        for (int i = 1; i <= resolution; i++)
        {
            float t = (float)i / resolution;
            Vector3 currentPoint = CalculateCubicBezierPoint(t, p0, p1, p2, p3);
            length += Vector3.Distance(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
        // Tránh chia cho 0 nếu có lỗi
        return Mathf.Max(0.01f, length);
    }

    private Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // Công thức Bézier bậc 3: B(t) = (1-t)^3*P0 + 3(1-t)^2*t*P1 + 3(1-t)*t^2*P2 + t^3*P3
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;
        
        return (uuu * p0) + (3 * uu * t * p1) + (3 * u * tt * p2) + (ttt * p3);
    }

    private Vector3 CalculateCubicBezierTangent(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // Đạo hàm bậc 1 của Bézier bậc 3: 
        // B'(t) = 3(1-t)^2(P1-P0) + 6(1-t)t(P2-P1) + 3t^2(P3-P2)
        float u = 1 - t;
        float uu = u * u;
        float tt = t * t;

        Vector3 p01 = p1 - p0;
        Vector3 p12 = p2 - p1;
        Vector3 p23 = p3 - p2;

        return 3f * uu * p01 + 6f * u * t * p12 + 3f * tt * p23;
    }

    private void DetachZipline(bool isLauncher, float lockMovementTime = 0f)
    {
        if (!_isZiplining) return; // Ngăn việc gọi lại hàm khi đang xử lý

        // Đặt một cooldown nhỏ để ngăn việc bám lại ngay lập tức gây ra lỗi giật
        _reattachCooldown = 0.2f;

        // Tắt hiệu ứng tia lửa
        if (_slideEffect != null)
        {
            // Dùng StopEmitting để các hạt cũ biến mất tự nhiên theo thời gian sống (lifetime)
            _slideEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // Audio: Stop Loop & Play Detach
        if (_slideAudioSource.isPlaying) _slideAudioSource.Stop();
        _motor.PlaySound(_ziplineDetachSound);

        _isZiplining = false;
        _currentZipline = null;
        _motor.SetZiplining(false);

        // Nếu có thời gian khóa di chuyển, áp dụng ngay để bảo toàn quán tính phóng
        if (lockMovementTime > 0f) _motor.LockMovement(lockMovementTime);

        // Khôi phục trọng lực
        // FIX: Chỉ khôi phục trọng lực nếu KHÔNG bơi.
        // Nếu bị ngắt do nước dâng, ta muốn giữ gravity = 0 của trạng thái bơi.
        if (!_motor.IsSwimming)
            _rb.gravityScale = _motor.OriginalGravityScale;
        
        // Reset góc xoay về 0 (thẳng đứng) khi rời dây
        _rb.SetRotation(0f);

        // Bắt đầu coroutine để khôi phục va chạm vật lý một cách an toàn
        // Điều này ngăn player bị "bắn" lên trời do physics engine cố gắng giải quyết va chạm
        // ngay tại frame mà vận tốc phóng được áp dụng.
        StartCoroutine(DetachmentSequence(isLauncher));
    }

    private void HandleZiplineMovement()
    {
        if (_currentZipline == null)
        {
            DetachZipline(false); // An toàn nếu có lỗi, mặc định không phải launcher
            return;
        }

        // 1. Cập nhật tiến trình dựa trên tốc độ và chiều dài dây
        float speed = _currentZipline.GetSpeed();
        if (_ziplineCurveLength > 0.01f)
        {
            _ziplineProgress += (speed * Time.fixedDeltaTime) / _ziplineCurveLength;
        }

        // 2. Tính toán vị trí mới của Anchor trên đường cong
        Vector3 p0 = _currentZipline.GetStartPoint();
        Vector3 p1 = _currentZipline.GetControlPoint1();
        Vector3 p2 = _currentZipline.GetControlPoint2();
        Vector3 p3 = _currentZipline.GetEndPoint();
        Vector3 anchorTargetPosition = CalculateCubicBezierPoint(Mathf.Clamp01(_ziplineProgress), p0, p1, p2, p3);

        // --- CẢI TIẾN: Xoay người và chỉnh hướng ---
        // Tính vector hướng (tiếp tuyến)
        Vector3 tangent = CalculateCubicBezierTangent(Mathf.Clamp01(_ziplineProgress), p0, p1, p2, p3);

        // 1. Luôn quay mặt về phía di chuyển
        if (tangent.x > 0.01f && !_motor.IsFacingRight) _motor.Flip(); // Chỉ flip nếu có chuyển động đáng kể
        else if (tangent.x < -0.01f && _motor.IsFacingRight) _motor.Flip();

        // 2. Nghiêng người theo dây (Vector pháp tuyến/tiếp tuyến)
        // Sử dụng Atan2 để lấy góc của dây. 
        float rotationAngle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

        // Nếu nhân vật đang nhìn sang trái, chúng ta cần điều chỉnh góc xoay 
        // để tư thế treo người không bị ngược.
        if (!_motor.IsFacingRight) rotationAngle += 180f;
        
        _rb.SetRotation(rotationAngle);

        // FIX: Liên tục ép Scale của Particle về dương vì Motor.Flip có thể chạy trong lúc đang trượt
        if (_slideEffect != null)
        {
            float counterFlip = transform.localScale.x < 0 ? -1f : 1f;
            _slideEffect.transform.localScale = new Vector3(counterFlip, 1f, 1f);
        }

        // 3. Cập nhật vị trí: Tính toán lại Offset động để Anchor luôn bám sát dây
        // bất kể nhân vật đang Flip hay đang xoay theo góc nào.
        Vector3 playerTargetPosition = anchorTargetPosition - GetCurrentAnchorWorldOffset(rotationAngle);
        _rb.MovePosition(playerTargetPosition);

        // Khóa Movement của Motor để không bị xung đột input trái/phải
        _motor.LockMovement(Time.fixedDeltaTime * 2f);

        // 4. Kiểm tra nếu đã đi hết dây
        if (_ziplineProgress >= 1f)
        {
            IZipline nextZipline = _currentZipline.NextZipline;
            if (nextZipline != null)
            {
                // Nếu có zipline nối tiếp, bám vào nó ngay lập tức
                AttachZipline(nextZipline);
            }
            else
            {
                // Nếu là zipline cuối cùng, rời ra
                // FIX: Sử dụng chính vector tangent hiện tại để hướng phóng đồng bộ với hướng nhân vật đang nhìn/di chuyển
                // Thay vì lấy từ ZiplineController (vốn tính toán dựa trên P2 - P1 cứng nhắc)
                Vector3 direction = tangent.normalized;
                float exitSpeed = _currentZipline.GetSpeed();
                bool isLauncher = _currentZipline.IsLauncher;

                // Truyền thời gian lock movement nếu là launcher
                DetachZipline(isLauncher, isLauncher ? _launcherLockTime : 0f);

                // Nếu là Launcher: Giữ nguyên vận tốc của dây để phóng đi
                // Nếu là dây thường: Chỉ đẩy nhẹ (50% tốc độ) để không bị rơi thẳng xuống
                if (isLauncher)
                {
                    _rb.linearVelocity = direction * exitSpeed;

                    // THÊM: Thông báo cú nhảy để Animator thoát trạng thái Idle/Run lập tức
                    _motor.NotifyJumpTriggered();
                }
                // Nếu là dây thường, không làm gì cả, để player rơi tự do sau khi DetachZipline() khôi phục trọng lực.
            }
        }
    }

    private IEnumerator DetachmentSequence(bool isLauncher)
    {
        // Đợi một frame vật lý để vận tốc phóng được áp dụng và player di chuyển ra khỏi vị trí cũ.
        yield return new WaitForFixedUpdate();

        // Bật lại collider cho cả Launcher và Zipline thường.
        // Physics Engine sẽ tự xử lý va chạm và đẩy Player ra nếu bị kẹt.
        if (_colliders != null)
        {
            foreach (var col in _colliders) col.isTrigger = false;
        }
    }
}