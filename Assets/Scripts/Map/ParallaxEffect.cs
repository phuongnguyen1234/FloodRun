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
    [Tooltip("Hệ số di chuyển theo camera. 0 = đứng yên, 1 = di chuyển cùng camera. Giá trị nhỏ hơn cho cảm giác xa hơn.")]
    [SerializeField] private Vector2 _parallaxEffectMultiplier = new Vector2(0.5f, 0.2f);

    [Header("Infinite Tiling")]
    [Tooltip("Bật nếu muốn background lặp lại vô tận theo chiều ngang.")]
    [SerializeField] private bool _infiniteHorizontal = true;
    [Tooltip("Bật nếu muốn background lặp lại vô tận theo chiều dọc.")]
    [SerializeField] private bool _infiniteVertical = false;

    private Vector3 _lastCameraPosition;
    private float _textureUnitSizeX;
    private float _textureUnitSizeY;

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

        _lastCameraPosition = _cameraTransform.position;

        // Tính toán kích thước của sprite để lặp lại
        Sprite sprite = GetComponent<SpriteRenderer>()?.sprite;
        if (sprite != null)
        {
            // CẢI TIẾN: Lấy kích thước thực tế của 1 vòng lặp (Sprite size * Scale)
            // Không dùng renderer.bounds vì nó sẽ lấy tổng kích thước vùng Tiled
            _textureUnitSizeX = (sprite.rect.width / sprite.pixelsPerUnit) * transform.localScale.x;
            _textureUnitSizeY = (sprite.rect.height / sprite.pixelsPerUnit) * transform.localScale.y;
        }
    }

    private void LateUpdate()
    {
        if (_cameraTransform == null) return;

        // Tính toán khoảng cách camera đã di chuyển kể từ frame trước
        Vector3 deltaMovement = _cameraTransform.position - _lastCameraPosition;

        // Di chuyển background một khoảng tương ứng với hệ số parallax
        transform.position += new Vector3(deltaMovement.x * _parallaxEffectMultiplier.x, deltaMovement.y * _parallaxEffectMultiplier.y);

        // Cập nhật lại vị trí camera cho frame tiếp theo
        _lastCameraPosition = _cameraTransform.position;

        // Xử lý lặp lại background theo chiều ngang (Snapping logic)
        if (_infiniteHorizontal)
        {
            if (Mathf.Abs(_cameraTransform.position.x - transform.position.x) >= _textureUnitSizeX)
            {
                float offsetPositionX = (_cameraTransform.position.x - transform.position.x) % _textureUnitSizeX;
                transform.position = new Vector3(_cameraTransform.position.x - offsetPositionX, transform.position.y);
            }
        }

        if (_infiniteVertical)
        {
            if (Mathf.Abs(_cameraTransform.position.y - transform.position.y) >= _textureUnitSizeY)
            {
                float offsetPositionY = (_cameraTransform.position.y - transform.position.y) % _textureUnitSizeY;
                transform.position = new Vector3(transform.position.x, _cameraTransform.position.y - offsetPositionY);
            }
        }
    }
}