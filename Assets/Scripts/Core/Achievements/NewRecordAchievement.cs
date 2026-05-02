using UnityEngine;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu mở khóa ngay khi người chơi phá kỷ lục cá nhân trên một map bất kỳ lần đầu tiên.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRecordAchievement", menuName = "Flood Run/Achievements/New Record")]
    public class NewRecordAchievement : AchievementSO
    {
        public override bool CheckCondition(PlayerProfile profile)
        {
            // Chỉ cần người chơi đã từng phá kỷ lục ít nhất 1 lần (TotalRecordsBroken > 0)
            // Thành tựu này sẽ thỏa mãn điều kiện.
            return profile.TotalRecordsBroken > 0;
        }
    }
}