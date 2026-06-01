#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Text;
using Mechanics;
public class HierarchyPrinter
{
    // Thêm menu item vào chuột phải Hierarchy hoặc menu GameObject trên thanh công cụ
    [MenuItem("GameObject/Flood Run Tools/Print Hierarchy", false, 0)]
    static void PrintHierarchy()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("Hãy chọn GameObject gốc của Map trước!");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"--- CẤU TRÚC CỦA '{go.name}' ---");
        Traverse(go.transform, "", sb);
        sb.AppendLine("--------------------------------");
        
        Debug.Log(sb.ToString());
    }

    static void Traverse(Transform current, string indent, StringBuilder sb)
    {
        // Kiểm tra xem object này có script quan trọng nào không để hiển thị kèm
        string components = "";
        if (current.GetComponent<MapManager>()) components += "[MapManager] ";
        if (current.GetComponent<PlayerSpawn>()) components += "[Spawn] ";
        if (current.GetComponent<FloodController>()) components += "[Flood] ";
        if (current.GetComponent<ParallaxEffect>()) components += "[Parallax] ";
        if (current.GetComponent<Canvas>()) components += "[Canvas] ";

        // Kiểm tra xem có phải là Prefab con không
        string prefabStatus = PrefabUtility.IsPartOfAnyPrefab(current.gameObject) ? "(Prefab)" : "";

        sb.AppendLine($"{indent}- {current.name} {prefabStatus} {components}");

        foreach (Transform child in current)
        {
            Traverse(child, indent + "  ", sb);
        }
    }
}
#endif
