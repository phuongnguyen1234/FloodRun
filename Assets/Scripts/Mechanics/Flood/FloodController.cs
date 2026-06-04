using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Core.Data;
using Unity.Netcode;
using Core.Interfaces;
using System.Linq;
using UnityEngine.Events;
using DG.Tweening;

namespace Mechanics
{
    /// <summary>
/// FloodController là một MonoBehaviour quản lý quá trình dâng nước trong game, bao gồm:
/// - Di chuyển flood qua nhiều stage với các thiết lập khác nhau (vị trí, tốc độ, âm thanh, loại flood...)
/// - Cho phép thay đổi loại flood (Water, Acid, Lava...) trong runtime với hiệu ứng chuyển đổi mượt mà (fade out -> change type -> fade in).
/// - Hỗ trợ tạm dừng, điều chỉnh vị trí flood, và áp dụng hệ số nhân dựa trên độ sâu của Player.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer), typeof(AudioSource))]
public class FloodController : MonoBehaviour, IFloodZone, IFloodManager
{
    private IGameLoopManager _gameLoopManager;

    [System.Serializable]
    public class FloodStage
    {
        [Header("Stage Settings")]
        public string StageName = "Stage";
        [Tooltip("Thời gian chờ trước khi bắt đầu stage này")]
        public float StartDelay = 0f;

        [Tooltip("Nếu bật, Stage này sẽ bỏ qua việc di chuyển, chỉ dùng để đợi hoặc đổi loại Flood.")]
        public bool NoMovement = false;
        
        [Header("Movement")]
        [Tooltip("Vị trí đích đến trong Local Space (tương đối với object cha)")]
        public Vector3 TargetLocalPosition;
        [Tooltip("Tốc độ di chuyển đến đích")]
        public float MoveSpeed = 1.0f;

        [Header("Audio")]
        [Tooltip("Âm thanh phát lặp lại trong khi flood đang di chuyển ở Stage này.")]
        public AudioClip MovementSound;
        [Tooltip("Nếu true, flood sẽ nhảy ngay lập tức đến đích thay vì di chuyển")]
        public bool InstantMove = false;

        [Header("Modification")]
        [Tooltip("Nếu gán, Flood sẽ đổi sang loại này khi bắt đầu Stage (VD: Water -> Acid)")]
        public FloodTypeData ChangeTypeTo;

        [Tooltip("Nếu gán, Flood sẽ đổi sang loại này giữa lúc di chuyển. 'Change Type At' xác định thời điểm.")]
        public FloodTypeData MidMoveChangeType;
        [Range(0, 1)]
        [Tooltip("Thời điểm đổi sang 'Mid Move Change Type' (0=bắt đầu, 0.5=nửa đường, 1=kết thúc)")]
        public float ChangeTypeAt = 0.5f;

        [Header("Events")]
        public UnityEvent OnStageStart;
        public UnityEvent OnStageComplete;
    }

    [Header("Flood Settings")]
    [Tooltip("Kéo ScriptableObject FloodTypeData vào đây để định nghĩa thuộc tính cho loại flood này.")]
    [SerializeField] private FloodTypeData _floodProfile;

    [Header("Map-Specific Override")]
    [Tooltip("Bật cờ này để tùy chỉnh các thuộc tính của Flood ngay trong Map Prefab, ghi đè lên FloodTypeData ở trên.")]
    [SerializeField] private bool _overrideProfile = false;

    // Các trường này chỉ có tác dụng khi _overrideProfile = true
    [Header("Override Properties (Chỉ hoạt động khi Override Profile được bật)")]
    [SerializeField] private FloodType _overrideType = FloodType.Water;
    [SerializeField] private Sprite _overrideFloodSprite;
    [SerializeField] private AudioClip _overrideSplashSound;
    [SerializeField] private float _overrideAirDrainRate = 8f;
    [SerializeField] private bool _overrideNoSwim = false;
    [SerializeField] private bool _overrideApplyDepthMultiplier = false;
    [SerializeField] private float _overrideDepthMultiplierFactor = 0.5f;
    [SerializeField] private bool _overrideFadeAlphaOnSwim = true;
    [Range(0, 1)]
    [SerializeField] private float _overrideTargetSwimAlpha = 0.5f;
    [SerializeField] private float _overrideAlphaTransitionSpeed = 2f;

