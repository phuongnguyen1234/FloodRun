using System;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface cho UI Manager của Multiplayer.
    /// Extends IGameplayHUDUI để sử dụng chung các hành vi gameplay HUD.
    /// Thêm các hành vi MP-specific như chat, room info, spectate, etc.
    /// </summary>
    public interface IMultiplayerUIManager : IGameplayHUDUI
    {
        /// <summary>
        /// Hiển thị/ẩn hộp chat.
        /// </summary>
        void ShowChat(bool show);

        /// <summary>
        /// Toggle đóng/mở hộp chat.
        /// </summary>
        void ToggleChat();

        /// <summary>
        /// Thêm tin nhắn mới vào chat.
        /// </summary>
        void AddChatMessage(string sender, string message, bool isHost);

        /// <summary>
        /// Hiển thị/ẩn modal thông tin phòng.
        /// </summary>
        void ShowRoomInfo(bool show);

        /// <summary>
        /// Hiển thị/ẩn settings modal.
        /// </summary>
        void ShowSettings(bool show);

        /// <summary>
        /// Hỏi xác nhận trước khi thực hiện hành động (e.g., leave room).
        /// </summary>
        void AskConfirmation(string message, Action onYes);

        /// <summary>
        /// Hiển thị loading screen khi joining room.
        /// </summary>
        void ShowJoiningLoadingScreen(bool show);

        /// <summary>
        /// Chuyển chế độ HUD giữa Lobby và Gameplay.
        /// </summary>
        void SetHUDMode(bool isGameplay);

        /// <summary>
        /// Cập nhật trạng thái Active/AFK của player.
        /// </summary>
        void UpdatePlayStatus(bool isAFK);

        /// <summary>
        /// Cập nhật trạng thái spectate của player.
        /// </summary>
        void UpdateSpectateStatus(bool isSpectating);

        /// <summary>
        /// Hiển thị modal thông báo với một nút "Close" (dùng cho trường hợp bị disconnect, kicked, etc.).
        /// </summary>
        /// <param name="message"></param>
        /// <param name="onClose"></param>
        void ShowNotificationModal(string message, Action onClose);
    }
}