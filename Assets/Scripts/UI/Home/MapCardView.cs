using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core;

/// <summary>
/// Script để hiển thị thông tin của một Map trong màn hình chọn Map.
/// </summary>
public class MapCardView : MonoBehaviour
{
    [Header("Visual States")]
    [SerializeField] private Image _mapPreviewImage;
    [SerializeField] private GameObject _completeObject;

    [Header("Information References")]
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _difficultyRatingText;

    [Header("Settings")]
    [SerializeField] private DifficultyPalette _palette;

    public void Setup(MapData data, bool isCompleted)
    {
        if (_mapPreviewImage != null) _mapPreviewImage.sprite = data.MapPreviewImage;

        // Lấy màu chủ đạo dựa trên độ khó từ Palette
        Color themeColor = Color.white;
        if (_palette != null)
        {
            themeColor = _palette.GetColorFromRating(data.Difficulty);
            themeColor.a = 1f; // Đảm bảo Alpha luôn bằng 1 để tránh lỗi hiển thị từ Inspector
        }

        // Đổi màu tên Map theo độ khó
        if (_nameText != null)
        {
            _nameText.text = data.Name;
            // Sử dụng hệ thống Gradient để có màu sắc chuẩn nhất
            _nameText.enableVertexGradient = true;
            _nameText.colorGradient = new VertexGradient(themeColor);
            // Đặt Vertex Color về trắng để không làm tối/sai màu Gradient
            _nameText.color = Color.white;
        }
        
        if (_difficultyRatingText != null)
        {
            // Hiển thị con số độ khó nhưng giữ nguyên màu trắng theo yêu cầu
            _difficultyRatingText.text = data.Difficulty.ToString("F1");
            _difficultyRatingText.color = Color.white;
            _difficultyRatingText.enableVertexGradient = false;
        }

        // Hiển thị dấu tích đã hoàn thành hay chưa
        if (_completeObject != null) _completeObject.SetActive(isCompleted);
    }
}