    [Tooltip("Nếu true, nước sẽ tự động dâng khi object được khởi tạo. Nếu false, cần gọi hàm StartFlood() từ bên ngoài.")]
    [SerializeField] private bool _autoStart = false; // Mặc định false để MapManager kiểm soát, hoặc dùng event

    [Tooltip("Nếu true, Flood sẽ giữ nguyên trục Z (độ sâu) hiện tại khi di chuyển, bất kể TargetWorldPosition có Z là bao nhiêu. Rất hữu ích cho game 2D.")]
    [SerializeField] private bool _lockLocalZAxis = true;

    [Header("Type Change Transitions")]
    [Tooltip("Thời gian để transition giữa 2 loại flood (fade out -> change -> fade in).")]
    [SerializeField] private float _typeChangeDuration = 0.4f;
    [Tooltip("Âm thanh phát ra khi biến đổi loại flood (ví dụ: tiếng xèo xèo khi nước hóa acid).")]
    [SerializeField] private AudioClip _typeChangeGlobalSound;

    [Header("Sequence")]
    [SerializeField] private List<FloodStage> _stages = new List<FloodStage>();

    [Header("Global Events")]
    public UnityEvent OnFloodSequenceComplete;

    // Public properties để các script khác truy cập vào thông tin của Flood
    public FloodType Type => _overrideProfile ? _overrideType : (_floodProfile != null ? _floodProfile.Type : FloodType.Water);
    public AudioClip SplashSound => _overrideProfile ? _overrideSplashSound : (_floodProfile != null ? _floodProfile.SplashSound : null);
    public float AirDrainRate => _overrideProfile ? _overrideAirDrainRate : (_floodProfile != null ? _floodProfile.AirDrainRate : 8f);
    public bool NoSwim => _overrideProfile ? _overrideNoSwim : (_floodProfile != null && _floodProfile.NoSwim);
    public bool ApplyDepthMultiplier => _overrideProfile ? _overrideApplyDepthMultiplier : (_floodProfile != null && _floodProfile.ApplyDepthMultiplier);
    
    // Thuộc tính Alpha lấy từ Profile hoặc Override
    private bool FadeAlphaOnSwim => _overrideProfile ? _overrideFadeAlphaOnSwim : (_floodProfile != null && _floodProfile.FadeAlphaOnSwim);
    private float TargetSwimAlpha => _overrideProfile ? _overrideTargetSwimAlpha : (_floodProfile != null ? _floodProfile.TargetSwimAlpha : 0.5f);
    private float AlphaTransitionSpeed => _overrideProfile ? _overrideAlphaTransitionSpeed : (_floodProfile != null ? _floodProfile.AlphaTransitionSpeed : 0.1f);
    private float DepthMultiplierFactor => _overrideProfile ? _overrideDepthMultiplierFactor : (_floodProfile != null ? _floodProfile.DepthMultiplierFactor : 0.5f);

    private bool _isRunning = false;
    private BoxCollider2D _collider; // Dùng để tính toán bề mặt nước cho Depth
    private Sequence _floodSequence;
    private AudioSource _audioSource;
    private SpriteRenderer _spriteRenderer;
    private float _defaultAlpha = 1f;
    private bool _isChangingType = false; // Cờ ngăn Update can thiệp khi đang đổi loại
    private Tween _fadeTween; // Lưu lại tween để quản lý
    private int _currentStageIndex = -1; // Theo dõi stage đang thực hiện
    private Vector3 _initialLocalPosition; // Lưu vị trí gốc để tính toán timeline chuẩn xác

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _collider = GetComponent<BoxCollider2D>();
        _audioSource = GetComponent<AudioSource>();
        _defaultAlpha = _spriteRenderer.color.a;
        _initialLocalPosition = transform.localPosition;

