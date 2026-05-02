using System;
using UnityEngine.SceneManagement;

namespace Core
{
    /// <summary>
    /// Quản lý dữ liệu và trạng thái liên quan đến level/map đang chơi.
    /// </summary>
    public static class LevelManager
    {
        // Dữ liệu map được chọn để truyền từ Home sang Gameplay
        public static MapData SelectedMap { get; set; }
        
        public static Action OnLevelLoadStarted;

        // Cờ hiệu để biết HomeUIManager nên hiển thị màn hình nào khi vừa Load xong
        public static bool ReturnToMapSelection { get; set; } = false;
    }
}