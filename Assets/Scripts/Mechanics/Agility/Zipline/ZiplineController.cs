using UnityEngine;

/// <summary>
/// Điều khiển dây zipline với khả năng uốn cong bằng đường cong Bézier bậc 3 (Cubic).
/// Cho phép thiết lập điểm bắt đầu, điểm kết thúc và 2 điểm điều khiển để tạo ra đường cong tự nhiên hơn.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class ZiplineController : MonoBehaviour, IZipline
{
    [Header("References")]
    [Tooltip("Điểm bắt đầu của dây (thường là object con LineStart của ZiplineStart)")]
    [SerializeField] private Transform _startPoint;
    [Tooltip("Điểm kết thúc của dây (thường là object con LineEnd của ZiplineEnd)")]
    [SerializeField] private Transform _lineEndTarget;
    [Tooltip("Điểm điều khiển 1 để uốn cong dây (gần điểm đầu).")]
    [SerializeField] private Transform _controlPoint1;
    [Tooltip("Điểm điều khiển 2 để uốn cong dây (gần điểm cuối).")]
    [SerializeField] private Transform _controlPoint2;
    [Header("Chaining")]
    [Tooltip("Kéo một Zipline khác vào đây để tự động chuyển sang khi trượt hết dây.")]
    [SerializeField] private ZiplineController _nextZipline;
    
    [Header("Settings")]
    [SerializeField] private float _speed = 10f;
    [SerializeField] [Range(3, 50)] private int _lineResolution = 20;
    [Tooltip("Nếu bật, player sẽ bay theo hướng dây với tốc độ của dây khi kết thúc (như bệ phóng).")]
    [SerializeField] private bool _isLauncher = false;

    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();

        // Đảm bảo LineRenderer sử dụng tọa độ Local để không bị lệch khi Map spawn ở xa
        if (_lineRenderer != null)
            _lineRenderer.useWorldSpace = false;

        
        UpdateZiplineVisuals();
    }

    private void Update()
    {
        // Trong Editor, cập nhật liên tục để bạn kéo thả thoải mái
        if (!Application.isPlaying)
        {
            UpdateZiplineVisuals();
        }
    }

    /// <summary>
    /// Vẽ LineRenderer và cập nhật EdgeCollider nối giữa 2 điểm
    /// </summary>
    private void UpdateZiplineVisuals()
    {
        if (_startPoint == null || _lineEndTarget == null || _lineRenderer == null)
            return;

        // Luôn vẽ đường cong Bézier bậc 3 (Cubic).
        // Nếu người dùng không gán điểm control, các điểm sẽ được tự động tính toán để tạo đường thẳng.
        _lineRenderer.positionCount = _lineResolution;

        Vector3 p0 = transform.InverseTransformPoint(GetStartPoint());
        Vector3 p1 = transform.InverseTransformPoint(GetControlPoint1());
        Vector3 p2 = transform.InverseTransformPoint(GetControlPoint2());
        Vector3 p3 = transform.InverseTransformPoint(GetEndPoint());

        for (int i = 0; i < _lineResolution; i++)
        {
            float t = i / (float)(_lineResolution - 1);
            Vector3 point = CalculateCubicBezierPoint(t, p0, p1, p2, p3);
            _lineRenderer.SetPosition(i, point);
        }
    }

    private Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // B(t) = (1-t)^3 * P0 + 3(1-t)^2 * t * P1 + 3(1-t) * t^2 * P2 + t^3 * P3
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;
        return (uuu * p0) + (3 * uu * t * p1) + (3 * u * tt * p2) + (ttt * p3);
    }

    // Implementation of IZipline
    public Vector3 GetStartPoint()
    {
        if (_startPoint == null) return transform.position;
        return _startPoint.position;
    }

    public Vector3 GetControlPoint1()
    {
        if (_controlPoint1 != null) return _controlPoint1.position;
        // Nếu không có, giả lập điểm nằm ở 1/3 quãng đường
        return Vector3.Lerp(GetStartPoint(), GetEndPoint(), 0.33f);
    }

    public Vector3 GetControlPoint2()
    {
        if (_controlPoint2 != null) return _controlPoint2.position;
        // Nếu không có, giả lập điểm nằm ở 2/3 quãng đường
        return Vector3.Lerp(GetStartPoint(), GetEndPoint(), 0.66f);
    }

    public IZipline NextZipline => _nextZipline;
    public bool IsLauncher => _isLauncher;

    public Vector3 GetEndPoint() => _lineEndTarget.position;
    public float GetSpeed() => _speed;
    
    public Vector3 GetDirection()
    {
        // Trả về hướng ở cuối đường cong (tiếp tuyến tại t=1 cho cubic bezier: P3 - P2)
        Vector3 p2 = GetControlPoint2();
        Vector3 p3 = GetEndPoint();
        return (p3 - p2).normalized;
    }

    private void OnDrawGizmos()
    {
        // Vẽ gizmo để dễ nhìn hướng trượt
        // Chỉ vẽ nếu có đủ reference cơ bản
        if (_startPoint == null || _lineEndTarget == null) return;

        Vector3 startPos = GetStartPoint();
        Vector3 endPos = GetEndPoint();

        Vector3 controlPos1 = GetControlPoint1();
        Vector3 controlPos2 = GetControlPoint2();

        // Vẽ các đường handle màu vàng nếu người dùng đã gán transform
        Gizmos.color = Color.yellow;
        if (_controlPoint1 != null) Gizmos.DrawLine(startPos, controlPos1);
        if (_controlPoint2 != null) Gizmos.DrawLine(endPos, controlPos2);
        // Vẽ đường nối giữa 2 điểm control để dễ hình dung khung dây
        if (_controlPoint1 != null && _controlPoint2 != null)
        {
            Gizmos.DrawLine(controlPos1, controlPos2);
        }

        // Vẽ chính đường cong trong Scene view để dễ hình dung
        Gizmos.color = Color.cyan;
        Vector3 previousPoint = startPos;
        for (int i = 1; i <= _lineResolution; i++)
        {
            float t = i / (float)_lineResolution;
            Vector3 currentPoint = CalculateCubicBezierPoint(t, startPos, controlPos1, controlPos2, endPos);
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }
}