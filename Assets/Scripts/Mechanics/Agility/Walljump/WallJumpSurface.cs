using UnityEngine;
using Core.Interfaces;

/// <summary>
/// Lớp này đại diện cho một bề mặt có thể thực hiện wall jump.
/// </summary>
public class WallJumpSurface : MonoBehaviour, IWallJumpSurface
{
    [Header("Jump Timer")]
    [SerializeField] private bool _useJumpTimer = false;
    [Tooltip("Thời gian bám tường trước khi tự động nhảy ra.")]
    [SerializeField] private float _clingDuration = 1.5f;

    // Interface Implementation
    public bool UseJumpTimer => _useJumpTimer;
    public float ClingDuration => _clingDuration;
}