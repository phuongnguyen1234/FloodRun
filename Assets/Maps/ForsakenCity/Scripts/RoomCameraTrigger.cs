using UnityEngine;
using Unity.Cinemachine; 
using Core.Interfaces;
using Unity.Netcode; 

[RequireComponent(typeof(Collider2D))]
public class RoomCameraTrigger : MonoBehaviour
{
    [SerializeField] private CinemachineCamera _roomCamera;
    [SerializeField] private int _activePriority = 20;
    [SerializeField] private bool _isStartingRoom = false;

    private static RoomCameraTrigger _currentActiveRoom;
    private IGameLoopManager _gameLoopManager;

    private void Start()
    {
        if (_roomCamera != null) _roomCamera.Priority = 0;

        var monos = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var mono in monos)
        {
            if (mono is IGameLoopManager glManager)
            {
                _gameLoopManager = glManager;
                break;
            }
        }

        if (_isStartingRoom)
        {
            ActivateRoom();
        }
    }

    private void Update()
    {
        if (_currentActiveRoom != this) return;
        if (_gameLoopManager == null || _gameLoopManager.LocalPlayer == null) return;

        IPlayer localPlayer = _gameLoopManager.LocalPlayer;

        // Nếu Player chết hoặc về Lobby -> Nhả quyền kiểm soát camera
        if (localPlayer.IsDead)
        {
            DeactivateRoom();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CheckAndActivate(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        CheckAndActivate(other);
    }

    private void CheckAndActivate(Collider2D other)
    {
        if (_currentActiveRoom == this) return;
        
        if (_gameLoopManager == null || _gameLoopManager.LocalPlayer == null) return;

        IPlayer player = GetPlayer(other);
        
        if (player != null && player == _gameLoopManager.LocalPlayer)
        {
            // Chỉ lock camera lại khi trạng thái của player là hợp lệ (đang InGame)
            if (!player.IsDead && 
                player.Status.Value != PlayerStatus.Lobby && 
                player.Status.Value != PlayerStatus.Dead)
            {
                ActivateRoom();
            }
        }
    }

    private void ActivateRoom()
    {
        if (_currentActiveRoom != null && _currentActiveRoom._roomCamera != null)
        {
            _currentActiveRoom._roomCamera.Priority = 0;
        }

        _currentActiveRoom = this;
        if (_roomCamera != null)
        {
            _roomCamera.Priority = _activePriority;
        }
    }

    private void DeactivateRoom()
    {
        if (_roomCamera != null)
        {
            _roomCamera.Priority = 0;
        }
        
        if (_currentActiveRoom == this)
        {
            _currentActiveRoom = null;
        }
    }

    private void OnDestroy()
    {
        if (_currentActiveRoom == this) 
        {
            _currentActiveRoom = null;
        }
    }

    private IPlayer GetPlayer(Collider2D col)
    {
        IPlayer p = col.GetComponentInParent<IPlayer>();
        if (p != null) return p;

        if (col.attachedRigidbody != null)
        {
            p = col.attachedRigidbody.GetComponent<IPlayer>();
            if (p != null) return p;
        }

        return col.transform.root.GetComponent<IPlayer>();
    }
}