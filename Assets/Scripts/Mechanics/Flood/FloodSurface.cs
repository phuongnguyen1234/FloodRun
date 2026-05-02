using UnityEngine;

/// <summary>
/// Component này dùng để đồng bộ kích thước và chế độ vẽ giữa visual chính (SpriteRenderer) và background (SpriteRenderer con).
/// </summary>
[ExecuteAlways]
public class FloodSurface : MonoBehaviour
{
    public SpriteRenderer visual;
    public SpriteRenderer background;
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

        // 2. Tự động tìm background ở object con (loại trừ chính mình)
        if (background == null)
        {
            foreach (Transform child in transform)
            {
                if (child.TryGetComponent(out SpriteRenderer childRenderer))
                {
                    background = childRenderer;
                    break; // Lấy object con đầu tiên có SpriteRenderer
                }
            }
        }

        SyncSize();
    }

    void SyncSize()
    {
        if (visual != null)
        {
            // Sync collider size theo visual
            if (col != null) col.size = visual.size;
            
            if (background != null)
            {
                // Đồng bộ chế độ vẽ (Sliced/Tiled)
                if (background.drawMode != visual.drawMode) background.drawMode = visual.drawMode;
                
                // Đồng bộ kích thước và vị trí
                background.size = visual.size;
                if (background.transform.localPosition != Vector3.zero) background.transform.localPosition = Vector3.zero;
            }
        }
    }
}