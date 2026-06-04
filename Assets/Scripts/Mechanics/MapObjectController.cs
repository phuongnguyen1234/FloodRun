using UnityEngine;
using Core.Interfaces;

/// <summary>
/// Component tổng quát gắn vào các vật thể trong Map để nhận lệnh từ Actions thông qua ID.
/// </summary>
public class MapObjectController : MonoBehaviour, IMapCommandHandler
{
    [Header("Identification")]
    [Tooltip("ID dùng để các Action tìm thấy object này. Nhiều object có thể chung ID để điều khiển cùng lúc.")]
    public string ObjectID;

    private IMapManager _mapManager;

    private void OnEnable()
    {
        RegisterWithMap();
    }

    private void OnDisable()
    {
        _mapManager?.UnregisterMapObject(ObjectID, this);
        _mapManager = null;
    }

    private void RegisterWithMap()
    {
        _mapManager?.UnregisterMapObject(ObjectID, this);

        _mapManager = GetComponentInParent<IMapManager>();
        if (_mapManager == null)
        {
            var mapRoot = transform.root;
            if (mapRoot != null)
                _mapManager = mapRoot.GetComponentInChildren<IMapManager>();
        }

        _mapManager?.RegisterMapObject(ObjectID, this);
    }

    public void HandleCommand(string command)
    {
        if (command == "Show") gameObject.SetActive(true);
        if (command == "Hide") gameObject.SetActive(false);

        var handlers = GetComponents<IMapCommandHandler>();
        foreach (var h in handlers)
        {
            if (h != (IMapCommandHandler)this) h.HandleCommand(command);
        }
    }
}
