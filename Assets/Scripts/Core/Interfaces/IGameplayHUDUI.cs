namespace Core.Interfaces
{
    /// <summary>
    /// Interface chung cho UI HUD khi gameplay diễn ra (SP & MP).
    /// Chứa các hành vi liên quan đến thông tin gameplay thực tế (timer, air, buttons, etc.).
    /// </summary>
    public interface IGameplayHUDUI : ICommonUIManager
    {
        /// <summary>
        /// Cập nhật thời gian cá nhân của player hiện tại.
        /// </summary>
        void UpdatePersonalRecord(float time);

        /// <summary>
        /// Đặt kỷ lục thời gian (chỉ SP).
        /// </summary>
        void SetRecordTime(float time);

        /// <summary>
        /// Cập nhật trạng thái hệ thống không khí (air, bonus, rate).
        /// </summary>
        void UpdateAirUI(float currentAir, float bonusAir, float bonusMax, float rate);

        /// <summary>
        /// Cập nhật tiến độ nút bấm (hiện tại / tổng).
        /// </summary>
        void UpdateButtonProgress(int current, int total);

        /// <summary>
        /// Cập nhật số lượng player còn sống sót.
        /// </summary>
        void UpdateAlivePlayerCount(int current, int total);

        /// <summary>
        /// Hiển thị/ẩn cờ hoàn thành cho người chơi cục bộ.
        /// </summary>
        void ShowPlayerFinishFlag(bool show);

        /// <summary>
        /// Cập nhật text đếm ngược (vote map, map timer, etc.).
        /// </summary>
        void SetCountdownText(string text);

        /// <summary>
        /// Cập nhật slider thời gian (cho vote map hoặc map timer).
        /// </summary>
        void UpdateTimeSlider(float currentTime, float maxTime, bool isVotePhase = false);

        /// <summary>
        /// Hiển thị/ẩn cờ hoàn thành cho nút bấm cuối cùng
        /// </summary>
        /// <param name="show"></param>
        void ShowButtonFinishFlag(bool show);


    }
}
