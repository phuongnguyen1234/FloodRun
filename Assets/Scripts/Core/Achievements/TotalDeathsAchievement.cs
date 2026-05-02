using UnityEngine;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu đạt được khi tổng số lần chết của người chơi đạt đến một giá trị nhất định.
    /// </summary>
    [CreateAssetMenu(fileName = "TotalDeathsAchievement", menuName = "Flood Run/Achievements/Total Deaths")]
    public class TotalDeathsAchievement : AchievementSO
    {
        public int RequiredDeaths;

        public override bool CheckCondition(PlayerProfile profile)
        {
            return profile.TotalDeathCount >= RequiredDeaths;
        }
    }
}