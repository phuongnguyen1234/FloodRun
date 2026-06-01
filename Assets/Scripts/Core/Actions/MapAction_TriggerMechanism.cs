using UnityEngine;
using Core.Interfaces;

[System.Serializable]
public class MapAction_TriggerMechanism : MapAction
{
    public IInteractable TargetMechanism;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        if (TargetMechanism != null)
        {
            TargetMechanism.Interact();
        }
    }
}
