using UnityEngine;
using System;

namespace Core.Interfaces
{
    public interface IHomeUIManager : IUISfxPlayer
    {
        void ShowHomeScreen();
        void ShowMapSelectionScreen();
        void ShowLoadingScreen(bool show);
        void SetupLoadingScreen(MapData data);
        void ShowNotification(string message, Action onClose = null);
        void AskConfirmation(string message, Action onYes);

        void PlayCustomSound(AudioClip clip);
    }
}