using UnityEngine;

/// <summary>
/// Setup một vùng thang có thể leo được. Có thể tùy chỉnh để có phần nền và platform 1 chiều ở đỉnh thang.
/// </summary>
[ExecuteAlways]
public class LadderZone : MonoBehaviour, ILadder
{
    public SpriteRenderer visual;
    public BoxCollider2D col;

    [Header("Settings")]
    [Tooltip("Nếu bật, thang sẽ có platform 1 chiều ở đỉnh để đứng lên được")]
    public bool hasTopPlatform = true;
    [Tooltip("Layer của sàn ở đỉnh thang (Phải thuộc GroundLayer của PlayerMotor)")]
    public LayerMask topPlatformLayer;
    [Tooltip("Độ dày của platform trên đỉnh")]
    public float topPlatformHeight = 0.2f;

    private BoxCollider2D _topPlatformCollider;

    void Update()
    {
        if (!Application.isPlaying) SyncSize();
    }

    void OnValidate()
    {
        if (visual == null) visual = GetComponent<SpriteRenderer>();
        if (col == null) col = GetComponent<BoxCollider2D>();
        
        // Tự động set collider là Trigger để player đi xuyên qua được
        if (col != null) col.isTrigger = true;

        SyncSize();
    }

    void SyncSize()
    {
        if (visual != null)
        {
            if (col != null) col.size = visual.size;
        }
    }

    private void Awake()
    {
        if (Application.isPlaying && hasTopPlatform)
        {
            CreateTopPlatform();
        }
    }

    private void CreateTopPlatform()
    {
        // Tạo object con mới làm Platform
        GameObject platformObj = new GameObject("TopPlatform");
        platformObj.transform.SetParent(transform);
        
        // Chuyển đổi LayerMask thành Layer Index (lấy bit đầu tiên được bật)
        int layerIndex = 0;
        int mask = topPlatformLayer.value;
        for (int i = 0; i < 32; i++) { if ((mask & (1 << i)) != 0) { layerIndex = i; break; } }
        
        platformObj.layer = layerIndex; 
        
        // Đặt vị trí ở ngay đỉnh thang
        // (size.y / 2) là đỉnh của thang
        float topY = col.size.y / 2f;
        // Đặt platform sao cho mặt trên của nó bằng với đỉnh thang
        platformObj.transform.localPosition = new Vector3(0, topY - (topPlatformHeight / 2f), 0);

        // Thêm BoxCollider2D
        _topPlatformCollider = platformObj.AddComponent<BoxCollider2D>();
        _topPlatformCollider.size = new Vector2(col.size.x, topPlatformHeight);
        _topPlatformCollider.usedByEffector = true; // Quan trọng: Để dùng được Effector

        // Thêm PlatformEffector2D (Cơ chế One-Way)
        PlatformEffector2D effector = platformObj.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        effector.surfaceArc = 180f; // Chỉ chặn va chạm từ trên xuống
    }
    
    // Expose collider để Ability có thể bỏ qua va chạm khi đang leo
    public Collider2D TopPlatformCollider => _topPlatformCollider;

    public Collider2D GetTopPlatformCollider() => _topPlatformCollider;

    // Hàm lấy vị trí trung tâm thang theo trục X để căn chỉnh player
    public float GetCenterX()
    {
        return transform.position.x;
    }

    public float GetTopY()
    {
        // Tính toán vị trí Y cao nhất dựa trên vị trí transform và chiều cao collider
        // Có tính đến cả Scale của object để đảm bảo độ chính xác trong World Space
        return transform.position.y + (col.size.y * Mathf.Abs(transform.localScale.y) / 2f);
    }

    public float GetWidth()
    {
        // Lấy chiều rộng của collider và nhân với scale của object để có kích thước thực tế trong world space.
        // Điều này đảm bảo logic hoạt động đúng ngay cả khi bạn scale object LadderZone.
        return col != null ? col.size.x * Mathf.Abs(transform.localScale.x) : 0f;
    }
}
