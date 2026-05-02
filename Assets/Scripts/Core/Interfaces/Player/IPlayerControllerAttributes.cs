namespace Core.Interfaces
{
    /// <summary>
    /// Interface để các hệ thống bên ngoài (như MapManager) có thể thay đổi thuộc tính của PlayerController (Air, Health...).
    /// </summary>
    public interface IPlayerControllerAttributes
    {
        void SetMaxAir(float newMaxAir);
        void AddAir(float amount);
    }
}