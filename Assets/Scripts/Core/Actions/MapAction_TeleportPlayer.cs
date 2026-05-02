using UnityEngine;
using Core.Interfaces;
using System.Linq;

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

    public override void Execute(IMapManager manager)
    {
        var gameplay = Object.FindObjectsByType<Component>(FindObjectsSortMode.None).OfType<IGameplayManager>().FirstOrDefault();
        if (gameplay == null || Destination == null) return;

        // Lấy danh sách cần teleport
        System.Collections.Generic.List<IPlayer> targets = TeleportAllPlayers 
            ? gameplay.AllPlayers 
            : new System.Collections.Generic.List<IPlayer> { gameplay.LocalPlayer };

        foreach (var player in targets)
        {
            if (player == null) continue;

            GameObject playerObject = player.gameObject;
            if (playerObject.TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.linearVelocity = Vector2.zero;
            }

            playerObject.transform.position = Destination.position;
        }
        Debug.Log($"[Timeline] Teleported {(TeleportAllPlayers ? "all" : "local")} players to {Destination.name}.");
    }
}