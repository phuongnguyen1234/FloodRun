using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View hiển thị changelog, sẽ tự động tìm Container bên trong Prefab để đổ dữ liệu vào
/// </summary>
public class ChangelogView : MonoBehaviour
{
    [Header("References")]
    public GameObject changelogItemPrefab;
    
    // Không cần public để kéo thả nữa, code sẽ tự tìm
    private Transform contentContainer; 
    public TimelineData data;

    private void Start()
    {
        // Tự động tìm Container bên trong Prefab này
        ScrollRect scrollRect = GetComponentInChildren<ScrollRect>();
        if (scrollRect != null) contentContainer = scrollRect.content;
        
        // Nếu tìm không ra hoặc Data chưa gán thì dừng
        if (contentContainer == null || data == null) return;

        // Logic đổ dữ liệu chuyển về đây
        PopulateData();
    }

    private void PopulateData()
    {
        if (data.entries == null) return;

        // Xóa item cũ (nếu có)
        foreach (Transform child in contentContainer) Destroy(child.gameObject);

        // Tạo item mới
        for (int i = data.entries.Count - 1; i >= 0; i--) 
        {
            GameObject item = Instantiate(changelogItemPrefab, contentContainer);
            item.GetComponent<ChangelogItem>().Setup(data.entries[i]);
            item.transform.localScale = Vector3.one; 
        }
    }
}
