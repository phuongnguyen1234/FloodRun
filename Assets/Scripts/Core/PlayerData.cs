using System;
using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// Lớp dữ liệu lưu trữ thông tin về người chơi, bao gồm tên, số trận thắng, kỷ lục thời gian theo từng map, và thống kê theo từng Tier độ khó.
    /// </summary>
    [Serializable]
    public class MapRecord
    {
        public string MapName; // Dùng Name từ MapData làm khóa
        public float BestTime;
        public int WinCount;
    }

    /// <summary>
    /// Lớp dữ liệu thống kê số trận thắng theo từng Tier độ khó, giúp hiển thị thông tin chi tiết hơn về thành tích của người chơi.
    /// </summary>
    [Serializable]
    public class TierStat
    {
        public DifficultyPalette.Tier Tier;
        public int TotalWins;
        public int UniqueWins;
    }

    /// <summary>
    /// Lớp dữ liệu chính để lưu trữ thông tin về người chơi, bao gồm tên, số trận thắng, 
    /// kỷ lục thời gian theo từng map, và thống kê theo từng Tier độ khó. 
    /// Dữ liệu này sẽ được lưu vào file JSON và tải lại khi cần thiết.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public string PlayerID = Guid.NewGuid().ToString(); // ID duy nhất cho profile, tạo mới nếu chưa có
        public string PlayerName = "Noob";
        public int TotalWinsCount = 0; // Tổng số trận thắng (bao gồm chơi lại)
        public int TotalCoins = 0; // Tổng số xu hiện có

        public int TotalButtonsPressed = 0;
        public int TotalDeathCount = 0;
        public int CurrentMultiplayerWinStreak = 0;
        public int MaxMultiplayerWinStreak = 0;
        public int TotalRecordsBroken = 0; // Thống kê số lần phá kỷ lục trên map cũ
        
        // Property để lấy nhanh số lượng map duy nhất đã thắng
        public int UniqueWinsCount => MapRecords.Count;
        
        // Danh sách các kỷ lục theo từng map
        public List<MapRecord> MapRecords = new List<MapRecord>();

        // Thống kê số trận thắng theo từng Tier từ DifficultyPalette
        public List<TierStat> TierWins = new List<TierStat>();

        // Danh sách ID các thành tựu đã mở khóa
        public List<string> UnlockedAchievementIDs = new List<string>();

        public void IncrementWin(DifficultyPalette.Tier tier, bool isFirstWin, int earnedCoins)
        {
            TotalWinsCount++;
            TotalCoins += earnedCoins;

            TierStat stat = TierWins.Find(s => s.Tier == tier);
            if (stat == null)
            {
                stat = new TierStat { Tier = tier, TotalWins = 1, UniqueWins = isFirstWin ? 1 : 0 };
                TierWins.Add(stat);
            }
            else
            {
                stat.TotalWins++;
                if (isFirstWin) stat.UniqueWins++;
            }
        }

        /// <summary>
        /// Reset toàn bộ thông số về mặc định.
        /// </summary>
        public void ResetToDefault()
        {
            PlayerName = "Noob";
            TotalWinsCount = 0;
            TotalCoins = 0;
            TotalButtonsPressed = 0;
            TotalDeathCount = 0;
            CurrentMultiplayerWinStreak = 0;
            MaxMultiplayerWinStreak = 0;
            TotalRecordsBroken = 0;
            
            MapRecords.Clear();
            TierWins.Clear();
            UnlockedAchievementIDs.Clear();
        }

        public void RegisterDeath()
        {
            TotalDeathCount++;
        }

        public void RegisterButtonPress()
        {
            TotalButtonsPressed++;
        }

        public void RegisterMultiplayerWin()
        {
            CurrentMultiplayerWinStreak++;
            if (CurrentMultiplayerWinStreak > MaxMultiplayerWinStreak)
                MaxMultiplayerWinStreak = CurrentMultiplayerWinStreak;
        }

        public void ResetMultiplayerStreak()
        {
            CurrentMultiplayerWinStreak = 0;
        }

        public bool IsAchievementUnlocked(string id)
        {
            return UnlockedAchievementIDs.Contains(id);
        }
    }
}