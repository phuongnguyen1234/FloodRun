using UnityEngine;
using Core.Interfaces;

/// <summary>
/// Timeline action để log một message ra console, giúp debug hoặc thông báo gì đó trong quá trình timeline chạy.
/// </summary>
[System.Serializable]
public class MapAction_Log : MapAction
{
    public string Message;
    public override void Execute(IMapManager manager)
    {
        Debug.Log($"<color=yellow>[Timeline]</color> {Message}");
    }
}