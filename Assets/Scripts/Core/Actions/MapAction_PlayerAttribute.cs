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

        // Truy cập trực tiếp qua interface IPlayer (đã được refactor để chứa các setter)
        switch (Command)
        {
            case PlayerCommand.SetMoveSpeed:
                targetPlayer.SetSpeed(Value);
                break;
            case PlayerCommand.SetJumpForce:
                targetPlayer.SetJumpForce(Value);
                break;
            case PlayerCommand.SetGravityScale:
                targetPlayer.SetGravityScale(Value);
                break;
            case PlayerCommand.ResetGravityScale:
                targetPlayer.ResetGravityScale();
                break;
            case PlayerCommand.SetMaxAir:
                targetPlayer.SetMaxAir(Value);
                break;
            case PlayerCommand.AddAir:
                targetPlayer.AddAir(Value);
                break;
        }
    }
}