using UnityEngine.InputSystem;

namespace Core.Interfaces
{
    /// <summary>
    /// Interface cung cấp quyền truy cập vào Input Action Asset của người chơi.
    /// Giúp UI có thể rebind phím mà không cần tham chiếu trực tiếp tới Player Assembly.
    /// </summary>
    public interface IInputProvider
    {
        InputActionAsset ActionAsset { get; }
    }
}