using Core;
using UnityEngine;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface định nghĩa các hành vi của một UI Manager cho màn chơi.
    /// Giúp GameplayManager không phụ thuộc trực tiếp vào một class cụ thể.
    /// </summary>
    public interface IGameplayUIManager : IUISfxPlayer
    {
        void UpdatePersonalTime(float time);
        void SetMaxTime(float time);
        void SetRecordTime(float time);
        void ShowNotification(string message, Color color, float duration = 2f);
        void SetCountdownText(string text);
        void UpdateAirUI(float currentAir, float bonusAir, float bonusMax, float rate);
        void UpdateButtonProgress(int current, int total);
        void ShowEndGame(bool isVictory, string reason, MapData data, float time, int buttons, bool isNewBest, int coinsEarned);
        void ShowLoadingScreen(bool show);
        void ShowBackToHomeLoadingScreen();
        void SetupLoadingScreen(MapData data);
        void ShowDevTools(bool show);
        void ShowPauseMenu(bool show);
        void UpdateInfiniteAirStatus(bool isOn);
        void UpdateInfiniteJumpStatus(bool isOn);
        void UpdateTeleportModeStatus(bool isOn);
        void UpdateHaltTimelinesStatus(bool isHalted);
        void UpdateLevelProgress(float progress);
    }
}