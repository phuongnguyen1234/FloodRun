using UnityEngine;
using Unity.Cinemachine;

namespace Core
{
    /// <summary>
    /// Helper class cung cấp các tiện ích xử lý Camera cho cả Singleplayer và Multiplayer.
    /// </summary>
    public static class CameraHelper
    {
        /// <summary>
        /// Dịch chuyển camera tức thời đến một vị trí cụ thể, giữ nguyên độ sâu Z của camera.
        /// </summary>
        public static void WarpToTarget(CinemachineCamera vcam, Vector3 targetPosition)
        {
            if (vcam == null) return;

            Vector3 pos = targetPosition;
            pos.z = vcam.transform.position.z;
            vcam.ForceCameraPosition(pos, vcam.transform.rotation);
        }

        /// <summary>
        /// Dịch chuyển camera tức thời đến một Component (Player/Object).
        /// </summary>
        public static void WarpToTarget(CinemachineCamera vcam, Component target)
        {
            if (target != null) WarpToTarget(vcam, target.transform.position);
        }
    }
}