using UnityEngine;
using Core.Interfaces;
using Core.Data;

/// <summary>
/// Timeline action để điều khiển hệ thống Flood Control trong game.
/// </summary>
[System.Serializable]
public class MapAction_FloodControl : MapAction
{
    public enum CommandType { StartSequence, ChangeType, Stop, Pause, AdjustPosition }

    [Tooltip("Kéo GameObject chứa FloodController vào đây.")]
    public GameObject TargetFloodObject;
    public CommandType Command;

    [Tooltip("Dùng cho lệnh 'ChangeType'")]
    public BaseFloodTypeData NewType;
    [Tooltip("Dùng cho lệnh 'Pause'")]
    public float duration;
    [Tooltip("Dùng cho lệnh 'AdjustPosition'")]
    public float offset;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        if (TargetFloodObject == null)
        {
            Debug.LogWarning($"[Timeline Action] Flood Control: Chưa gán TargetFloodObject cho action '{Description}'.");
            return;
        }

        // Lấy component qua interface để không phụ thuộc vào assembly Mechanics
        IFloodManager floodManager = TargetFloodObject.GetComponent<IFloodManager>();
        if (floodManager == null) return;

        switch (Command)
        {
            case CommandType.StartSequence:
                floodManager.StartFlood();
                break;
            case CommandType.ChangeType:
                if (NewType != null) floodManager.ChangeFloodType(NewType);
                break;
            case CommandType.Stop:
                floodManager.StopFlood();
                break;
            case CommandType.Pause:
                // CẢI TIẾN: Tính toán lại thời gian chờ dựa trên elapsedTime (dành cho người join muộn)
                float actualDuration = duration - elapsedTime;
                if (actualDuration > 0) {
                    floodManager.PauseFlood(actualDuration);
                }
                break;
            case CommandType.AdjustPosition:
                floodManager.AdjustFloodPosition(offset);
                break;
        }
    }
}