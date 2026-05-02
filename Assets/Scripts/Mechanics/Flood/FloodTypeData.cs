using UnityEngine;
using Core.Data;

/// <summary>
/// ScriptableObject để định nghĩa các thuộc tính của một loại Flood.
/// Tạo các asset từ menu: Assets > Create > Flood > Flood Type Data.
/// </summary>
[CreateAssetMenu(fileName = "New Flood Type", menuName = "Flood Run/Flood Type Data")]
public class FloodTypeData : BaseFloodTypeData
{
    [Tooltip("Loại flood (dùng cho logic đặc biệt như Lava).")]
    public FloodType Type;

    [Tooltip("Sprite cho bề mặt của flood.")]
    public Sprite FloodSprite;

    [Tooltip("Âm thanh phát ra khi người chơi vào/ra khỏi flood này.")]
    public AudioClip SplashSound;

    [Tooltip("Tốc độ mất air mỗi giây. Dương = mất khí, 0 = không mất (Safe), Âm = hồi phục (Healing).")]
    public float AirDrainRate;

    [Tooltip("Màu của flood (tùy chọn, có thể dùng cho Gizmos hoặc shader).")]
    public Color FloodColor = Color.blue;

    [Header("Advanced Settings")]
    [Tooltip("Nếu true, Player sẽ đi bộ bình thường bên trong vùng này thay vì bơi (VD: Vùng khí độc).")]
    public bool NoSwim = false;

    [Tooltip("Nếu true, tốc độ mất khí sẽ tăng lên dựa theo độ sâu của Player.")]
    public bool ApplyDepthMultiplier = false;

    [Tooltip("Hệ số nhân theo độ sâu. Công thức: Rate = BaseRate * (1 + Depth * Multiplier).")]
    public float DepthMultiplierFactor = 0.5f;

    [Header("Dynamic Transparency Settings")]
    [Tooltip("Nếu true, khi Player bơi bên trong flood này, nó sẽ tự động giảm alpha để dễ nhìn thấy nhân vật.")]
    public bool FadeAlphaOnSwim = true;
    [Range(0, 1)]
    public float TargetSwimAlpha = 0.5f;
    public float AlphaTransitionSpeed = 1.5f;

    // Event để thông báo cho các Controller đồng bộ hóa khi dữ liệu asset thay đổi
    public static System.Action OnDataChanged;

    private void OnValidate()
    {
        OnDataChanged?.Invoke();
    }
}