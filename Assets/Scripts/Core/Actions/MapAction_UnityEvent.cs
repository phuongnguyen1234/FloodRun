using UnityEngine;
using UnityEngine.Events;
using Core.Interfaces;

/// <summary>
/// Action tổng quát nhất, cho phép gọi bất kỳ hàm public nào của các Component khác 
/// thông qua UnityEvent trực tiếp trên Inspector.
/// </summary>
[System.Serializable]
public class MapAction_UnityEvent : MapAction
{
    [Tooltip("Sử dụng danh sách này để kéo các Component/Hàm bạn muốn thực thi.")]
    public UnityEvent Event;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        if (Event != null)
        {
            Event.Invoke();
        }
    }
}