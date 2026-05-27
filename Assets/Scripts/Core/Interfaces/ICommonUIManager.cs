using System;
using UnityEngine;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface chung cho tất cả UI managers (SP & MP).
    /// Cung cấp các hành vi UI cơ bản như notification và loading screen.
    /// </summary>
    public interface ICommonUIManager : IUISfxPlayer
    {
        /// <summary>
        /// Hiển thị thông báo float trên màn hình.
        /// </summary>
        void ShowNotification(string message, Color color = default, float duration = 2f);

        /// <summary>
        /// Hiển thị/ẩn loading screen.
        /// </summary>
        void ShowLoadingScreen(bool show);

        /// <summary>
        /// Cấu hình loading screen với dữ liệu map.
        /// </summary>
        void SetupLoadingScreen(MapData data);

        /// <summary>
        /// Hiển thị loading screen khi quay về home/main menu.
        /// </summary>
        void ShowBackToMainMenuLoadingScreen();
    }
}
