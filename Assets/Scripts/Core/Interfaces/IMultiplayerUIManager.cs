using System;
using System.Collections.Generic;
using UnityEngine;
using Core;
using static Core.DifficultyPalette;

namespace Core.Interfaces
{
    /// <summary>
    /// Dữ liệu kết quả của một ván chơi trong chuỗi ván đấu.
    /// </summary>
    public struct RoundSummaryData
    {
        public string MapName;
        public Tier Tier;
        public int Rank;
        public int ButtonsPressed;
        public float FinishTime;
        public Sprite MapPreviewSprite;
        public int CoinsEarned;
        public bool IsWin;
    }

    /// <summary>
    /// Interface cho UI Manager của Multiplayer.
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

        /// <summary>
        /// Mở modal vote map khi host bắt đầu giai đoạn vote.
        /// </summary>
        void OpenVotingModal();

        /// <summary>
        /// Bật/Tắt hiển thị nút mở Modal Vote.
        /// </summary>
        void SetVotingButtonVisible(bool visible);

        /// <summary>
        /// Làm sạch các yếu tố Gameplay HUD (lá cờ, đếm ngược, màu sắc thời gian) để chuẩn bị cho round mới.
        /// </summary>
        void ResetGameplayHUD();

        /// <summary>
        /// Cập nhật text hiển thị khi đang chờ player khác đang load map.
        /// </summary>
        /// <param name="text"></param>
        void SetWaitingForPlayersText(string text);

        /// <summary>
        /// Cập nhật thông tin bản đồ trên bảng thông tin trong Lobby (map name, preview image, difficulty color, etc.).
        /// </summary>
        /// <param name="mapData"></param>
        /// <param name="difficulty"></param>
        void UpdateLobbyWorldMapInfo(MapData mapData, float difficulty);

        /// <summary>
        /// Kiểm tra xem có bất kỳ Modal nào đang mở hay không (để quyết định có nên khóa input của player hay không).
        /// </summary>    
        bool IsAnyModalOpen();

        /// <summary>
        /// Chỉ cập nhật phần độ khó trên bảng Lobby mà không ảnh hưởng đến tên hay ảnh map. 
        /// Thường được gọi khi kết thúc round để chỉ thay đổi độ khó cho round tiếp theo mà giữ nguyên thông tin map đã chọn.
        /// </summary>
        /// <param name="newVal"></param>
        void UpdateLobbyDifficultyOnly(float newVal);

        /// <summary>
        /// Hiển thị bảng tổng hợp kết quả sau khi kết thúc một chuỗi chơi (khi chết).
        /// </summary>
        void ShowSummary(List<RoundSummaryData> results);
    }
}