using Core;
using UnityEngine;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface định nghĩa các hành vi UI của Single Player.
    /// Extends IGameplayHUDUI để sử dụng chung các hành vi gameplay HUD.
    /// Thêm các hành vi SP-specific như ShowEndGame, ShowPauseMenu, dev tools, etc.
    /// </summary>
    public interface ISingleplayerUIManager : IGameplayHUDUI
    {
        /// <summary>
        /// Hiển thị kết thúc game (SP-specific).
        /// </summary>
        void ShowEndGame(bool isVictory, string reason, MapData data, float time, int buttons, bool isNewBest, int coinsEarned);

        /// <summary>
        /// Hiển thị/ẩn dev tools (SP-specific).
        /// </summary>
        void ShowDevTools(bool show);

        /// <summary>
        /// Hiển thị/ẩn pause menu (SP-specific).
        /// </summary>
        void ShowPauseMenu(bool show);

        /// <summary>
        /// Cập nhật trạng thái infinite air (dev tool).
        /// </summary>
        void UpdateInfiniteAirStatus(bool isOn);

        /// <summary>
        /// Cập nhật trạng thái infinite jump (dev tool).
        /// </summary>
        void UpdateInfiniteJumpStatus(bool isOn);

        /// <summary>
        /// Cập nhật trạng thái teleport mode (dev tool).
        /// </summary>
        void UpdateTeleportModeStatus(bool isOn);

        /// <summary>
        /// Cập nhật trạng thái halt timelines (dev tool).
        /// </summary>
        void UpdateHaltTimelinesStatus(bool isHalted);

        /// <summary>
        /// Cập nhật tiến độ level (SP-specific).
        /// </summary>
        void UpdateLevelProgress(float progress);
    }
}