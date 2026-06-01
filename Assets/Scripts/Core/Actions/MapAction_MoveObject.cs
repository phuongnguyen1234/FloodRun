using UnityEngine;
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

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        if (Target != null && manager != null)
        {
            // Dừng mọi tween đang chạy trên Target này để tránh xung đột
            Target.DOKill();
            Tween tween = null;

            switch (Type)
            {
                case MovementType.Move:
                    tween = Target.DOLocalMove(MoveDestination, Duration).SetEase(EaseCurve);
                    break;
                case MovementType.Rotate:
                    tween = Target.DORotate(RotateDestination, Duration, RotateMode).SetEase(EaseCurve);
                    break;
                case MovementType.Scale:
                    tween = Target.DOScale(ScaleDestination, Duration).SetEase(EaseCurve);
                    break;
                case MovementType.Jump:
                    tween = Target.DOLocalJump(MoveDestination, JumpPower, NumJumps, Duration, SnapToGround).SetEase(EaseCurve);
                    break;
            }

            if (tween != null)
            {
                tween.SetUpdate(UpdateType.Normal);
                // CẢI TIẾN: Nếu tham gia muộn, nhảy đến vị trí hiện tại của vật thể dựa trên thời gian trôi qua
                // Bỏ qua nếu thời gian quá nhỏ để tránh lỗi khởi tạo tween của DOTween ở frame đầu tiên
                if (elapsedTime > 0.1f) 
                {
                    tween.ForceInit(); // Bắt buộc DOTween lưu giá trị khởi tạo trước khi nhảy
                    tween.Goto(elapsedTime, true);
                }
            }
        }
    }
}