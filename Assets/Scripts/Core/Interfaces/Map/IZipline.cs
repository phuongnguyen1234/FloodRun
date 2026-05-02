using UnityEngine;

public interface IZipline
{
    Vector3 GetStartPoint();
    Vector3 GetEndPoint();
    Vector3 GetControlPoint1();
    Vector3 GetControlPoint2();
    float GetSpeed();
    // Hướng trượt từ Start đến End
    Vector3 GetDirection();
    IZipline NextZipline { get; }
    // Xác định xem đây có phải là bệ phóng không (giữ nguyên vận tốc khi rời dây)
    bool IsLauncher { get; }
}