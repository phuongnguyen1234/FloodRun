using UnityEngine;
/// <summary>
/// Interface định nghĩa một vật thể có thể leo được (Thang).
/// Bất kỳ object nào trong Mechanics (LadderZone, VineZone, etc.) muốn cho Player leo lên
/// đều phải implement interface này.
/// </summary>
public interface ILadder
{
    float GetCenterX();
    float GetWidth();
    float GetTopY();
    Collider2D GetTopPlatformCollider();
}