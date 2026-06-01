using Core.Data;

namespace Core.Interfaces
{
    public interface IFloodManager
    {
        void StartFlood();
        void SyncToMapTime();
        void ChangeFloodType(BaseFloodTypeData newType);
        void StopFlood();
        void PauseFlood(float duration);
        void AdjustFloodPosition(float offset); // Hàm duy nhất để tiến/lùi dọc quỹ đạo
    }
}