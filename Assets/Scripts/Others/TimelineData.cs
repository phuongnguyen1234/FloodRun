using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lớp này đại diện cho một mục trong changelog, bao gồm phiên bản, ngày phát hành, 
/// mô tả và thông tin về việc đó có phải là bản cập nhật lớn hay không.
/// </summary>
[System.Serializable]
public class ChangelogEntry {
    public string version;
    public string date;
    [TextArea(3, 10)] public string description;
    public bool isMajorUpdate; // Để đổi màu hoặc kích thước nếu là bản cập nhật lớn
}

[CreateAssetMenu(fileName = "TimelineData", menuName = "Flood Run/Timeline")]
public class TimelineData : ScriptableObject {
    public List<ChangelogEntry> entries;
}