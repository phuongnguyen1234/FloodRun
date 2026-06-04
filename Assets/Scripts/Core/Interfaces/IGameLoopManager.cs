using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// Base interface cho tất cả game loop managers (Single Player & Multiplayer).
    /// Cung cấp thông tin cơ bản về trạng thái gameplay mà các mechanics có thể phụ thuộc vào.
    /// </summary>
    public interface IGameLoopManager
    {
        /// <summary>
        /// Player cục bộ trong bối cảnh hiện tại (SP hoặc MP).
        /// </summary>
        IPlayer LocalPlayer { get; }

        /// <summary>
        /// Tất cả players trong game (bao gồm local + remote trong MP).
        /// </summary>
        List<IPlayer> AllPlayers { get; }

        /// <summary>
        /// Gameplay có đang hoạt động không (phân biệt Lobby/Playing states).
        /// </summary>
        bool IsGameActive { get; }

        /// <summary>
        /// Game có đang bị tạm dừng không.
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// Kiểm tra xem client hiện tại có phải là host/server không.
        /// Trong SP: luôn true. Trong MP: true nếu là host.
        /// </summary>
        bool IsHost { get; }

        /// <summary>
        /// Kiểm tra xem có phải chế độ multiplayer không.
        /// </summary>
        bool IsMultiplayer { get; }

        IMapManager CurrentMapManager { get; }
    }
}
