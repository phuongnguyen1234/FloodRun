using UnityEngine;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu đạt được khi người chơi nhấn tổng cộng một số nút nhất định trong suốt quá trình chơi.
    /// </summary>
    [CreateAssetMenu(fileName = "TotalButtonsAchievement", menuName = "Flood Run/Achievements/Total Buttons")]
    public class TotalButtonsAchievement : AchievementSO
    {
        public int RequiredButtons;

        public override bool CheckCondition(PlayerProfile profile)
        {
            return profile.TotalButtonsPressed >= RequiredButtons;
        }
    }
}