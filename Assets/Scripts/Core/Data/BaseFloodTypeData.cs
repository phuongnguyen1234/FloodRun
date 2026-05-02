using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// Lớp ScriptableObject cơ sở cho dữ liệu của Flood.
    /// Nó tồn tại trong assembly Core để các assembly khác (như Map)
    /// có thể giữ một tham chiếu đến nó mà không cần phụ thuộc vào assembly Mechanics.
    /// </summary>
    public abstract class BaseFloodTypeData : ScriptableObject
    {
        // Lớp này được cố ý để trống.
    }
}