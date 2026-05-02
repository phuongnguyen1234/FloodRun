using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Core;
using Core.Events;
using System.Collections;
using System.Collections.Generic;

namespace UI
{
    /// <summary>
    /// UI Popup hiển thị khi người chơi mở khóa thành tựu mới.
    /// </summary>
    public class AchievementPopupUI : MonoBehaviour
    {
        public static AchievementPopupUI Instance { get; private set; }

        [Header("References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _rewardText;
        [SerializeField] private RectTransform _container;

        [Header("Audio Settings")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _unlockedSound;
        [SerializeField] private float _displayDuration = 5f;

        private Queue<AchievementSO> _achievementQueue = new Queue<AchievementSO>();
        private bool _isShowing = false;
        private Vector2 _originalPopupPos;

        #region Debug & Mock Test
        [Header("Mock Test")]
        [SerializeField] private AchievementSO _testAchievement;

        // Hàm này cho phép bạn chuột phải vào Component AchievementPopupUI 
        // trong Inspector và chọn "Test Show Popup"
        [ContextMenu("Test Show Popup")]
        public void TestShowPopup()
        {
            if (_testAchievement != null)
            {
                EnqueueAchievement(_testAchievement);
            }
            else
            {
                // Nếu không gán SO, tạo một bản tạm thời để test text
                var mock = ScriptableObject.CreateInstance<AchievementSO>();
                mock.Title = "Mock Achievement";
                mock.CoinReward = 999;
                EnqueueAchievement(mock);
            }
        }
        #endregion

        private void Awake()
        {
            // Triển khai Singleton bền vững
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

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0;
                // Tắt hoàn toàn việc chặn Raycast để không ảnh hưởng UI phía sau
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
            if (_container != null) _originalPopupPos = _container.anchoredPosition;
        }

        private void OnEnable()
        {
            AchievementEvents.OnAchievementUnlocked += EnqueueAchievement;
        }

        private void OnDisable()
        {
            AchievementEvents.OnAchievementUnlocked -= EnqueueAchievement;
        }

        private void EnqueueAchievement(AchievementSO achievement)
        {
            _achievementQueue.Enqueue(achievement);
            if (!_isShowing)
            {
                StartCoroutine(ProcessQueueRoutine());
            }
        }

        private IEnumerator ProcessQueueRoutine()
        {
            _isShowing = true;

            while (_achievementQueue.Count > 0)
            {
                AchievementSO current = _achievementQueue.Dequeue();

                // Cập nhật thông tin
                if (_titleText != null) _titleText.text = current.Title;
                if (_rewardText != null) _rewardText.text = $"+{current.CoinReward}";

                float fadeTime = 0.2f;
                float moveOffset = 20f;

                // 1. Reset trạng thái ban đầu (Ở trên cao và mờ) giống GameplayUIManager
                _container.anchoredPosition = _originalPopupPos + new Vector2(0, moveOffset);
                _canvasGroup.alpha = 0f;

                // Phát âm thanh mở khóa
                PlayUnlockSound();

                Sequence seq = DOTween.Sequence().SetUpdate(true);

                // 2. Fade In từ trên xuống vị trí gốc
                seq.Append(_container.DOAnchorPos(_originalPopupPos, fadeTime).SetEase(Ease.Linear));
                seq.Join(_canvasGroup.DOFade(1f, fadeTime));
                
                seq.AppendInterval(_displayDuration);

                // 3. Fade Out bay ngược lên trên
                seq.Append(_container.DOAnchorPos(_originalPopupPos + new Vector2(0, moveOffset), fadeTime).SetEase(Ease.Linear));
                seq.Join(_canvasGroup.DOFade(0f, fadeTime));

                // Chờ cho đến khi Sequence hiện tại hoàn thành mới chạy tiếp vòng lặp
                yield return seq.WaitForCompletion();
            }

            _isShowing = false;
        }

        private void PlayUnlockSound()
        {
            if (_audioSource != null && _unlockedSound != null)
            {
                // Lấy âm lượng SFX từ SettingsManager để đồng bộ với tùy chỉnh của người chơi
                float volume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
                _audioSource.PlayOneShot(_unlockedSound, volume);
            }
        }
    }
}