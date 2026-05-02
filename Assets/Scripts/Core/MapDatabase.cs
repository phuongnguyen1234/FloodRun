using UnityEngine;
using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// ScriptableObject để lưu trữ dữ liệu tất cả các map trong game.
    /// Tạo menu để click phải -> Create -> Flood Run -> Map Database
    /// </summary>
    [CreateAssetMenu(fileName = "MapDatabase", menuName = "Flood Run/Map Database")]
    public class MapDatabase : ScriptableObject
    {
        public List<MapData> AllMaps;

        // Hàm tiện ích để lấy map theo index (an toàn)
        public MapData GetMapByIndex(int index)
        {
            if (AllMaps != null && index >= 0 && index < AllMaps.Count)
            {
                return AllMaps[index];
            }
            return null;
        }

        public int Count => AllMaps != null ? AllMaps.Count : 0;
    }
}

