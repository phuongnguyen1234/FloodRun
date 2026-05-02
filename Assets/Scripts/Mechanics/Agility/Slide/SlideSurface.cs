using UnityEngine;

/// <summary>
/// Component này quản lý một bề mặt trượt (Slide Surface) trong game, bao gồm:
/// - Đồng bộ kích thước và chế độ vẽ giữa SpriteRenderer chính (visual) và SpriteRenderer nền (background).
/// - Cập nhật tốc độ cuộn của texture và màu sắc của các mũi tên thông qua MaterialPropertyBlock để tránh ảnh hưởng đến các instance khác sử dụng cùng material.
/// </summary>
[ExecuteAlways]
public class SlideSurface : MonoBehaviour
{
    public SpriteRenderer mainSurfaceRenderer; // Đây là SpriteRenderer của GameObject gốc (root prefab), hiển thị nền bề mặt trượt
    public SpriteRenderer arrowVisualRenderer; // Đây là SpriteRenderer của GameObject con, hiển thị mũi tên cuộn
    [Tooltip("Optional: Nếu object này có Collider, kéo vào đây để sync size.")]
    public BoxCollider2D col;

    [Header("Surface Settings")]
    [Tooltip("Tốc độ cuộn của texture (X, Y).")]
    public Vector2 scrollSpeed = new Vector2(0, -1);
    [Tooltip("Màu sắc của các mũi tên (Texture).")]
    public Color arrowColor = Color.white;

    private MaterialPropertyBlock _propBlock;
    private static readonly int ScrollSpeedID = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    void Update()
    {
        // Luôn cập nhật hình ảnh để đảm bảo màu sắc và tốc độ cuộn được áp dụng ngay lập tức
        UpdateVisualProperties();

        // Đồng bộ kích thước trong Editor
        if (!Application.isPlaying)
            SyncSize();
    }

    void OnValidate()
    {
        // 1. Tự động tìm component trên cha
        if (mainSurfaceRenderer == null) mainSurfaceRenderer = GetComponent<SpriteRenderer>();
        
        // Thử tìm Collider, nếu không có cũng không sao (vì bạn nói cha chỉ có SpriteRenderer)
        if (col == null) col = GetComponent<BoxCollider2D>();

        // 2. Tự động tìm background ở object con (loại trừ chính mình)
        if (arrowVisualRenderer == null)
        {
            foreach (Transform child in transform)
            {
                if (child.TryGetComponent(out SpriteRenderer childRenderer) && childRenderer != mainSurfaceRenderer)
                {
                    arrowVisualRenderer = childRenderer;
                    break; // Lấy object con đầu tiên có SpriteRenderer
                }
            }
        }
        SyncSize();
    }

    void SyncSize()
    {
        UpdateVisualProperties();

        // Collider size dựa trên kích thước của mainSurfaceRenderer (nền bề mặt)
        if (mainSurfaceRenderer != null && col != null)
        {
            col.size = mainSurfaceRenderer.size;
        }
    }

    void UpdateVisualProperties()
    {
        // Cập nhật thuộc tính cho arrowVisualRenderer (mũi tên cuộn)
        if (mainSurfaceRenderer != null && arrowVisualRenderer != null)
        {
            // Đồng bộ chế độ vẽ (Sliced/Tiled) và kích thước của mũi tên theo nền bề mặt
            if (arrowVisualRenderer.drawMode != mainSurfaceRenderer.drawMode) arrowVisualRenderer.drawMode = mainSurfaceRenderer.drawMode;
            arrowVisualRenderer.size = mainSurfaceRenderer.size;
            
            // Đảm bảo arrowVisualRenderer nằm chính giữa cha (mainSurfaceRenderer)
            if (arrowVisualRenderer.transform.localPosition != Vector3.zero)
                arrowVisualRenderer.transform.localPosition = Vector3.zero;

            // Cập nhật tốc độ cuộn và màu sắc vào Shader của arrowVisualRenderer
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            arrowVisualRenderer.GetPropertyBlock(_propBlock);
                
                _propBlock.SetVector(ScrollSpeedID, new Vector4(scrollSpeed.x, scrollSpeed.y, 0, 0));
                // Sử dụng biến arrowColor riêng biệt để không bị trùng với màu nền
                _propBlock.SetColor(ColorID, arrowColor);

            arrowVisualRenderer.SetPropertyBlock(_propBlock);
        }
    }
}