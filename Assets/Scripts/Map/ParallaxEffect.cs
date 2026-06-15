using UnityEngine;

/// <summary>
/// Tạo hiệu ứng Parallax Scrolling cho một lớp background.
/// Script này sẽ di chuyển transform của nó dựa trên vị trí của camera.
/// </summary>
[DefaultExecutionOrder(1000)] // FIX: Chạy sau Cinemachine (thường update ở LateUpdate) để tránh rung giật
public class ParallaxEffect : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform của camera chính. Nếu để trống, sẽ tự động tìm Main Camera.")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Parallax Settings")]
    [Range(0f, 1f)]
    [Tooltip("Hệ số Parallax X: 0 = Gần cam (chạy nhanh trên màn hình), 1 = Xa vô tận (đứng yên trên màn hình).")]
    [SerializeField] private float _parallaxMultiplierX = 0.5f;

    [Header("Infinite Tiling")]
    [Tooltip("Bật nếu muốn background lặp lại vô tận theo chiều ngang.")]
    [SerializeField] private bool _infiniteHorizontal = true;

    [Tooltip("Nếu camera di chuyển xa hơn mức này trong 1 frame, coi như là Teleport và reset parallax.")]
    [SerializeField] private float _teleportThreshold = 5f;

    private Vector2 _lastCameraPosition;
    private Vector3 _initialLocalPosition;
    private float _textureUnitSizeX;
    private bool _isInitialized = false;

    private void Start()
    {
        if (_cameraTransform == null)
        {
            // Cố gắng tìm camera chính trong scene
            if (Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
            else
            {
                // Nếu không tìm thấy, báo lỗi và vô hiệu hóa script này để tránh lỗi runtime
                Debug.LogError("ParallaxEffect: Không tìm thấy Main Camera. Hãy đảm bảo camera trong Scene của bạn có tag 'MainCamera'.", this);
                this.enabled = false;
                return;
            }
        }

        // Tính toán kích thước của sprite để lặp lại
        Sprite sprite = GetComponent<SpriteRenderer>()?.sprite;
        if (sprite != null)
        {
            // CẢI TIẾN: Lấy pixelsPerUnit từ sprite gốc để tính toán chuẩn xác 
            // kích thước của 1 mẫu texture trước khi nó được Tiled.
            _textureUnitSizeX = sprite.rect.width / sprite.pixelsPerUnit;
        }

        _initialLocalPosition = transform.localPosition;
    }

    /// <summary>
    /// Reset lại điểm neo của Parallax. Cần gọi khi Camera Teleport đến Map mới.
    /// </summary>
    public void ResetOrigin()
    {
        if (_cameraTransform == null) return;
        
        // Đảm bảo lấy vị trí camera tại ĐÚNG thời điểm này (đã được Warp)
        Vector3 cameraWorldPos = _cameraTransform.position;
        
        // Tính toán vị trí camera trong không gian của cha (Map)
        Vector3 localCameraPos = transform.parent != null 
            ? transform.parent.InverseTransformPoint(cameraWorldPos) 
            : cameraWorldPos;

        // FIX: Không ghi lại _startCameraPosition nữa. 
        // Mốc (0,0) của Map Parent chính là mốc cố định cho mọi người chơi.
        _lastCameraPosition = (Vector2)localCameraPos;
        _isInitialized = true;
    }

    private void LateUpdate()
    {
        if (_cameraTransform == null) return;

        // 1. Lấy vị trí World của Camera
        Vector3 cameraWorldPos = _cameraTransform.position;

        // 2. Chuyển đổi vị trí Camera sang Local Space tương đối với Map (parent của background)
        Vector3 localCameraPos = transform.parent != null 
            ? transform.parent.InverseTransformPoint(cameraWorldPos) 
            : cameraWorldPos;

        // Khởi tạo ngay trong frame đầu tiên có camera
        if (!_isInitialized)
        {
            ResetOrigin();
        }

        Vector2 currentCamPos2D = new Vector2(localCameraPos.x, localCameraPos.y);

        // CẢI TIẾN: Kiểm tra Teleport dựa trên khoảng cách di chuyển giữa 2 frame (Frame Delta)
        // Thay vì so sánh với điểm bắt đầu, ta so sánh với frame trước đó.
        if (Vector2.Distance(currentCamPos2D, _lastCameraPosition) > _teleportThreshold)
        {
            ResetOrigin();
            return; 
        }
        _lastCameraPosition = currentCamPos2D;

        // FIX: Tính toán vị trí dựa trên tọa độ tuyệt đối trong Map (Local Space của Parent)
        // Điều này đảm bảo dù Spectate tại bất kỳ thời điểm nào, Background vẫn khớp 100% với Host.
        float targetX;
        float targetY = localCameraPos.y; // Mặc định Y đi theo Camera 1:1 để giữ background luôn ở giữa màn hình dọc

        // XỬ LÝ LẶP LẠI (SMOOTH INFINITE TILING)
        if (_infiniteHorizontal && _parallaxMultiplierX != 1 && _textureUnitSizeX > 0)
        {
            // CẢI TIẾN: Trừ đi _initialLocalPosition.x để giữ nguyên offset thiết kế của Designer trong Prefab
            float effectiveCamX = localCameraPos.x - _initialLocalPosition.x;
            float relativeOffset = effectiveCamX * (1 - _parallaxMultiplierX) % _textureUnitSizeX;
            
            targetX = localCameraPos.x - relativeOffset;
        }
        else
        {
            // Công thức Parallax đồng bộ: 
            // Nếu Multiplier = 1 (Xa vô tận): Background sẽ ở đúng _initialLocalPosition.x + deltaX (đi theo Cam)
            // Nếu Multiplier = 0 (Gần nhất): Background sẽ đứng yên tại _initialLocalPosition.x (vị trí trong World)
            float deltaXFromInitial = localCameraPos.x - _initialLocalPosition.x;
            targetX = _initialLocalPosition.x + (deltaXFromInitial * _parallaxMultiplierX);
        }

        transform.localPosition = new Vector3(targetX, targetY, transform.localPosition.z);
    }
}