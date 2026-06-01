using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using Core.Interfaces;
using Core;
using System.Collections.Generic;
using Core.Events;
using System.Linq;
using Unity.Netcode;

/// <summary>
/// ButtonController là script chính để điều khiển hành vi của nút bấm trong game.
/// Nó quản lý trạng thái của nút, cập nhật hình ảnh và marker tương ứng,
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(AudioSource))]
public class ButtonController : MonoBehaviour, IInteractable, IButtonController
{
    public enum ButtonState
    {
        // Trạng thái nút không hoạt động (chưa đến lượt)
        Inactive,
        Normal,
        Activated,
        Warning // Trạng thái đếm ngược nổ
    }

    [Header("Settings")]
    [SerializeField] private ButtonState _initialState = ButtonState.Normal;
    
    [Header("Main Button Visuals")]
    [Tooltip("SpriteRenderer của object con Button_Core. Đây là nơi màu sắc và sprite chính của nút được áp dụng.")]
    [SerializeField] private SpriteRenderer _buttonCoreSpriteRenderer;
    [Header("New Marker System")]
    [SerializeField] private GameObject _markerRoot;
    [SerializeField] private SpriteRenderer _markerRing;
    [SerializeField] private SpriteRenderer _markerArrow;
    [Tooltip("Animator nằm trên Marker để điều khiển quay Ring và di chuyển Arrow")]
    [SerializeField] private Animator _markerAnimator;

    [Header("Explosion Settings")]
    [SerializeField] private bool _isExplosive = false;
    [SerializeField] private float _explosionDelay = 3f;
    [SerializeField] private float _explosionRadius = 3f;
    [Tooltip("Layer của Player để kiểm tra sát thương")]
    [SerializeField] private LayerMask _targetLayer;
    [Tooltip("Particle System hiệu ứng nổ. Sẽ được Instantiate tại vị trí nút.")]
    [SerializeField] private GameObject _explosionEffectPrefab;
    [Tooltip("Object hiển thị vùng nổ (vùng tròn), sẽ được bật lên khi đếm ngược")]
    [SerializeField] private GameObject _warningAreaObject;

    [Header("Audio")]
    [SerializeField] private AudioClip _activationSound;
    [SerializeField] private AudioClip _warningSound;
    [SerializeField] private AudioClip _timeUpSound; // Audio báo hiệu hết giờ (Ting)
    [Tooltip("Thời gian phát 'Time Up Sound' trước khi nổ (giây). Phải nhỏ hơn Explosion Delay.")]
    [SerializeField] private float _timeUpLeadTime = 0.5f;
    [SerializeField] private AudioClip _explosionSound;

    [Header("Events")]
    [Space]
    [Tooltip("Sự kiện nảy ra khi nút được kích hoạt. Các script khác có thể lắng nghe tại đây.")]
    public UnityEvent OnButtonActivated;

    [Header("Map Actions")]
    [Tooltip("Danh sách các hành động sẽ thực thi khi nút được kích hoạt (thay thế hoặc bổ sung cho UnityEvent).")]
    [SerializeReference]
    private List<MapAction> _actions = new List<MapAction>();

    private AudioSource _audioSource;
    private ButtonState _currentState;
    private float _activationPitch = 1.0f;

    // Cache màu sắc từ Hex
    private Color _colorGreen;
    private Color _colorYellow;
    private Color _colorRed;
    private Color _colorDark; // Màu xám đậm cho trạng thái Activated

    private IMapManager _cachedMapManager;

    // Thực thi interface IInteractable
    public bool CanInteract => _currentState == ButtonState.Normal;

    // Cho phép hệ thống Locator kiểm tra loại nút
    public bool IsExplosive => _isExplosive;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        InitializeColors();

        // Đảm bảo Collider của nút là Trigger để Player đi qua được (nếu muốn)
        // Nếu bạn muốn Player đứng lên nút thì bỏ dòng này và dùng OnCollisionEnter2D
        GetComponent<BoxCollider2D>().isTrigger = true; 

        if (OnButtonActivated == null)
            OnButtonActivated = new UnityEvent();
        
