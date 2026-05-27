using UnityEngine;
using System.Linq;
using Core;

namespace Multiplayer
{
    public static class MultiplayerDevFix
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            // Kiểm tra xem có tham số -isClientInstance được truyền từ Editor script không
            string[] args = System.Environment.GetCommandLineArgs();
            if (args.Contains("-isClientInstance"))
            {
                Debug.Log("<color=cyan>[MultiplayerDevFix]</color> detected Client Instance. Adjusting profile...");
                
                // Đợi DataManager khởi tạo xong rồi ghi đè tên để tránh trùng lặp với Editor (Host)
                if (DataManager.Instance != null && DataManager.Instance.Profile != null)
                {
                    DataManager.Instance.Profile.PlayerName += "_Client";
                    Debug.Log($"[MultiplayerDevFix] Player name changed to: {DataManager.Instance.Profile.PlayerName}");
                }
                else
                {
                    // Nếu tại thời điểm này DataManager vẫn chưa sẵn sàng (hiếm gặp ở AfterSceneLoad), 
                    // ta có thể dùng PlayerPrefs tạm thời hoặc đợi thêm 1 chút.
                    Debug.LogWarning("[MultiplayerDevFix] DataManager not ready yet. Name will be updated on next access.");
                }

                // Mẹo thêm: Giảm độ phân giải của bản EXE xuống để dễ nhìn cả 2 cửa sổ
                Screen.SetResolution(800, 450, false);
                Application.runInBackground = true; // Đảm bảo EXE không bị đứng khi bạn click vào Unity Editor
            }
        }
    }
}