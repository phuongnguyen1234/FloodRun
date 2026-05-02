using UnityEngine;

namespace Core.Interfaces
{
    public interface IHomeUIManager : IUISfxPlayer
    {
        void ShowHomeScreen();
        void ShowMapSelectionScreen();
        void ShowLoadingScreen(bool show);
        void SetupLoadingScreen(MapData data);

        void PlayCustomSound(AudioClip clip);
    }
}