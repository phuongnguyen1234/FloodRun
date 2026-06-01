using UnityEngine;
using Core.Interfaces;

/// <summary>
/// Action gửi một chuỗi lệnh (string) tới các object qua ID. 
/// Object nhận được sẽ tự quyết định làm gì (đổi màu, bật đèn, phát nhạc...).
/// </summary>
[System.Serializable]
public class MapAction_GenericCommand : MapAction
{
    public string TargetID;
    public string Command;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        var targets = manager.GetMapObjectsByID<IMapCommandHandler>(TargetID);
        foreach (var t in targets)
        {
            t.HandleCommand(Command);
        }
        Debug.Log($"[MapAction] Sent Command '{Command}' to ID '{TargetID}' ({targets.Count} objects)");
    }
}