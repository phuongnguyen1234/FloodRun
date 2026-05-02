using System.IO;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Lớp tĩnh để quản lý việc lưu và tải dữ liệu người chơi. 
    /// Dữ liệu được lưu dưới dạng JSON trong thư mục persistentDataPath của ứng dụng, đảm bảo tính bền vững qua các phiên chơi khác nhau.
    /// </summary>
    public static class SaveSystem
    {
        private static string SavePath => Path.Combine(Application.persistentDataPath, "player_profile.json");

        public static void SaveProfile(PlayerProfile profile)
        {
            string json = JsonUtility.ToJson(profile, true);
            File.WriteAllText(SavePath, json);
        }

        public static void DeleteProfile()
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }

        public static PlayerProfile LoadProfile()
        {
            if (!File.Exists(SavePath))
            {
                return new PlayerProfile(); // Trả về profile mới nếu chưa có file
            }

            try
            {
                string json = File.ReadAllText(SavePath);
                return JsonUtility.FromJson<PlayerProfile>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to load profile: {e.Message}");
                return new PlayerProfile();
            }
        }
    }
}