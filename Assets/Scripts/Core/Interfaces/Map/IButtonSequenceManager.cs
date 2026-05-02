using UnityEngine.Events;
using UnityEngine;
using System.Collections.Generic;

namespace Core.Interfaces
{
    public interface IButtonSequenceManager
    {
        UnityEvent OnSequenceComplete { get; }

        // Public getters cho UI
        int TotalButtons { get; }

        int CurrentIndex { get; }

        Transform GetCurrentButtonTransform();
        void TriggerCurrentButton();
        List<Transform> GetRemainingButtonTransforms();
    }
}