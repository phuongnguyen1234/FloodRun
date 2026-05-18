#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Core;

    /// <summary>
    /// Tiện ích Editor để quản lý dữ liệu lưu trữ mà không cần chạy game.
    /// </summary>
    public static class DataUtility
    {
        [MenuItem("Tools/Data/Reset All Player Data")]
        public static void ResetPlayerData()
        {
            // Hiển thị hộp thoại xác nhận để tránh bấm nhầm
            if (EditorUtility.DisplayDialog("Reset Player Data",
                "Bạn có chắc chắn muốn xóa toàn bộ dữ liệu người chơi và thiết lập? Hành động này không thể hoàn tác.",
                "Xóa hết", "Hủy"))
            {
                SaveSystem.DeleteProfile();
                PlayerPrefs.DeleteAll(); // Xóa cả âm lượng, phím bấm trong SettingsManager
                
                Debug.Log("<color=red><b>[DataUtility]</b> Đã xóa sạch file save và PlayerPrefs.</color>");
            }
        }

        [MenuItem("Tools/Data/Open Save Folder")]
        public static void OpenSaveFolder()
        {
            // Mở thư mục chứa file save trong File Explorer để kiểm tra thủ công
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }
    }
#endif