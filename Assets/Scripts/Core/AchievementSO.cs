using UnityEngine;

namespace Core
{
    /// <summary>
    /// Lớp cơ sở cho các thành tựu. Sử dụng ScriptableObject để dễ dàng tạo mới trong Inspector.
    /// </summary>
    public abstract class AchievementSO : ScriptableObject
    {
        [Header("Basic Info")]
        public string ID; // Định danh duy nhất (ví dụ: "win_10_games")
        public string Title;
        [TextArea] public string Description;
        public Sprite Icon;
        public int CoinReward;

        /// <summary>
        /// Kiểm tra xem người chơi đã đạt đủ điều kiện chưa.
        /// Mỗi loại thành tựu sẽ tự định nghĩa logic này.
        /// </summary>
        public abstract bool CheckCondition(PlayerProfile profile);
    }
}