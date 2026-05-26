using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface dành riêng cho Single Player gameplay.
    /// Extends IGameLoopManager để cung cấp các tính năng cơ bản, 
    /// đồng thời có thể thêm các method SP-specific ở đây.
    /// </summary>
    public interface IGameplayManager : IGameLoopManager
    {
        // Hiện tại không có thêm method nào, nhưng có thể mở rộng sau này
        // Ví dụ: void RestartLevel(), void BackToMainMenu()...
    }
}