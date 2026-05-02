using UnityEngine;
using System.Collections;
using Core.Interfaces;

/// <summary>
/// Class cơ sở cho mọi hành động trong Map Timeline.
/// Sử dụng [SerializeReference] trong MapManager để hiển thị list các action này.
/// </summary>
[System.Serializable]
public abstract class MapAction
{
    [Tooltip("Mô tả ngắn gọn hành động này làm gì (để dễ quản lý)")]
    public string Description;

    [Tooltip("Thời gian chờ trước khi thực thi hành động này (nếu cần)")]
    public float Delay = 0f;

    /// <summary>
    /// Hàm thực thi logic chính.
    /// </summary>
    /// <param name="manager">Tham chiếu đến MapManager để dùng Coroutine hoặc truy cập global state.</param>
    public abstract void Execute(IMapManager manager);

    /// <summary>
    /// Wrapper để hỗ trợ delay tự động.
    /// </summary>
    public IEnumerator ExecuteRoutine(IMapManager manager)
    {
        if (Delay > 0) yield return new WaitForSeconds(Delay);
        Execute(manager);
    }
}