        // Tìm IGameLoopManager trong Scene (có thể là GameplayManager hoặc MultiplayerManager)
        // Việc này giải quyết vấn đề "không thấy MapManager.cs" giữa các Assembly.
        if (_gameLoopManager == null)
        {
            // Tìm kiếm đối tượng thực thi IGameLoopManager
            _gameLoopManager = FindObjectsByType<Component>().OfType<IGameLoopManager>().FirstOrDefault();
        }

        UpdateVisuals();
    }

    private void OnEnable()
    {
        // Đăng ký đồng bộ hóa khi file Asset thay đổi
        FloodTypeData.OnDataChanged += UpdateVisuals;

        if (Core.SettingsManager.Instance != null)
            Core.SettingsManager.Instance.OnSettingsApplied += UpdateAudioVolume;
    }

    private void OnDisable()
    {
        FloodTypeData.OnDataChanged -= UpdateVisuals;

        if (Core.SettingsManager.Instance != null)
            Core.SettingsManager.Instance.OnSettingsApplied -= UpdateAudioVolume;
    }

    private void OnValidate()
    {
        // Tự động cập nhật visuals khi thay đổi bất kỳ biến nào trong Inspector (bao gồm cả kéo thả Profile)
        UpdateVisuals();
    }

    private void Update()
    {
        HandleDynamicAlpha();
    }

    private void HandleDynamicAlpha()
    {
        if (!FadeAlphaOnSwim || _spriteRenderer == null || _isChangingType) return;

        bool isPlayerSwimmingHere = false;

        // Truy vấn Player thông qua IGameLoopManager (Generic, works for SP & MP)
        if (_gameLoopManager != null && _gameLoopManager.LocalPlayer is IPlayer player)
        {
            // Sử dụng IsSubmerged để giữ Alpha thấp ngay cả khi đang Zipline/Climb/Cling trong nước
            if (player.IsSubmerged && player.CurrentFlood == (IFloodZone)this)
                isPlayerSwimmingHere = true;
        }

        float targetAlpha = isPlayerSwimmingHere ? TargetSwimAlpha : _defaultAlpha;
        Color c = _spriteRenderer.color;
        _spriteRenderer.color = new Color(c.r, c.g, c.b, Mathf.MoveTowards(c.a, targetAlpha, AlphaTransitionSpeed * Time.deltaTime));
    }

    private void UpdateAudioVolume()
    {
        if (_audioSource != null)
        {
            // Nếu không có SettingsManager (trong Editor), mặc định là 1 để dễ test
            _audioSource.volume = (Core.SettingsManager.Instance != null) ? Core.SettingsManager.Instance.SfxVolume : 1f;
        }
    }

    private void Start()
    {
        UpdateAudioVolume(); // Đảm bảo âm lượng được cập nhật từ SettingsManager khi bắt đầu

        if (_autoStart)
        {
            StartFlood();
        }
    }

    /// <summary>
    /// Hàm public để MapManager gọi khi bắt đầu game
    /// </summary>
    public void StartFlood()
    {
        {
            _isRunning = true;
            BuildAndStartSequence();
        }
    }

