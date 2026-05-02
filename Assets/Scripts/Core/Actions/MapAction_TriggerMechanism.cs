using UnityEngine;
using Core.Interfaces;

[System.Serializable]
public class MapAction_TriggerMechanism : MapAction
{
    public IInteractable TargetMechanism;

    public override void Execute(IMapManager manager)
    {
        if (TargetMechanism != null)
        {
            TargetMechanism.Interact();
        }
    }
}
