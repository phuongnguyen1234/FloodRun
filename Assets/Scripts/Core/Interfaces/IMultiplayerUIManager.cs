using System;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface cho các thành phần UI đặc thù của Multiplayer.
    /// </summary>
    public interface IMultiplayerUIManager : IUISfxPlayer
    {
        void ShowChat(bool show);
        void ToggleChat();
        void AddChatMessage(string sender, string message, bool isHost);
        void UpdateAlivePlayerCount(int current, int total);

        void ShowRoomInfo(bool show);
        void ShowSettings(bool show);
        void ShowNotification(string message, Action onClose = null);
        void AskConfirmation(string message, Action onYes);

        void ShowLoadingScreen(bool show);
        void ShowJoiningLoadingScreen(bool show);
        void ShowBackToMainMenuLoadingScreen();
        void SetupLoadingScreen(MapData data);
    }
}