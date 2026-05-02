using UnityEngine;
using System.Collections.Generic;
using Core.Interfaces;
using Core.Events;

namespace Core.Achievements
{
    /// <summary>
    /// Manager chịu trách nhiệm kiểm tra và mở khóa thành tựu dựa trên các sự kiện gameplay.
    /// </summary>
    public class AchievementManager : MonoBehaviour, IAchievementManager
    {
        public static AchievementManager Instance { get; private set; }

        [SerializeField] private AchievementDatabaseSO _database;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Giữ manager qua các scene
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            // Lắng nghe các sự kiện gameplay để tự động kiểm tra thành tựu
            GameplayEvents.OnPlayerDied += CheckAndUnlock;
            GameplayEvents.OnButtonPressed += CheckAndUnlock;
            // Ở đây ta dùng lambda để bỏ qua tham số MonoBehaviour của OnLevelCompleted
            GameplayEvents.OnLevelCompleted += (ctx) => CheckAndUnlock();
        }

        private void OnDisable()
        {
            GameplayEvents.OnPlayerDied -= CheckAndUnlock;
            GameplayEvents.OnButtonPressed -= CheckAndUnlock;
            // Lưu ý: Lambda không dễ Unsubscribe, nhưng vì Manager là DontDestroyOnLoad 
            // nên nó sẽ tồn tại suốt vòng đời app.
        }

        /// <summary>
        /// Quét toàn bộ database để kiểm tra những thành tựu chưa mở khóa.
        /// </summary>
        public void CheckAndUnlock()
        {
            PlayerProfile profile = DataManager.Instance.Profile;
            bool hasNewUnlock = false;

            foreach (var achievement in _database.AllAchievements)
            {
                if (achievement == null || profile.IsAchievementUnlocked(achievement.ID))
                    continue;

                if (achievement.CheckCondition(profile))
                {
                    Unlock(achievement, profile);
                    hasNewUnlock = true;
                }
            }

            if (hasNewUnlock) DataManager.Instance.SaveData();
        }

        protected virtual void Unlock(AchievementSO achievement, PlayerProfile profile)
        {
            profile.UnlockedAchievementIDs.Add(achievement.ID);
            profile.TotalCoins += achievement.CoinReward;
                        
            // Bắn event cho UI Popup lắng nghe
            AchievementEvents.TriggerAchievementUnlocked(achievement);
        }
    }
}