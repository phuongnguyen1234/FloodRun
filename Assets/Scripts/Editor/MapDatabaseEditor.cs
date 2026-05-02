#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using Core;

/// <summary>
/// Custom Editor cho MapDatabase, cho phép tự động tìm và load tất cả MapData trong project vào database chỉ với một nút bấm.
/// </summary>
[CustomEditor(typeof(MapDatabase))]
public class MapDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        MapDatabase database = (MapDatabase)target;

        GUILayout.Space(20);
        if (GUILayout.Button("Auto Load All MapData from Project", GUILayout.Height(40)))
        {
            FindAllMapData(database);
        }
    }

    private void FindAllMapData(MapDatabase database)
    {
        // Tìm tất cả asset có kiểu MapData trong toàn bộ project
        string[] guids = AssetDatabase.FindAssets("t:MapData");
        
        database.AllMaps.Clear();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapData mapData = AssetDatabase.LoadAssetAtPath<MapData>(path);
            if (mapData != null)
            {
                database.AllMaps.Add(mapData);
            }
        }

        // Sắp xếp theo tên cho gọn (Map_01, Map_02...)
        database.AllMaps = database.AllMaps.OrderBy(m => m.Name).ToList();

        EditorUtility.SetDirty(database); // Đánh dấu để Unity lưu file lại
        Debug.Log($"Đã tìm thấy và cập nhật {database.AllMaps.Count} map vào Database!");
    }
}
#endif
