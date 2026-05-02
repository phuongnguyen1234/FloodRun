using UnityEngine;
using System;

namespace Core
{
    /// <summary>
    /// ScriptableObject để quản lý bảng màu theo từng Tier độ khó
    /// </summary>
    [CreateAssetMenu(fileName = "DifficultyPalette", menuName = "Flood Run/Difficulty Palette")]
    public class DifficultyPalette : ScriptableObject
    {
        // Chuyển Enum vào đây để đóng gói dữ liệu tốt hơn
        public enum Tier
        {
            Easy,
            Normal,
            Hard,
            Insane,
            Crazy,
            CrazyPlus
        }

        [Serializable]
        public struct TierColor
        {
            public Tier Tier;
            public Color Color;
        }

        [Header("Color Settings")]
        public Color DefaultColor = new Color(141f / 255f, 141f / 255f, 141f / 255f, 1f);
        public TierColor[] Colors;

        public Color GetColor(Tier tier)
        {
            foreach (var item in Colors)
            {
                if (item.Tier == tier) return item.Color;
            }
            return DefaultColor;
        }

        /// <summary>
        /// Chuyển đổi từ float rating sang Enum (Dùng cho logic phân loại)
        /// </summary>
        public Tier GetTierFromRating(float rating)
        {
            if (rating < 2f) return Tier.Easy;      // 1.0 - 1.9
            if (rating < 3f) return Tier.Normal;    // 2.0 - 2.9
            if (rating < 4f) return Tier.Hard;      // 3.0 - 3.9
            if (rating < 5f) return Tier.Insane;    // 4.0 - 4.9
            if (rating < 6f) return Tier.Crazy;     // 5.0 - 5.9
            return Tier.CrazyPlus;                  // 6.0 - 6.9
        }

        // Helper để lấy màu nhanh từ rating
        public Color GetColorFromRating(float rating)
        {
            return GetColor(GetTierFromRating(rating));
        }

        /// <summary>
        /// Trả về số lượng xu thưởng cơ bản cho từng Tier
        /// </summary>
        public int GetRewardForTier(Tier tier)
        {
            return tier switch
            {
                Tier.Easy => 50,
                Tier.Normal => 100,
                Tier.Hard => 150,
                Tier.Insane => 200,
                Tier.Crazy => 300,
                Tier.CrazyPlus => 400,
                _ => 0
            };
        }
    }
}