using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core;

namespace UI
{
    /// <summary>
    /// Lớp này chịu trách nhiệm hiển thị thông tin của một achievement trên UI, bao gồm:
    /// - Icon: hình đại diện của achievement
    /// - Title: tiêu đề của achievement, sẽ có hậu tố "(Not Completed)" nếu chưa hoàn thành
    /// - Description: mô tả chi tiết về achievement
    /// - Reward: phần thưởng (số coin) sẽ hiển thị nếu achievement chưa hoàn thành, và sẽ ẩn nếu đã hoàn thành
    /// - Lock Overlay: một lớp phủ hiển thị khi achievement chưa hoàn thành để tạo cảm giác bị khóa, 
    /// nhưng vẫn giữ alpha của card ở mức 1f để không làm mờ toàn bộ card. 
    /// Tuy nhiên, phần thưởng sẽ bị ẩn đi khi đã hoàn thành để tránh gây nhầm lẫn cho người chơi. 
    /// Card sẽ không thể tương tác khi achievement chưa hoàn thành để tránh bấm nhầm vào card bị khóa.
    /// </summary>
    public class AchievementCardView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _descriptionText;
        [SerializeField] private TextMeshProUGUI _rewardText;
        [SerializeField] private GameObject _rewardBlock; // Kéo cụm UI Reward (Text+Icon) vào đây
        [SerializeField] private GameObject _lockOverlay;
        [SerializeField] private CanvasGroup _canvasGroup;

        public void Setup(AchievementSO achievement, bool isUnlocked)
        {
            if (_iconImage != null) _iconImage.sprite = achievement.Icon;

            // Cập nhật tiêu đề: thêm hậu tố nếu chưa hoàn thành
            string displayTitle = achievement.Title;
            if (!isUnlocked) displayTitle += " (Not Completed)";
            
            if (_titleText != null) _titleText.text = displayTitle;
            if (_descriptionText != null) _descriptionText.text = achievement.Description;
            if (_rewardText != null) _rewardText.text = achievement.CoinReward.ToString();

            // Bật/tắt overlay dựa trên trạng thái mở khóa
            if (_lockOverlay != null) _lockOverlay.SetActive(!isUnlocked);

            // Ẩn khối reward nếu đã unlock
            if (_rewardBlock != null) _rewardBlock.SetActive(!isUnlocked);

            // Giữ Alpha luôn là 1f (không làm mờ) theo yêu cầu, 
            // nhưng vẫn cập nhật interactable để tránh bấm nhầm vào card bị khóa
            SetState(1f, isUnlocked);
        }

        private void SetState(float alpha, bool interactable)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = alpha;
            _canvasGroup.interactable = interactable;
            _canvasGroup.blocksRaycasts = interactable;
        }
    }
}
