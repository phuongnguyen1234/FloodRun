using UnityEngine;
using System.Linq;

namespace Core.Achievements
{
    /// <summary>
    /// Thành tựu win map cụ thể. Người chơi cần phải thắng ít nhất một lần trên bản đồ được chỉ định để đạt được thành tựu này.
    /// </summary>
    [CreateAssetMenu(fileName = "MapWinAchievement", menuName = "Flood Run/Achievements/Win Specific Map")]
    public class MapWinAchievement : AchievementSO
    {
        public MapData TargetMap;

        public override bool CheckCondition(PlayerProfile profile)
        {
            if (TargetMap == null) return false; // Đảm bảo TargetMap đã được gán
            var record = profile.MapRecords.FirstOrDefault(r => r.MapName == TargetMap.Name);
            return record != null && record.WinCount > 0;
        }
    }
}