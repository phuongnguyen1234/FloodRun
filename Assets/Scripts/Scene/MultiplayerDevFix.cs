using UnityEngine;
using System.Linq;
using Core;
using System.Runtime.InteropServices;
using System;

namespace Multiplayer
{
    /// <summary>
    /// Hỗ trợ fix các lỗi về độ phân giải, tên player và chạy nền khi test Multiplayer cục bộ.
    /// </summary>
    public class MultiplayerDevFix : MonoBehaviour
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

                // 1. Thiết lập mặc định chuẩn 1280x720 ở chế độ cửa sổ.
                // Windowed mode giúp giữ thanh tiêu đề và không đè lên Taskbar để dễ dàng test nhiều cửa sổ.
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                
                // 2. Cho phép chạy nền
                Application.runInBackground = true;

                // 3. Tạo một Object ẩn để quản lý tỉ lệ khung hình khi người dùng chuyển sang chế độ cửa sổ
                GameObject fixer = new("MultiplayerDev_RatioFixer");
                DontDestroyOnLoad(fixer);
                fixer.hideFlags = HideFlags.HideAndDontSave;
                fixer.AddComponent<WindowRatioEnforcer>();
            }
        }
    }

    /// <summary>
    /// Component nội bộ giúp ép tỉ lệ 16:9 khi resize cửa sổ.
    /// </summary>
    internal class WindowRatioEnforcer : MonoBehaviour
    {
        #region Win32 API
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_THICKFRAME = 0x00040000; // Viền cho phép resize
        #endregion

        private void Start()
        {
            // FIX: Vô hiệu hóa nút Maximize và khả năng kéo giãn cửa sổ để giữ cố định 1280x720
            IntPtr hWnd = GetActiveWindow();
            int style = GetWindowLong(hWnd, GWL_STYLE);
            
            // Sử dụng toán tử Bitwise AND NOT để loại bỏ các thuộc tính resizable
            SetWindowLong(hWnd, GWL_STYLE, style & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME);
        }
    }
}