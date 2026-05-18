using UnityEngine;
using Unity.Cinemachine; 

namespace Core
{
    /// <summary>
    /// Lớp này sẽ được gắn vào Camera chính của bạn. Nó có nhiệm vụ nhận thông tin về target từ MapManager 
    /// và cập nhật Follow của CinemachineCamera hoặc CinemachineVirtualCamera tương ứng.
    /// </summary>
    public class CinemachineTargetSetter : MonoBehaviour
    {
        // Hàm này sẽ được gọi từ MapManager
        public void SetCameraTarget(Transform target)
        {
            // Code cho Unity 6 (Cinemachine v3)
            var cam = GetComponent<CinemachineCamera>();
            if (cam != null)
            {
                cam.Follow = target;
            }
        }
    }
}