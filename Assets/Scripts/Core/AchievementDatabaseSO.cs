using UnityEngine;
using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// ScriptableObject này chứa tất cả các AchievementSO của game. Nó sẽ được sử dụng để truy cập và quản lý các thành tích trong trò chơi.
    /// </summary>
    [CreateAssetMenu(fileName = "AchievementDatabase", menuName = "Flood Run/Achievements/Database")]
    public class AchievementDatabaseSO : ScriptableObject
    {
        [Tooltip("Kéo tất cả các AchievementSO (Win Map, N nút, Chet N lần...) vào đây.")]
        public List<AchievementSO> AllAchievements;
    }
}