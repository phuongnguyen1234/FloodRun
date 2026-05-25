using UnityEngine;
using System.Collections;

/// <summary>
/// Gắn script này vào Map Prefab để thay đổi thông số Player riêng cho Map này.
/// </summary>
public class MapCustomModifier : MonoBehaviour
{
    [Header("Tùy chỉnh thông số cho Map này")]
    [Tooltip("Lực nhảy mới. Khuyên dùng: 15 đến 18")]
    [SerializeField] private float _customJumpForce = 15f;

    private IEnumerator Start()
    {
        // Đợi 0.5 giây để đảm bảo GameplayManager đã Instantiate Player ra Scene xong
        yield return new WaitForSeconds(0.5f);

        // Quét tìm tất cả các Player đang có trên Scene (hỗ trợ cả Multiplayer nếu có)
        PlayerMotor[] allPlayers = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
        
        foreach (var player in allPlayers)
        {
            // Gọi hàm SetJumpForce mà tác giả PlayerMotor đã mở sẵn
            player.SetJumpForce(_customJumpForce);
        }

        Debug.Log($"[MapCustomModifier] Đã kích hoạt lực nhảy đặc biệt ({_customJumpForce}) cho Map này!");
    }
}