namespace Core.Interfaces
{
    public interface IAchievementManager
    {
        /// <summary>
        /// Kiểm tra và mở khóa các thành tựu dựa trên profile hiện tại.
        /// </summary>
        void CheckAndUnlock();
    }
}