using UnityEngine;
using TMPro;
using Unity.Netcode;

/// <summary>
/// AirBubble là một Mechanics đơn giản cho phép Player thu thập để bổ sung Air.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class AirBubble : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _airAmount = 300f; // Lượng air bổ sung
    [SerializeField] private AudioClip _pickupSound;

    [Header("Visuals")]
    [Tooltip("Text hiển thị lượng Air (Object con)")]
    [SerializeField] private TMP_Text _amountText;

    private bool _isCollected = false; // Cờ để chống gọi lại nhiều lần trong 1 frame

    private void Start()
    {
        // Cấu hình Collider là Trigger
        GetComponent<CircleCollider2D>().isTrigger = true;

        UpdateVisuals();
    }

    private void OnValidate()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_amountText != null) _amountText.text = $"{_airAmount:F0}";
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Nếu đã thu thập rồi thì không xử lý nữa
        if (_isCollected) return;

        // Chỉ cho phép Local Player (người chơi tại máy này) nhặt bubble
        // Điều này giúp mỗi player trong multiplayer đều có thể tự nhặt bubble của riêng mình
        if (other.GetComponentInParent<NetworkBehaviour>() is NetworkBehaviour nb && nb.IsSpawned && !nb.IsLocalPlayer)
            return;

        // Thay vì tìm PlayerController, ta tìm bất kỳ ai có interface IAirRefillable
        // Điều này giúp Mechanics không phụ thuộc vào Player (Decoupling)
        var target = other.GetComponentInParent<IAirRefillable>();

        if (target != null)
        {
            Collect(target);
        }
    }

    private void Collect(IAirRefillable target)
    {
        // Chỉ hủy bubble nếu Player chấp nhận nhận air (AddBonusAir trả về true)
        // Nếu Player từ chối (do đang có bubble to hơn), bubble sẽ giữ nguyên
        if (target.AddBonusAir(_airAmount))
        {
            _isCollected = true;
            target.PlaySound(_pickupSound);
            Destroy(gameObject);
        }
    }
}