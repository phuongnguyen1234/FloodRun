using UnityEngine;
using Unity.Cinemachine;
using Core.Interfaces;
using System.Linq;

/// <summary>
/// Helper tổng quát cho các vùng thay đổi Camera.
/// Tự động chiếm cam khi Local Player đi vào và trả cam khi đi ra.
/// </summary>
public class MapZone_CameraControl : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private CinemachineVirtualCameraBase _zoneCamera;
    [SerializeField] private int _activePriority = 20;

    private IGameplayManager _gameplayManager;

    private void Awake()
    {
        _gameplayManager = FindObjectsByType<Component>().OfType<IGameplayManager>().FirstOrDefault();
        if (_gameplayManager == null)
        {
            Debug.LogError("[MapZone_CameraControl] IGameplayManager not found in scene!");
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
        {
            CameraSystemHelper.SetActive(_zoneCamera, _activePriority);
            Debug.Log($"[CameraZone] Switched to {_zoneCamera.name}.");
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
            CameraSystemHelper.Release(_zoneCamera);
    }

    private bool IsLocalPlayer(Collider2D other)
    {
        if (_gameplayManager == null) return false;
        IPlayer player = other.GetComponentInParent<IPlayer>();
        return player != null && player == _gameplayManager.LocalPlayer;
    }
}