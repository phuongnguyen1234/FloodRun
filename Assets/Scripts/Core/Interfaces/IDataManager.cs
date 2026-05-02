namespace Core.Interfaces
{
    /// <summary>
    /// Interface cung cấp quyền truy cập vào dữ liệu người chơi mà không phụ thuộc vào class cụ thể.
    /// </summary>
    public interface IDataManager
    {
        PlayerProfile Profile { get; }
        
        void LoadData();
        
        void SaveData();
    }
}