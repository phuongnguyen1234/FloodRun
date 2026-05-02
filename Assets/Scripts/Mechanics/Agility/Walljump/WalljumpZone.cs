using UnityEngine;

/// <summary>
/// WalljumpZone: Quản lý khu vực walljump với khả năng đồng bộ kích thước giữa SpriteRenderer và BoxCollider2D.
/// Cũng như cung cấp các thuộc tính để điều chỉnh tốc độ cuộn texture và màu sắc của mũi tên (texture) thông qua MaterialPropertyBlock.
/// Thiết kế để dễ dàng sử dụng và chỉnh sửa trong Editor, giúp Designer có thể nhanh chóng căn chỉnh và tạo hiệu ứng mong muốn cho khu vực walljump.
/// </summary>
[ExecuteAlways]
public class WalljumpZone : MonoBehaviour
{
    public SpriteRenderer mainWallRenderer; // Đây là SpriteRenderer của GameObject gốc (root prefab), hiển thị nền tường
    public SpriteRenderer arrowVisualRenderer; // Đây là SpriteRenderer của GameObject con, hiển thị mũi tên cuộn
    public BoxCollider2D col;

    [Header("Visual Settings")]
    [Tooltip("Tốc độ cuộn của texture (X, Y).")]
    public Vector2 scrollSpeed = new Vector2(0, -1);
    [Tooltip("Màu sắc của các mũi tên (Texture).")]
    public Color arrowColor = Color.white;
    private MaterialPropertyBlock _propBlock;
    private static readonly int ScrollSpeedID = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    void Update()
    {
        // Luôn cập nhật các thuộc tính hiển thị (màu, tốc độ cuộn) mỗi frame
        UpdateVisualProperties();

        // Chỉ đồng bộ kích thước Collider/Renderer khi đang ở Edit Mode (để Designer căn chỉnh)
        // Hoặc khi có sự thay đổi kích thước cụ thể
        if (!Application.isPlaying)
            SyncSize();
    }

    void OnValidate()
    {
        // 1. Tự động tìm component trên cha nếu chưa gán
        if (mainWallRenderer == null) mainWallRenderer = GetComponent<SpriteRenderer>();
        if (col == null) col = GetComponent<BoxCollider2D>();

        // 2. Tự động tìm background ở object con (loại trừ chính mình)
        if (arrowVisualRenderer == null)
        {
            foreach (Transform child in transform)
            {
                if (child.TryGetComponent(out SpriteRenderer childRenderer) && childRenderer != mainWallRenderer)
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
        // Collider size dựa trên kích thước của mainWallRenderer (nền tường)
        if (mainWallRenderer != null && col != null)
        {
            col.size = mainWallRenderer.size;
        }
    }

    void UpdateVisualProperties()
    {
        // Cập nhật thuộc tính cho arrowVisualRenderer (mũi tên cuộn)
        if (mainWallRenderer != null && arrowVisualRenderer != null)
        {
            // Đồng bộ chế độ vẽ (Sliced/Tiled) và kích thước của mũi tên theo nền tường
            if (arrowVisualRenderer.drawMode != mainWallRenderer.drawMode) arrowVisualRenderer.drawMode = mainWallRenderer.drawMode;
            arrowVisualRenderer.size = mainWallRenderer.size;
            
            // Đảm bảo arrowVisualRenderer nằm chính giữa cha (mainWallRenderer)
            if (arrowVisualRenderer.transform.localPosition != Vector3.zero)
                arrowVisualRenderer.transform.localPosition = Vector3.zero;

            // Cập nhật tốc độ cuộn và màu sắc vào Shader của arrowVisualRenderer
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            arrowVisualRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetVector(ScrollSpeedID, new Vector4(scrollSpeed.x, scrollSpeed.y, 0, 0));
                // Sử dụng biến arrowColor riêng biệt thay vì lấy từ visual.color
                _propBlock.SetColor(ColorID, arrowColor);
            arrowVisualRenderer.SetPropertyBlock(_propBlock);
        }
    }
}