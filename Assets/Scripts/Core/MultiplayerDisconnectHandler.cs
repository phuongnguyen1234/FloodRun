using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Core.Interfaces;
using System.Linq;
using System;

namespace Core
{
    /// <summary>
    /// Chuyên trách xử lý việc ngắt kết nối đột ngột trong Multiplayer.
    /// Tách biệt logic này giúp MultiplayerManager tập trung vào Game Loop.
    /// </summary>
    public class MultiplayerDisconnectHandler : MonoBehaviour
    {
        private bool _isReturningToHome = false;

        private void Awake()
        {
            // Subscribe ở Awake để chắc chắn callback được attach trước khi scene unload
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                Debug.Log("[MultiplayerDisconnectHandler] Subscribed to disconnect callback");
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            // Chúng ta chỉ quan tâm nếu CHÍNH MÁY NÀY bị ngắt kết nối
            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[DisconnectHandler] NetworkManager is null, cannot check LocalClientId");
                HandleReturnToHome("Disconnected from server.");
                return;
            }

            if (clientId != NetworkManager.Singleton.LocalClientId) return;

            // Tránh việc xử lý trùng lặp nếu nhiều sự kiện bắn ra cùng lúc
            if (_isReturningToHome) return;
            _isReturningToHome = true;

            // NOTE: Netcode không cung cấp enum lý do disconnect, nên không thể phân biệt
            // giữa "kicked", "host left", hay "network error"
            // Thay vì hiển thị message sai, sử dụng thông báo chung
            string reason = "You have been disconnected from the room.";

            Debug.Log($"[DisconnectHandler] Client disconnected: {reason}");
            HandleReturnToHome(reason);
        }

        private void HandleReturnToHome(string message)
        {
            Debug.Log($"[DisconnectHandler] Returning to Home. Reason: {message}");
            
            // Tìm UI Manager thông qua Interface vì Handler này nằm ở Core
            // Sử dụng FindAnyObjectByType để tìm Instance đang hoạt động trong Scene hiện tại
            var uiManager = FindObjectsByType<MonoBehaviour>().OfType<IMultiplayerUIManager>().FirstOrDefault();

            if (uiManager != null)
            {
                // Hiển thị NotificationModal với thông báo và hành động quay về Home khi đóng modal
                uiManager.ShowNotificationModal(message, () => ForceReturnToHome());
            }
            else
            {
                // Nếu không tìm thấy UI, thực hiện dọn dẹp và về Home ngay lập tức
                ForceReturnToHome();
            }
        }

        private void ForceReturnToHome()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Home");
        }
    }
}
