using UnityEngine;

/// <summary>
/// Component này dùng để đồng bộ kích thước và chế độ vẽ giữa visual chính (SpriteRenderer) và background (SpriteRenderer con).
/// </summary>
[ExecuteAlways]
public class FloodSurface : MonoBehaviour
{
    public SpriteRenderer visual;
    public BoxCollider2D col;

    void Update()
    {
        if (!Application.isPlaying) SyncSize();
    }

    void OnValidate()
    {
        // 1. Tự động tìm component trên cha
        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (col == null) col = GetComponent<BoxCollider2D>();
        SyncSize();
    }

    void SyncSize()
    {
        if (visual != null)
        {
            // Sync collider size theo visual
            if (col != null) col.size = visual.size;
        }
    }
}