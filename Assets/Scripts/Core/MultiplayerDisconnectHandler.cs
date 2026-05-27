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
        private bool _isSubscribed = false;
        private IMultiplayerUIManager _uiManager;

        private void Awake()
        {
            // Subscribe ở Awake để chắc chắn callback được attach trước khi scene unload
            TrySubscribe();
        }

        private void Start()
        {
            // In case NetworkManager.Singleton wasn't ready at Awake, ensure subscription later
            if (!_isSubscribed)
            {
                StartCoroutine(WaitAndSubscribe());
            }

            // Cache UI Manager sớm để tránh việc tìm kiếm lặp lại khi xảy ra sự cố ngắt kết nối
            if (_uiManager == null)
            {
                _uiManager = FindObjectsByType<MonoBehaviour>().OfType<IMultiplayerUIManager>().FirstOrDefault();
            }
        }

        private System.Collections.IEnumerator WaitAndSubscribe()
        {
            float timeout = 5f; // avoid waiting forever
            float t = 0f;
            while (!_isSubscribed && t < timeout)
            {
                TrySubscribe();
                if (_isSubscribed) break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        private void TrySubscribe()
        {
            if (_isSubscribed) return;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                _isSubscribed = true;
                Debug.Log("[MultiplayerDisconnectHandler] Subscribed to disconnect callback");
            }
        }

        private void OnDestroy()
        {
            // Kiểm tra Singleton một cách an toàn để tránh cảnh báo khi thoát app
            if (_isSubscribed && NetworkManager.Singleton != null && NetworkManager.Singleton.gameObject != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
                _isSubscribed = false;
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
            
            // Kiểm tra fallback nếu cache bị null
            if (_uiManager == null)
                _uiManager = FindObjectsByType<MonoBehaviour>().OfType<IMultiplayerUIManager>().FirstOrDefault();

            if (_uiManager != null)
            {
                // YÊU CẦU 2: Hiển thị đồng thời màn hình loading đen và modal thông báo.
                // Màn hình loading sẽ che đi các xử lý dọn dẹp scene phía sau.
                _uiManager.ShowBackToMainMenuLoadingScreen();
                _uiManager.ShowNotificationModal(message, () => ForceReturnToHome());
            }
            else
            {
                // Nếu không tìm thấy UI, thực hiện dọn dẹp và về Home ngay lập tức
                ForceReturnToHome();
            }
        }

        private void ForceReturnToHome()
        {
            // Đã hiện loading screen ở bước trước, giờ chỉ thực hiện shutdown và chuyển scene
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Home");
        }
    }
}
