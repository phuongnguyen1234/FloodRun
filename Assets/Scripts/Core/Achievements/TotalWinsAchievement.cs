using UnityEngine;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu đạt được khi người chơi có tổng số chiến thắng vượt qua một ngưỡng nhất định.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTotalWinsAchievement", menuName = "Flood Run/Achievements/Total Wins")]
    public class TotalWinsAchievement : AchievementSO
    {
        public int RequiredWins;

        public override bool CheckCondition(PlayerProfile profile)
        {
            return profile.TotalWinsCount >= RequiredWins;
        }
    }
}