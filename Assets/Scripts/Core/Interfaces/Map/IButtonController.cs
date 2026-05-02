namespace Core.Interfaces
{
    /// <summary>
    /// Interface giúp các hệ thống khác (như Locator) truy cập thông tin Button mà không bị lỗi Assembly.
    /// </summary>
    public interface IButtonController
    {
        bool IsExplosive { get; }
    }
}