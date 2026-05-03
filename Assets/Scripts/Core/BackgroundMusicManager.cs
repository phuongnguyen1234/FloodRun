using UnityEngine;
using Core.Interfaces;
using Core.Events;

namespace Core
{
    /// <summary>
    /// Quản lý nhạc nền tập trung và hiệu ứng âm thanh môi trường (như dưới nước).
    /// </summary>
    [RequireComponent(typeof(AudioSource), typeof(AudioLowPassFilter))]
    public class BackgroundMusicManager : MonoBehaviour
    {
        public static BackgroundMusicManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("Kéo GameObject của Player vào đây để script biết khi nào người chơi đang bơi.")]
        private IPlayer _currentPlayer; // Dùng Interface thay vì class cụ thể PlayerMotor

        [Header("Audio Settings")]
        [Tooltip("Tần số cắt khi ở dưới nước (Hz). Càng thấp nghe càng trầm/nghẹt. Mặc định Unity là 5000.")]
        [SerializeField] private float _underwaterCutoff = 800f;

        private AudioSource _audioSource;
        private AudioLowPassFilter _lowPassFilter;

        private void Awake()
        {
            // Singleton Pattern
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); 
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // Lấy các component cần thiết và lưu lại để sử dụng
            _audioSource = GetComponent<AudioSource>();
            _lowPassFilter = GetComponent<AudioLowPassFilter>();

            // Cài đặt tần số cắt mong muốn
            if (_lowPassFilter != null) _lowPassFilter.cutoffFrequency = _underwaterCutoff;

            // Fix: Mặc định tắt LowPassFilter ngay khi khởi chạy để đảm bảo âm thanh rõ
            if (_lowPassFilter != null) _lowPassFilter.enabled = false;

            // Áp dụng âm lượng từ Settings
            if (SettingsManager.Instance != null && _audioSource != null)
            {
                _audioSource.volume = SettingsManager.Instance.MusicVolume;
            }
        }

        private void OnEnable()
        {
            GameplayEvents.OnPauseRequested += HandlePause;
            GameplayEvents.OnLocalPlayerSpawned += SetPlayer;
        }

        private void OnDisable()
        {
            GameplayEvents.OnPauseRequested -= HandlePause;
            GameplayEvents.OnLocalPlayerSpawned -= SetPlayer;
        }

        private void Update()
        {
            // FIX: Chỉ bật hiệu ứng LowPass khi có Player đang bơi VÀ AudioSource đang có nhạc.
            // Nếu bật filter khi clip bị null, Unity sẽ báo warning "Only custom filters can be played".
            if (_currentPlayer != null && _audioSource.clip != null)
            {
                _lowPassFilter.enabled = _currentPlayer.IsSwimming;
            }
            else if (_lowPassFilter != null && _lowPassFilter.enabled)
            {
                // Nếu không có player (Menu), đảm bảo filter luôn tắt
                _lowPassFilter.enabled = false;
            }
        }

        private void HandlePause(bool paused)
        {
            // Bảo vệ: Không gọi lệnh Audio nếu chưa có clip được gán
            if (_audioSource.clip == null) return;

            if (paused) _audioSource.Pause();
            else _audioSource.UnPause();
        }

        /// <summary>
        /// Đăng ký Player với Manager để theo dõi trạng thái bơi/dưới nước.
        /// </summary>
        public void SetPlayer(IPlayer player)
        {
            _currentPlayer = player;
        }

        /// <summary>
        /// Hàm public để các script khác (như MapManager) có thể lấy AudioSource và phát nhạc.
        /// </summary>
        /// <returns>Component AudioSource của trình quản lý nhạc nền.</returns>
        public AudioSource GetAudioSource()
        {
            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();
            return _audioSource;
        }
    }
}
