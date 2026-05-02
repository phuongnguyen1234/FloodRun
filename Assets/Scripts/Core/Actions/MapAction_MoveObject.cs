using UnityEngine;
using System.Collections;
using DG.Tweening;
using Core.Interfaces;

/// <summary>
/// Hành động di chuyển một đối tượng đến vị trí mới trong một khoảng thời gian nhất định.
/// </summary>
[System.Serializable]
public class MapAction_MoveObject : MapAction
{
    public enum MovementType
    {
        Move,
        Rotate,
        Scale,
        Jump
    }

    [Tooltip("Loại chuyển động mà hành động này sẽ thực hiện.")]
    public MovementType Type = MovementType.Move;

    public Transform Target;

    [Header("Move Settings")]
    public Vector3 MoveDestination;
    public float Duration = 2.0f;
    public bool UseLocalPosition = false;
    public AnimationCurve EaseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Rotate Settings")]
    public Vector3 RotateDestination;
    public RotateMode RotateMode = RotateMode.FastBeyond360;

    [Header("Scale Settings")]
    public Vector3 ScaleDestination = new Vector3(1, 1, 1);

    [Header("Jump Settings")]
    public float JumpPower = 2f;
    public int NumJumps = 1;
    public bool SnapToGround = false;

    public override void Execute(IMapManager manager)
    {
        if (Target != null && manager != null)
        {
            // Dừng mọi tween đang chạy trên Target này để tránh xung đột
            Target.DOKill();

            switch (Type)
            {
                case MovementType.Move:
                    if (UseLocalPosition)
                    {
                        Target.DOLocalMove(MoveDestination, Duration).SetEase(EaseCurve).SetUpdate(UpdateType.Normal);
                    }
                    else
                    {
                        Target.DOMove(MoveDestination, Duration).SetEase(EaseCurve).SetUpdate(UpdateType.Normal);
                    }
                    break;
                case MovementType.Rotate:
                    Target.DORotate(RotateDestination, Duration, RotateMode).SetEase(EaseCurve).SetUpdate(UpdateType.Normal);
                    break;
                case MovementType.Scale:
                    Target.DOScale(ScaleDestination, Duration).SetEase(EaseCurve).SetUpdate(UpdateType.Normal);
                    break;
                case MovementType.Jump:
                    // DOJump luôn dùng World Position
                    Target.DOJump(MoveDestination, JumpPower, NumJumps, Duration, SnapToGround).SetEase(EaseCurve).SetUpdate(UpdateType.Normal);
                    break;
                default:
                    Debug.LogWarning($"[MapAction_MoveObject] Loại chuyển động '{Type}' chưa được xử lý.");
                    break;
            }
        }
    }
}