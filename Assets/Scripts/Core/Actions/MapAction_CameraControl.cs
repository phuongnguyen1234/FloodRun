using UnityEngine;
using Unity.Cinemachine; // Thêm namespace của Cinemachine
using Core.Interfaces;

/// <summary>
/// Action để điều khiển các hành vi của Cinemachine Camera.
/// </summary>
[System.Serializable]
public class MapAction_CameraControl : MapAction
{
    public enum Command
    {
        Shake,
        SwitchToCamera,
        SetFollow,
        SetLookAt,
        ResetPriority // Thêm lệnh để đưa Camera về Priority thấp nhất
    }

    [Tooltip("Hành động camera muốn thực hiện.")]
    public Command CameraCommand;

    [Header("Shake Settings")]
    [Tooltip("Kéo CinemachineImpulseSource vào đây để tạo rung lắc.")]
    public CinemachineImpulseSource ImpulseSource;
    [Tooltip("Hướng và cường độ của cú rung.")]
    public Vector3 ShakeVelocity = Vector3.one;

    [Header("Switch/Modify Settings")]
    [Tooltip("Virtual Camera mục tiêu để chuyển đổi hoặc thay đổi thuộc tính.")]
    public CinemachineVirtualCameraBase TargetCamera;

    [Header("Switch Settings")]
    [Tooltip("Mức ưu tiên (Priority) sẽ được đặt cho camera mục tiêu khi chuyển đổi.")]
    public int NewPriority = 20;

    [Header("Reset Settings")]
    [Tooltip("Mức ưu tiên sẽ trả về khi Reset. Thường là 0 để nhường quyền cho Player Camera.")]
    public int ResetPriorityValue = 0;

    [Header("Target Settings")]
    [Tooltip("Transform mới mà camera sẽ Follow hoặc LookAt.")]
    public Transform NewTarget;

    public override void Execute(IMapManager manager)
    {
        switch (CameraCommand)
        {
            case Command.Shake:
                if (ImpulseSource != null)
                {
                    ImpulseSource.GenerateImpulseWithVelocity(ShakeVelocity);
                }
                else
                {
                    Debug.LogWarning($"[Timeline Action] Camera Shake: Chưa gán ImpulseSource cho action '{Description}'.");
                }
                break;

            case Command.SwitchToCamera:
                if (TargetCamera != null) TargetCamera.Priority = NewPriority;
                break;

            case Command.SetFollow:
                if (TargetCamera != null) TargetCamera.Follow = NewTarget;
                break;

            case Command.SetLookAt:
                if (TargetCamera != null) TargetCamera.LookAt = NewTarget;
                break;

            case Command.ResetPriority:
                if (TargetCamera != null) TargetCamera.Priority = ResetPriorityValue;
                break;
        }
    }
}

/// <summary>
/// Helper static để điều khiển Camera từ bất kỳ đâu trong code mà không cần thông qua Timeline.
/// </summary>
public static class CameraSystemHelper
{
    public const int PRIORITY_LOW = 0;
    public const int PRIORITY_PLAYER = 10;
    public const int PRIORITY_ZONE = 20;
    public const int PRIORITY_CUTSCENE = 100;

    public static void SetActive(CinemachineVirtualCameraBase cam, int priority = PRIORITY_ZONE)
    {
        if (cam != null) cam.Priority = priority;
    }

    public static void Release(CinemachineVirtualCameraBase cam)
    {
        if (cam != null) cam.Priority = PRIORITY_LOW;
    }
}
