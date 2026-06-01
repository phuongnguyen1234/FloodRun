using UnityEngine;
using Core.Interfaces;
using Core;
using System.Linq;
using Unity.Cinemachine;

/// <summary>
/// Action để dịch chuyển Player đến một vị trí cụ thể.
/// </summary>
[System.Serializable]
public class MapAction_TeleportPlayer : MapAction
{
    [Tooltip("Vị trí đích mà Player sẽ được dịch chuyển tới. Kéo một Empty GameObject vào đây để xác định vị trí.")]
    public Transform Destination;
    [Tooltip("Nếu bật, sẽ dịch chuyển toàn bộ player trong danh sách AllPlayers thay vì chỉ Local Player.")]
    public bool TeleportAllPlayers = false;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        var gameplay = Object.FindObjectsByType<Component>().OfType<IGameLoopManager>().FirstOrDefault();
        if (gameplay == null || Destination == null) return;

        // Lấy danh sách cần teleport
        System.Collections.Generic.List<IPlayer> targets = TeleportAllPlayers 
            ? gameplay.AllPlayers 
            : new System.Collections.Generic.List<IPlayer> { gameplay.LocalPlayer };

        foreach (var player in targets)
        {
            if (player == null) continue;

            player.Teleport(Destination.position);

            // Nếu player được dịch chuyển là Local Player, ta cần warp camera theo tức thời
            if (player == gameplay.LocalPlayer)
            {
                var vcam = Object.FindAnyObjectByType<CinemachineCamera>();
                CameraHelper.WarpToTarget(vcam, Destination.position);
            }
        }
        Debug.Log($"[Timeline] Teleported {(TeleportAllPlayers ? "all" : "local")} players to {Destination.name}.");
    }
}