using UnityEngine;
using System.Linq;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu mở khóa khi người chơi thắng một map thuộc một mức độ (Tier) cụ thể trở lên lần đầu tiên.
    /// </summary>
    [CreateAssetMenu(fileName = "FirstTierWinAchievement", menuName = "Flood Run/Achievements/First Tier Win")]
    public class FirstTierWinAchievement : AchievementSO
    {
        [Tooltip("Mức độ tối thiểu để đạt thành tựu này (ví dụ: Insane)")]
        public DifficultyPalette.Tier MinimumTier;

        public override bool CheckCondition(PlayerProfile profile)
        {
            // Kiểm tra trong danh sách thống kê TierWins, có Tier nào lớn hơn hoặc bằng MinimumTier 
            // mà số lượng map thắng duy nhất (UniqueWins) > 0 hay không.
            return profile.TierWins.Any(stat => stat.Tier >= MinimumTier && stat.UniqueWins > 0);
        }
    }
}