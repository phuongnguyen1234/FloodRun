using System;
using UnityEngine;
using System.Collections.Generic;
using Core.Interfaces;

namespace Core.Events
{
    /// <summary>
    /// Lớp tĩnh quản lý các sự kiện chung của gameplay.
    /// Giúp các assembly khác nhau giao tiếp mà không phụ thuộc trực tiếp vào nhau.
    /// </summary>
    public static class GameplayEvents
    {
        // Sự kiện được bắn khi player hoàn thành level (đi vào ExitRegion hợp lệ)
        public static event Action<IPlayer> OnLevelCompleted;
        
        // Sự kiện yêu cầu bật/tắt Infinite Air từ DevTool
        public static event Action OnInfiniteAirToggleRequested;

        // Sự kiện yêu cầu bật/tắt Infinite Jump từ DevTool
        public static event Action OnInfiniteJumpToggleRequested;

        // Sự kiện yêu cầu dịch chuyển đến nút tiếp theo
        public static event Action OnTeleportToNextButtonRequested;

        // Sự kiện yêu cầu bật/tắt chế độ Teleport tự do (Click to teleport)
        public static event Action OnTeleportModeToggleRequested;

        // Sự kiện yêu cầu dừng tất cả Timeline của Map
        public static event Action OnHaltTimelinesRequested;

        // Sự kiện yêu cầu tạm dừng hoặc tiếp tục game
        public static event Action<bool> OnPauseRequested;
        public static event Action OnRestartRequested;
        public static event Action OnBackToMenuRequested;
        // Sự kiện khi người chơi tử trận
        public static event Action OnPlayerDied;

        // Sự kiện khi một nút bất kỳ được nhấn
        public static event Action OnButtonPressed;

        // Sự kiện khi Local Player được sinh ra và sẵn sàng
        public static event Action<IPlayer> OnLocalPlayerSpawned;

        // Sự kiện khi bất kỳ người chơi nào (Local hoặc Remote) gia nhập Scene
        public static event Action<IPlayer> OnPlayerJoined;

        // Sự kiện khi một người chơi thoát/despawn khỏi mạng
        public static event Action<IPlayer> OnPlayerLeft;

        public static void TriggerLevelCompleted(IPlayer playerWhoCompleted)
        {
            OnLevelCompleted?.Invoke(playerWhoCompleted);
        }

        public static void TriggerInfiniteAirToggle()
        {
            OnInfiniteAirToggleRequested?.Invoke();
        }

        public static void TriggerInfiniteJumpToggle()
        {
            OnInfiniteJumpToggleRequested?.Invoke();
        }

        public static void TriggerTeleportToNextButton()
        {
            OnTeleportToNextButtonRequested?.Invoke();
        }

        public static void TriggerTeleportModeToggle()
        {
            OnTeleportModeToggleRequested?.Invoke();
        }

        public static void TriggerHaltTimelines()
        {
            OnHaltTimelinesRequested?.Invoke();
        }

        public static void TriggerPauseRequest(bool v)
        {
            OnPauseRequested?.Invoke(v);
        }

        public static void TriggerRestartRequested()
        {
            OnRestartRequested?.Invoke();
        }

        public static void TriggerBackToMenuRequested()
        {
            OnBackToMenuRequested?.Invoke();
        }
        public static void TriggerPlayerDied()
        {
            OnPlayerDied?.Invoke();
        }

        public static void TriggerButtonPressed()
        {
            OnButtonPressed?.Invoke();
        }

        public static void TriggerLocalPlayerSpawned(IPlayer player)
        {
            OnLocalPlayerSpawned?.Invoke(player);
        }

        public static void TriggerPlayerJoined(IPlayer player)
        {
            OnPlayerJoined?.Invoke(player);
        }

        public static void TriggerPlayerLeft(IPlayer player)
        {
            OnPlayerLeft?.Invoke(player);
        }
    }
}