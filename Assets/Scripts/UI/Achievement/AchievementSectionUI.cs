using UnityEngine;
using Core;
using System.Collections.Generic;

namespace UI
{
    /// <summary>
    /// Quản lý hiển thị danh sách Achievement trong Progress screen.
    /// </summary>
    public class AchievementSectionUI : MonoBehaviour
    {
        [Header("Databases")]
        [SerializeField] private AchievementDatabaseSO _database;

        [Header("UI Components")]
        [SerializeField] private AchievementCardView _cardPrefab;
        [SerializeField] private Transform _contentParent; // Thường là Content của Scroll View

        private void OnEnable()
        {
            RefreshList();
        }

        public void RefreshList()
        {
            // 1. Xóa các card cũ để tránh trùng lặp
            foreach (Transform child in _contentParent)
            {
                Destroy(child.gameObject);
            }

            if (_database == null || _cardPrefab == null || _contentParent == null) return;

            // 2. Lấy profile người chơi từ DataManager (chứa danh sách UnlockedAchievementIDs)
            PlayerProfile profile = DataManager.Instance.Profile;

            // 3. Duyệt qua database và tạo Card
            foreach (var achievement in _database.AllAchievements)
            {
                if (achievement == null) continue;

                AchievementCardView card = Instantiate(_cardPrefab, _contentParent);
                bool isUnlocked = profile.IsAchievementUnlocked(achievement.ID);
                
                card.Setup(achievement, isUnlocked);
            }
        }
    }
}
