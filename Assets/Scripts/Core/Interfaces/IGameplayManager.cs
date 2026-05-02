using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface cung cấp quyền truy cập vào các trạng thái global của màn chơi.
    /// </summary>
    public interface IGameplayManager
    {
        IPlayer LocalPlayer { get; }
        List<IPlayer> AllPlayers { get; }

        bool IsPaused { get; }
        bool IsGameActive { get; }
    }
}