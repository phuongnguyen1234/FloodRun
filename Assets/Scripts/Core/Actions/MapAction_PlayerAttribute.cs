using UnityEngine;
using Core.Interfaces;
using System.Linq;

/// <summary>
/// Action để thay đổi các thuộc tính của Player như tốc độ, máu (air), v.v.
/// </summary>
[System.Serializable]
public class MapAction_PlayerAttribute : MapAction
{
    public enum PlayerCommand
    {
        SetMoveSpeed,
        SetJumpForce,
        SetGravityScale,
        ResetGravityScale,
        SetMaxAir,
        AddAir,
    }

    [Tooltip("Hành động muốn thực hiện trên Player.")]
    public PlayerCommand Command;

    [Tooltip("Giá trị số sẽ được sử dụng cho hành động. Không áp dụng cho ResetGravityScale.")]
    public float Value;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        // Tìm GameLoopManager thông qua Interface để lấy LocalPlayer
        var gameplay = Object.FindObjectsByType<Component>().OfType<IGameLoopManager>().FirstOrDefault();
        IPlayer targetPlayer = gameplay?.LocalPlayer;

        if (targetPlayer == null)
        {
            Debug.LogWarning($"[Timeline Action] Player Attribute: Không tìm thấy Player để thực hiện action '{Description}'.");
            return;
        }

        GameObject playerObject = targetPlayer.gameObject;
        if (playerObject != null)
        {
            playerObject.TryGetComponent(out IPlayerMotorAttributes motor);
            playerObject.TryGetComponent(out IPlayerControllerAttributes controller);

            switch (Command)
            {
                case PlayerCommand.SetMoveSpeed:
                    motor?.SetSpeed(Value);
                    break;
                case PlayerCommand.SetJumpForce:
                    motor?.SetJumpForce(Value);
                    break;
                case PlayerCommand.SetGravityScale:
                    motor?.SetGravityScale(Value);
                    break;
                case PlayerCommand.ResetGravityScale:
                    motor?.ResetGravityScale();
                    break;
                case PlayerCommand.SetMaxAir:
                    controller?.SetMaxAir(Value);
                    break;
                case PlayerCommand.AddAir:
                    controller?.AddAir(Value);
                    break;
            }
        }
    }
}