    /// <summary>
    /// Hàm bổ sung để MapManager gọi khi cần đồng bộ cho người chơi Join muộn.
    /// </summary>
    public void SyncToMapTime()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            SyncFloodToNetworkTime();
        }
    }

    /// <summary>
    /// Dừng quá trình dâng nước ngay lập tức.
    /// </summary>
    public void StopFlood()
    {
        _floodSequence?.Kill();
        _floodSequence = null;
        _isRunning = false;
    }

    /// <summary>
    /// Tạm dừng quá trình dâng nước trong một khoảng thời gian nhất định.
    /// </summary>
    public void PauseFlood(float duration)
    {
        _floodSequence?.Pause();
        DOVirtual.DelayedCall(duration, () => _floodSequence?.Play());
    }

    /// <summary>
    /// Đồng bộ vị trí của Flood dựa trên thời gian thực tế đã trôi qua của trận đấu.
    /// Rất quan trọng cho Spectators hoặc người chơi Join muộn.
    /// </summary>
    private void SyncFloodToNetworkTime()
    {
        if (_floodSequence == null) return;
        
        // Lấy IMapManager từ cha để biết thời điểm map bắt đầu chạy thực tế
        IMapManager mapManager = GetComponentInParent<IMapManager>();
        if (mapManager == null) return;

        double elapsedTime = NetworkManager.Singleton.ServerTime.Time - mapManager.GetMapStartTime();
        if (elapsedTime > 0.5f) // Chỉ sync (Goto) nếu thực sự tham gia muộn hơn 0.5s để tránh lỗi khởi tạo DOTween
        {
            _isRunning = true;
            // Nhảy đến thời điểm hiện tại và thực thi tất cả các callback/event ở giữa
            _floodSequence.Goto((float)elapsedTime, true);
        }
    }

    /// <summary>
    /// Điều chỉnh vị trí Flood dọc theo quỹ đạo của Stage hiện tại hoặc sắp tới.
    /// </summary>
    /// <param name="offset">Khoảng cách điều chỉnh (Dương = Tiến tới, Âm = Lùi lại)</param>
    public void AdjustFloodPosition(float offset)
    {
        // Với DOTween Sequence, việc điều chỉnh vị trí chỉ đơn giản là dịch chuyển transform
        // Sequence sẽ tự động tiếp tục từ vị trí mới.
        Vector3 moveDir = GetActiveDirection();
        transform.DOMove(transform.position + (moveDir * offset), 0.5f).SetEase(Ease.OutQuad);

        string actionType = offset > 0 ? "Advanced" : "Retreated";
        Debug.Log($"[Flood] {actionType} by {Mathf.Abs(offset)} units along current trajectory.");
    }

    /// <summary>
    /// Xác định hướng di chuyển "có ý định" của Flood dựa trên Stage hiện tại hoặc tiếp theo.
    /// </summary>
    private Vector3 GetActiveDirection()
    {
        // Trường hợp 1: Đang chạy một Stage cụ thể
        if (_isRunning && _currentStageIndex >= 0 && _currentStageIndex < _stages.Count)
        {
            Vector3 target = _stages[_currentStageIndex].TargetLocalPosition;
            
            // Tính hướng trên 2D để tránh sai số Z làm lệch Vector.normalized
            Vector2 dir2D = (new Vector2(target.x, target.y) - new Vector2(transform.localPosition.x, transform.localPosition.y)).normalized;
            if (dir2D != Vector2.zero) return new Vector3(dir2D.x, dir2D.y, 0);
        }
        
        // Trường hợp 2: Game chưa bắt đầu hoặc đang trong StartDelay, nhìn vào Stage đầu tiên
        if (_stages.Count > 0)
        {
            Vector3 target = _stages[0].TargetLocalPosition;
            
            Vector2 dir2D = (new Vector2(target.x, target.y) - new Vector2(transform.localPosition.x, transform.localPosition.y)).normalized;
            if (dir2D != Vector2.zero) return new Vector3(dir2D.x, dir2D.y, 0);
        }

        return Vector3.up; // Fallback cuối cùng
    }
    /// <summary>
    /// Thực thi IFloodManager.ChangeFloodType.
    /// Cho phép các assembly bên ngoài thay đổi loại flood thông qua interface,
    /// sử dụng Core.Data.BaseFloodTypeData làm tham chiếu chéo assembly.
    /// </summary>
    public void ChangeFloodType(BaseFloodTypeData newType)
    {
        if (newType is FloodTypeData concreteType)
        {
            ExecuteTypeChange(concreteType);
        }
        else if (newType != null)
        {
            Debug.LogWarning($"Cố gắng thay đổi loại flood với một kiểu không tương thích: {newType.GetType()}", this);
        }
    }

    /// <summary>
    /// Hàm này có thể được gọi từ UnityEvent (ví dụ: ButtonController) để thay đổi loại Flood runtime.
    /// </summary>
    /// <param name="newData">ScriptableObject chứa data mới (Water, Acid, Lava...)</param>
    private void ExecuteTypeChange(FloodTypeData newData)
    {
        if (newData != null)
        {
            if (_typeChangeGlobalSound != null && _audioSource != null)
            {
                // Cập nhật volume của Source trước khi phát
                UpdateAudioVolume();
                // Dùng scale là 1f vì _audioSource.volume đã mang giá trị SfxVolume rồi
                // Điều này tránh việc bị nhân đôi âm lượng (SfxVolume * SfxVolume)
                _audioSource.PlayOneShot(_typeChangeGlobalSound, 1f);
            }

            _isChangingType = true;

            // Hủy tween cũ nếu đang chạy để tránh xung đột
            _fadeTween?.Kill();

            // Sử dụng DOTween để transition mượt mà
            // 1. Fade Out hiện tại
            _fadeTween = _spriteRenderer.DOFade(0, _typeChangeDuration / 2).SetUpdate(true).OnComplete(() =>
            {
                // 2. Thay đổi dữ liệu
                _overrideProfile = false;
                _floodProfile = newData;
                UpdateVisuals();

                // 3. Fade In lại
                _spriteRenderer.DOFade(_defaultAlpha, _typeChangeDuration / 2).SetUpdate(true).OnComplete(() => 
                {
                    _isChangingType = false;
                });
            });
        }
    }

    private void UpdateVisuals()
    {
        if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderer == null) return;

        // 1. Cập nhật Sprite
        Sprite spriteToApply = _overrideProfile ? _overrideFloodSprite : (_floodProfile != null ? _floodProfile.FloodSprite : null);
        if (spriteToApply != null) _spriteRenderer.sprite = spriteToApply;

        // 2. Cập nhật Color (Đồng bộ màu từ FloodColor trong Profile)
        Color targetColor = _overrideProfile ? _spriteRenderer.color : (_floodProfile != null ? _floodProfile.FloodColor : _spriteRenderer.color);
        
        // Cập nhật lại _defaultAlpha từ Profile mới để các logic khác (như HandleDynamicAlpha) dùng đúng mốc
        _defaultAlpha = targetColor.a;

        // Nếu đang trong quá trình chuyển đổi (Fade), ta giữ Alpha hiện tại của Renderer để không làm hỏng Tween.
        // Nếu không (ví dụ trong Editor hoặc lúc Game đang chạy bình thường), ta áp dụng Alpha từ Profile.
        if (_isChangingType && Application.isPlaying)
        {
            targetColor.a = _spriteRenderer.color.a;
        }

        _spriteRenderer.color = targetColor;
    }

    private void BuildAndStartSequence()
    {
        _floodSequence = DOTween.Sequence();
        
        // FIX: Luôn bắt đầu tính toán từ vị trí Local mặc định của Prefab
        // Điều này đảm bảo Timeline là cố định, không đổi dù Transform hiện tại đang ở đâu.
        Vector3 lastStageTarget = _initialLocalPosition; 

        bool isServer = NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;

        foreach (var stage in _stages)        {
            // 1. Delay
            _floodSequence.AppendInterval(stage.StartDelay);

            // 2. Start Event & Type Change
            _floodSequence.AppendCallback(() => {
                stage.OnStageStart?.Invoke();
                if (stage.ChangeTypeTo != null) ChangeFloodType(stage.ChangeTypeTo);
                
                if (stage.MovementSound != null && _audioSource != null) {
                    _audioSource.clip = stage.MovementSound;
                    _audioSource.loop = true;
                    _audioSource.Play();
                }
            });

            // 3. Movement
            if (!stage.NoMovement)
            {
                Vector3 target = stage.TargetLocalPosition;
                if (_lockLocalZAxis) target.z = transform.localPosition.z;

                // FIX: Tính duration dựa trên lastStageTarget (Điểm mốc trước đó)
                // thay vì Vector3.Distance(transform.localPosition, target)
                float duration = stage.InstantMove ? 0 : Vector3.Distance(lastStageTarget, target) / Mathf.Max(0.1f, stage.MoveSpeed);
                
                var moveTween = transform.DOLocalMove(target, duration).SetEase(Ease.Linear);
                
                // Xử lý đổi type giữa chừng (MidMove)
                if (stage.MidMoveChangeType != null)
                {
                    _floodSequence.Append(moveTween.OnUpdate(() => {
                        // Logic đơn giản hóa: DOTween Sequence Append đã quản lý thời gian, 
                        // MidMove nên dùng InsertCallback dựa trên duration của tween này.
                    }));
                    _floodSequence.InsertCallback(_floodSequence.Duration() - (duration * (1 - stage.ChangeTypeAt)), 
                        () => ChangeFloodType(stage.MidMoveChangeType));
                }
                else
                {
                    _floodSequence.Append(moveTween);
                }

                lastStageTarget = target;
            }

            // 4. Complete Event
            _floodSequence.AppendCallback(() => {
                if (_audioSource != null && _audioSource.clip == stage.MovementSound) _audioSource.Stop();
                stage.OnStageComplete?.Invoke();
            });
        }

        _floodSequence.OnComplete(() => {
            _isRunning = false;
            OnFloodSequenceComplete?.Invoke();
        });
    }

    // Vẽ Gizmos để visualize đường đi của Flood trong Editor
    private void OnDrawGizmosSelected()
    {
        if (_stages == null || _stages.Count == 0) return;

        Gizmos.color = Color.cyan;
        // Bắt đầu vẽ từ vị trí thế giới của object
        Vector3 currentWorldPos = transform.position;

        foreach (var stage in _stages)
        {
            // Chuyển đổi tọa độ local của Stage sang tọa độ thế giới để Gizmos vẽ đúng chỗ
            Vector3 targetWorldPos = transform.parent != null 
                ? transform.parent.TransformPoint(stage.TargetLocalPosition) 
                : stage.TargetLocalPosition;

            Gizmos.DrawLine(currentWorldPos, targetWorldPos);
            Gizmos.DrawWireSphere(targetWorldPos, 0.3f);
            currentWorldPos = targetWorldPos;
        }
    }
    
    // Helper function để set vị trí đích nhanh trong Editor (Context Menu)
    [ContextMenu("Set Last Stage Target to Current Pos")]
    private void SetLastTargetToCurrent()
    {
        if (_stages.Count > 0)
        {
            _stages[_stages.Count - 1].TargetLocalPosition = transform.localPosition;
        }
    }

    /// <summary>
    /// Tính toán hệ số nhân dựa trên độ sâu của Player so với bề mặt Flood.
    /// </summary>
    public float GetDepthMultiplier(float playerY)
    {
        if (!ApplyDepthMultiplier || _collider == null || _spriteRenderer == null)
            return 1.0f;

        // Tính mặt nước (Top Y)
        float surfaceY = _collider.bounds.max.y;
        
        // Độ sâu (càng xuống dưới càng dương)
        float depth = Mathf.Max(0f, surfaceY - playerY);
        
        // Công thức: 1 + (Depth * Factor)
        return 1.0f + (depth * DepthMultiplierFactor);
    }
}

}
