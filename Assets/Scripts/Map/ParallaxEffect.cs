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

    private Vector2 _startCameraPosition;
    private Vector2 _lastCameraPosition;
    private Vector3 _startPosition;
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
        
        Vector3 localCameraPos = transform.parent != null 
            ? transform.parent.InverseTransformPoint(_cameraTransform.position) 
            : _cameraTransform.position;

        _startCameraPosition = (Vector2)localCameraPos;
        _lastCameraPosition = _startCameraPosition;
        _startPosition = _initialLocalPosition;
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

        // Tính toán độ dời của camera so với lúc bắt đầu
        float deltaX = localCameraPos.x - _startCameraPosition.x;
        float deltaY = localCameraPos.y - _startCameraPosition.y;

        // Vị trí mục tiêu: 
        // X di chuyển chậm hơn camera tùy theo hệ số
        // Y di chuyển 1:1 cùng camera (giữ nguyên khoảng cách tương đối)
        float targetX = _startPosition.x + (deltaX * _parallaxMultiplierX);
        float targetY = _startPosition.y + deltaY;

        // XỬ LÝ LẶP LẠI (SMOOTH INFINITE TILING)
        if (_infiniteHorizontal && _parallaxMultiplierX != 1 && _textureUnitSizeX > 0)
        {
            // Tính toán khoảng cách mà Camera đã "vượt qua" texture này
            // (1 - multiplier) chính là tốc độ trôi tương đối của texture trên màn hình
            float relativeOffset = localCameraPos.x * (1 - _parallaxMultiplierX) % _textureUnitSizeX;
            
            // Ép vị trí X của background luôn nằm trong tầm nhìn của Camera dựa trên offset lặp lại
            targetX = localCameraPos.x - relativeOffset;
        }

        transform.localPosition = new Vector3(targetX, targetY, transform.localPosition.z);
    }
}