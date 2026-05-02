namespace Core.Interfaces
{
    /// <summary>
    /// Interface để các hệ thống bên ngoài (như MapManager) có thể thay đổi thuộc tính của PlayerMotor.
    /// </summary>
    public interface IPlayerMotorAttributes
    {
        void SetSpeed(float newSpeed);
        void SetJumpForce(float newJumpForce);
        void SetGravityScale(float newGravityScale);
        void ResetGravityScale();
    }
}