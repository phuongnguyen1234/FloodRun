using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Nút chọn độ khó trong giao diện người dùng. Cho phép thiết lập văn bản và màu sắc của nút để phản ánh độ khó được chọn.
/// </summary>
public class DifficultyButton : MonoBehaviour
{
    [SerializeField] private Image _background;
    [SerializeField] private TextMeshProUGUI _label;
    [SerializeField] private Button _button;

    public Button Button => _button;

    public void SetVisuals(string text, Color color)
    {
        if (_label != null) _label.text = text;
        if (_background != null) _background.color = color;
    }
}