        if (_warningAreaObject != null)
        {
            _warningAreaObject.SetActive(false);
        }
    }

    private void Start()
    {
        // Thử cache sớm, nếu null cũng không sao, hàm GetMapManager sẽ lo liệu sau
        _cachedMapManager = GetMapManager();
    }

    private void OnValidate()
    {
        // Hàm này chạy mỗi khi bạn thay đổi giá trị trong Inspector
        InitializeColors();
        SyncVisuals(_initialState);
    }

    private void InitializeColors()
    {
        ColorUtility.TryParseHtmlString("#45f145", out _colorGreen);
        ColorUtility.TryParseHtmlString("#ffff25", out _colorYellow);
        ColorUtility.TryParseHtmlString("#fd0000", out _colorRed);
        ColorUtility.TryParseHtmlString("#1a1a1a", out _colorDark);
    }

    /// <summary>
    /// Hàm thay đổi trạng thái của nút và cập nhật hình ảnh/marker tương ứng
    /// </summary>
    public void SetState(ButtonState newState)
    {
        _currentState = newState;
        SyncVisuals(_currentState);
    }

    private void SyncVisuals(ButtonState state)
    {
        // 1. Cập nhật màu sắc của Button_Core
        if (_buttonCoreSpriteRenderer == null) return;

        switch (state)
        {
            case ButtonState.Inactive:
                _buttonCoreSpriteRenderer.color = _colorYellow;
                break;
            case ButtonState.Normal:
                _buttonCoreSpriteRenderer.color = _colorGreen;
                break;
            case ButtonState.Activated:
                _buttonCoreSpriteRenderer.color = _colorDark;
                break;
            case ButtonState.Warning:
                _buttonCoreSpriteRenderer.color = _colorRed;
                break;
        }
        
        // 2. Cập nhật logic Marker mới
        UpdateMarkerVisuals(state);

        // 3. Cập nhật Warning Area (Vùng tròn đỏ dưới chân)
        if (_warningAreaObject != null)
        {
            _warningAreaObject.SetActive(state == ButtonState.Warning);
        }
    }

    private void UpdateMarkerVisuals(ButtonState state)
    {
        if (_markerRoot == null) return;

        switch (state)
        {
            case ButtonState.Inactive:
                _markerRoot.SetActive(true);
                if (_markerRing != null) _markerRing.color = _colorYellow;
                if (_markerArrow != null) _markerArrow.gameObject.SetActive(false); // Inactive: Không có arrow
                if (_markerAnimator != null) _markerAnimator.enabled = false;      // Không animation
                break;

            case ButtonState.Normal:
                _markerRoot.SetActive(true);
                if (_markerArrow != null) _markerArrow.gameObject.SetActive(true); // Reset arrow về active
                if (_markerAnimator != null) _markerAnimator.enabled = true;      // Luôn có animation khi sẵn sàng

                if (_isExplosive) // Rule cho nút exploding khi chưa ấn
                {
                    if (_markerRing != null) _markerRing.color = _colorRed;   // Ring đỏ
                    if (_markerArrow != null) _markerArrow.color = _colorGreen; // Arrow xanh
                }
                else // Nút thường
                {
                    if (_markerRing != null) _markerRing.color = _colorGreen;
                    if (_markerArrow != null) _markerArrow.color = _colorGreen;
                }
                break;

            case ButtonState.Warning:
                _markerRoot.SetActive(false); // Exploding sau khi ấn: ẩn hết marker
                break;

            case ButtonState.Activated:
                _markerRoot.SetActive(false); // Ẩn hoàn toàn
                break;
        }
    }

    public void SetActivationPitch(float pitch)
    {
        _activationPitch = pitch;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!CanInteract) return;

        // Chỉ cần object có IPlayerAbility là ta coi như đó là Player hoặc vật thể hợp lệ
        if (other.GetComponentInParent<IPlayerAbility>() != null)
        {
            Interact();
        }
    }

    /// <summary>
    /// Tìm kiếm MapManager thông qua Interface để tránh phụ thuộc trực tiếp vào class MapManager.
    /// Caching lại để tối ưu hiệu suất cho các lần gọi sau.
    /// </summary>
    private IMapManager GetMapManager()
    {
        if (_cachedMapManager == null)
        {
            _cachedMapManager = FindObjectsByType<MonoBehaviour>()
                .OfType<IMapManager>()
                .FirstOrDefault();
        }
        return _cachedMapManager;
    }

    /// <summary>
    /// Thực thi từ IInteractable. 
    /// Có thể được gọi từ Trigger hoặc từ một hệ thống Raycast/Input của Player.
    /// </summary>
    public void Interact()
    {
        if (!CanInteract) return;

        // 1. Thống kê và Event (Chỉ chạy 1 lần khi Player thực sự chạm vào)
        DataManager.Instance.Profile.RegisterButtonPress();
        GameplayEvents.TriggerButtonPressed();

        // 2. Gửi yêu cầu kích hoạt lên đầu não MapManager
        IMapManager manager = GetMapManager();
        if (manager != null)
        {
            // Manager sẽ quyết định khi nào nút này thực sự được "Activate" 
            // (Ví dụ: Server xác nhận hoặc kiểm tra đúng thứ tự)
            manager.TriggerCurrentButton();
        }
        else
        {
            // Fallback cho môi trường test/sandbox không có MapManager
            Activate();
        }
    }

    /// <summary>
    /// Thực thi kích hoạt nút (Visual + Logic). 
    /// Được gọi bởi ButtonSequenceManager để tránh vòng lặp.
    /// </summary>
    public void Activate()
    {
        if (!CanInteract) return;

        ButtonState nextState = _isExplosive ? ButtonState.Warning : ButtonState.Activated;
        
        // Cập nhật trạng thái và hình ảnh ngay lập tức
        SetState(nextState); 
        TriggerActivationLogic(nextState);
    }

    private void TriggerActivationLogic(ButtonState state)
    {
        // Logic này chạy khi nút đã được xác nhận kích hoạt
        
        // 1. Thông báo cho ButtonSequenceManager (để mở cửa/nút tiếp theo)
        OnButtonActivated?.Invoke();

        // 2. Map Actions & Stats
        if (_actions.Count > 0)
        {
            IMapManager manager = GetMapManager();
            foreach (var action in _actions)
                if (action != null) StartCoroutine(action.ExecuteRoutine(manager));
        }

        // Logic cộng Stats đã được chuyển lên hàm Interact() để đảm bảo 
        // người chơi nào ấn thì người đó nhận, thay vì chỉ Server nhận.

        // 3. Xử lý Audio/Explosion
        if (state == ButtonState.Warning)
        {
            StartCoroutine(ExplosionProcess());
        }
        else if (state == ButtonState.Activated && !_isExplosive)
        {
            PlayActivationAudio();
        }
    }

    private void PlayActivationAudio()
    {
        if (_audioSource != null && _activationSound != null)
        {
            _audioSource.pitch = _activationPitch;
            float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
            _audioSource.PlayOneShot(_activationSound, volume);
        }
    }

    private IEnumerator ExplosionProcess()
    {
        float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;

        // Play Warning Audio (ticking)
        if (_audioSource != null && _warningSound != null)
        {
            _audioSource.clip = _warningSound;
            _audioSource.loop = true; // Cho phép âm thanh warning lặp lại
            _audioSource.pitch = 1f;
            _audioSource.Play();
        }

        // Phát tiếng ấn nút (Click) ngay lập tức, đè lên tiếng ticking
        if (_audioSource != null && _activationSound != null)
        {
            // Lấy âm lượng SFX từ SettingsManager
            _audioSource.PlayOneShot(_activationSound, volume);
        }

        // Tính toán thời gian chờ cho từng giai đoạn
        // Đảm bảo _timeUpLeadTime không lớn hơn tổng thời gian đợi
        float actualTimeUpLeadTime = Mathf.Min(_timeUpLeadTime, _explosionDelay);
        float warningDuration = _explosionDelay - actualTimeUpLeadTime;

        // Đợi hết thời gian "ticking"
        if (warningDuration > 0)
        {
            yield return new WaitForSeconds(warningDuration);
        }

        // Dừng tiếng ticking và reset lại trạng thái AudioSource
        if (_audioSource != null && _audioSource.isPlaying)
        {
            _audioSource.Stop();
            _audioSource.loop = false;
        }

        // Play Time Up Sound (Ting)
        if (_audioSource != null && _timeUpSound != null)
        {
            _audioSource.PlayOneShot(_timeUpSound, volume);
        }

        // Đợi hết thời gian "ting" trước khi nổ
        if (actualTimeUpLeadTime > 0)
        {
            yield return new WaitForSeconds(actualTimeUpLeadTime);
        }


        // Play Explosion Audio
        if (_audioSource != null && _explosionSound != null) 
        {
            _audioSource.PlayOneShot(_explosionSound, volume);
        }

        // Spawn hiệu ứng nổ (Particle System)
        if (_explosionEffectPrefab != null)
        {
            GameObject effectInstance = Instantiate(_explosionEffectPrefab, transform.position, Quaternion.identity);
            
            // Tìm và kích hoạt tất cả Particle Systems có trong Prefab (bao gồm cả các object con)
            ParticleSystem[] allParticles = effectInstance.GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem ps in allParticles)
            {
                ps.Play();
            }
        }

        // Kiểm tra quyền Server hoặc SP
        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        bool isOffline = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;

        if (isServer || isOffline)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, _explosionRadius, _targetLayer);
            foreach (var hit in hits)
            {
                if (hit.TryGetComponent(out IPlayer player))
                {
                    player.Die(DeathReason.Explosion);
                }
            }
            SetState(ButtonState.Activated);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _explosionRadius);
    }
}