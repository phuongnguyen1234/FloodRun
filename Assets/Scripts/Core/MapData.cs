using UnityEngine;

namespace Core
{

/// <summary>
/// Lớp dữ liệu cho từng map, chứa thông tin cơ bản và các tài nguyên liên quan.
/// </summary>
[CreateAssetMenu(fileName = "NewMapData", menuName = "Flood Run/Map Data")]
public class MapData : ScriptableObject
{
    [Header("Basic Info")]
    public string Name;
    public string Author;
    [TextArea(3, 10)]
    public string Description;

    [Header("Difficulty & Stats")]
    [Range(1.0f, 6.9f)]
    [Tooltip("Độ khó của map, thang điểm từ 1.0 đến 6.9")]
    public float Difficulty = 1.0f;
    
    [Tooltip("Thời lượng của map tính bằng giây (dùng để hiển thị hoặc giới hạn thời gian)")]
    public float MapDuration;
    
    [Tooltip("Tổng số nút bấm cần kích hoạt trong màn chơi")]
    public int ButtonNumber;

    [Header("Assets")]
    [Tooltip("Hình ảnh xem trước của map trên menu chọn màn")]
    public Sprite MapPreviewImage;
    
    [Tooltip("Prefab của màn chơi này (thay vì load scene)")]
    public GameObject MapPrefab; 

    [Tooltip("Nhạc nền riêng cho map này")]
    public AudioClip BackgroundMusic;

    [Header("Dev Settings")]
    [Tooltip("Nếu bật, các chức năng DevTool sẽ hiển thị khi chơi map này.")]
    public bool EnableDevTools = false;
}
}