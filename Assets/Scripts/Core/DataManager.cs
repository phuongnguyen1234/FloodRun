using UnityEngine;
using Core.Interfaces;

namespace Core
{
    /// <summary>
    /// Quản lý dữ liệu người chơi tập trung dưới dạng Singleton tồn tại xuyên suốt các Scene.
    /// </summary>
    public class DataManager : MonoBehaviour, IDataManager
    {
        public static DataManager Instance { get; private set; }

        public PlayerProfile Profile { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadData();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Load dữ liệu từ file vào bộ nhớ. Gọi khi bắt đầu game.
        /// </summary>
        public void LoadData()
        {
            Profile = SaveSystem.LoadProfile();
        }

        /// <summary>
        /// Ghi dữ liệu từ bộ nhớ xuống file.
        /// </summary>
        public void SaveData()
        {
            if (Profile != null)
            {
                SaveSystem.SaveProfile(Profile);
            }
        }
    }
}