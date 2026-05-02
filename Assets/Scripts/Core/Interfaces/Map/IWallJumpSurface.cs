namespace Core.Interfaces
{
    /// <summary>
    /// Mở rộng IWallSurface để cung cấp các thuộc tính cụ thể cho Wall Jump.
    /// Điều này cho phép assembly Player tương tác với các thuộc tính của tường mà không phụ thuộc vào assembly Mechanics.
    /// </summary>
    public interface IWallJumpSurface : IWallSurface
    {
        /// <summary>
        /// Tường này có sử dụng bộ đếm thời gian bám không?
        /// </summary>
        bool UseJumpTimer { get; }
        float ClingDuration { get; }
    }
}