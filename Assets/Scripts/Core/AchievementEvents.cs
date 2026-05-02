using System;

namespace Core.Events
{
    /// <summary>
    /// Định nghĩa các sự kiện liên quan đến thành tích (achievement) trong trò chơi.
    /// </summary>
    public static class AchievementEvents
    {
        public static event Action<AchievementSO> OnAchievementUnlocked;

        public static void TriggerAchievementUnlocked(AchievementSO achievement)
        {
            OnAchievementUnlocked?.Invoke(achievement);
        }
    }
}