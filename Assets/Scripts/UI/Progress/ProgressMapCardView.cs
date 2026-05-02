using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core;

namespace UI
{
    /// <summary>
    /// ProgressMapCardView đại diện cho một card hiển thị thông tin về bản đồ đã hoàn thành, bao gồm hình ảnh, tên, thời gian kỷ lục và màu sắc theo độ khó.
    /// </summary>
    public class ProgressMapCardView : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] private Image _previewImage;
        [SerializeField] private Image _overlayImage; // Ảnh màu đè lên card
        
        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _recordText;

        [Header("Settings")]
        [SerializeField] private DifficultyPalette _palette;

        public void Setup(MapData data, float bestTime)
        {
            // 1. Cài đặt thông tin cơ bản
            if (_previewImage != null) _previewImage.sprite = data.MapPreviewImage;
            if (_nameText != null) _nameText.text = data.Name;

            // 2. Cài đặt màu sắc theo Tier
            if (_palette != null)
            {
                Color themeColor = _palette.GetColorFromRating(data.Difficulty);
                // Gán cố định Alpha = 64 (64/255f) để giữ độ trong suốt overlay
                themeColor.a = 64f / 255f; 
                if (_overlayImage != null) _overlayImage.color = themeColor;
            }

            // 3. Hiển thị thời gian kỷ lục
            if (_recordText != null)
            {
                if (bestTime > 0)
                {
                    _recordText.text = FormatTime(bestTime);
                    _recordText.color = Color.white;
                }
                else
                {
                    _recordText.text = "Not Completed";
                    _recordText.color = new Color(1, 1, 1);
                }
            }
        }

        private string FormatTime(float time)
        {
            System.TimeSpan t = System.TimeSpan.FromSeconds(time);
            return string.Format("{0:0}:{1:00}.{2:000}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
        }
    }
}