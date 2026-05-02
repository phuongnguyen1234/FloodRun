using UnityEngine;
using TMPro;

/// <summary>
/// Item hiển thị một mục trong changelog, bao gồm phiên bản, ngày phát hành và mô tả thay đổi.
/// </summary>
public class ChangelogItem : MonoBehaviour 
{
    [SerializeField] private TextMeshProUGUI versionText;
    [SerializeField] private TextMeshProUGUI dateText;
    [SerializeField] private TextMeshProUGUI descText;

    public void Setup(ChangelogEntry entry) 
    {
        versionText.text = entry.version;
        dateText.text = entry.date;
        descText.text = entry.description;

        // Tối ưu: Tự động cập nhật layout ngay khi đổ dữ liệu
        // Tránh lỗi ScrollView không tính kịp độ cao của text dài
        Canvas.ForceUpdateCanvases();
    }
}