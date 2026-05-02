using UnityEngine;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu đạt được khi người chơi có chuỗi thắng liên tiếp trong chế độ nhiều người chơi đạt đến một số nhất định.
    /// </summary>
    [CreateAssetMenu(fileName = "WinStreakAchievement", menuName = "Flood Run/Achievements/Win Streak")]
    public class WinStreakAchievement : AchievementSO
    {
        public int RequiredStreak;

        public override bool CheckCondition(PlayerProfile profile)
        {
            return profile.MaxMultiplayerWinStreak >= RequiredStreak;
        }
    }
}