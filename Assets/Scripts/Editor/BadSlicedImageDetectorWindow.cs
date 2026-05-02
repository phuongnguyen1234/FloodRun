#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Công cụ Editor để quét scene hoặc prefab stage hiện tại nhằm phát hiện các UI Image hoặc SpriteRenderer có kiểu Sliced hoặc 
/// Tiled với kích thước hoặc số lần lặp lại
/// </summary>
public class BadSlicedImageDetectorWindow : EditorWindow
{
    private float sizeThreshold = 2000f;
    private float maxTileCount = 100f;
    private Vector2 scrollPosition;

    // List to store the results
    private class ScanResult
    {
        public GameObject obj;
        public string path;
        public string details;
    }
    private readonly List<ScanResult> foundObjects = new List<ScanResult>();

    [MenuItem("Window/Analysis/Bad Sliced Image Detector")]
    public static void ShowWindow()
    {
        GetWindow<BadSlicedImageDetectorWindow>("Bad Sliced Detector");
    }

    void OnGUI()
    {
        GUILayout.Label("Sliced/Tiled Image Performance Scanner", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Công cụ này quét scene (hoặc prefab stage) hiện tại để tìm các UI Image hoặc SpriteRenderer có kích thước hoặc số lần lặp lại (Tiled) quá lớn, có thể gây lỗi vertex.", MessageType.Info);

        sizeThreshold = EditorGUILayout.FloatField("Ngưỡng Kích Thước (Size Threshold)", sizeThreshold);
        maxTileCount = EditorGUILayout.FloatField("Ngưỡng Lặp Lại (Max Tile Count)", maxTileCount);

        if (GUILayout.Button("Quét Object Đang Chọn (Scan Selection)"))
        {
            ScanSelection();
        }

        if (GUILayout.Button("Quét Scene / Prefab hiện tại"))
        {
            ScanScene();
        }

        // Display results
        if (foundObjects.Count > 0)
        {
            EditorGUILayout.HelpBox($"Tìm thấy {foundObjects.Count} đối tượng đáng ngờ. Click vào một mục để chọn nó trong Hierarchy.", MessageType.Warning);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var result in foundObjects)
            {
                if (GUILayout.Button(result.path, EditorStyles.label))
                {
                    Selection.activeGameObject = result.obj;
                    EditorGUIUtility.PingObject(result.obj);
                }
                EditorGUILayout.LabelField(" ", result.details, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Separator();
            }
            EditorGUILayout.EndScrollView();
        }
        else
        {
            EditorGUILayout.HelpBox("Không tìm thấy vấn đề nào với các thiết lập hiện tại.", MessageType.Info);
        }
    }

    private void ScanSelection()
    {
        GameObject selectedGO = Selection.activeGameObject;
        if (selectedGO == null)
        {
            Debug.LogWarning("Vui lòng chọn một GameObject (ví dụ: Root của Prefab) trước khi bấm nút này.");
            return;
        }
        
        // Tìm tất cả component trong object được chọn và các con của nó
        Image[] images = selectedGO.GetComponentsInChildren<Image>(true);
        SpriteRenderer[] renderers = selectedGO.GetComponentsInChildren<SpriteRenderer>(true);

        AnalyzeList(images, renderers);
    }

    private void ScanScene()
    {
        Image[] images = FindObjectsOfType<Image>(true);
        SpriteRenderer[] renderers = FindObjectsOfType<SpriteRenderer>(true);

        AnalyzeList(images, renderers);
    }

    private void AnalyzeList(Image[] images, SpriteRenderer[] renderers)
    {
        foundObjects.Clear();
        int tiledCulpritCount = 0;

        // --- 1. Quét UI Images ---
        foreach (var img in images)
        {
            if (img.sprite == null) continue;
            if (img.type != Image.Type.Sliced && img.type != Image.Type.Tiled) continue;

            RectTransform rt = img.rectTransform;
            float width = rt.rect.width * Mathf.Abs(rt.lossyScale.x);
            float height = rt.rect.height * Mathf.Abs(rt.lossyScale.y);
            float tileProduct = 1;
            
            if (img.type == Image.Type.Tiled)
            {
                // Tính toán chính xác hơn dựa trên pixelsPerUnitMultiplier
                float multiplier = img.pixelsPerUnitMultiplier > 0.001f ? img.pixelsPerUnitMultiplier : 1f;
                float spriteW = img.sprite.rect.width / multiplier;
                float spriteH = img.sprite.rect.height / multiplier;
                
                if (spriteW > 0) tileProduct *= (width / spriteW);
                if (spriteH > 0) tileProduct *= (height / spriteH);
            }

            // Cảnh báo đặc biệt nếu số lượng vertices ước tính > 65000 (giới hạn 16-bit mesh)
            float estimatedVerts = tileProduct * 4;
            bool isMeshCrashRisk = estimatedVerts > 60000;

            if (width > sizeThreshold || height > sizeThreshold || tileProduct > maxTileCount)
            {
                string details = $"Type: UI ({img.type}) | Size: {width:F0}x{height:F0} | Tiles: {tileProduct:F0} | ~{estimatedVerts:F0} Verts";
                if (isMeshCrashRisk) 
                {
                    details = "★ CRITICAL: " + details;
                    tiledCulpritCount++;
                }
                foundObjects.Add(new ScanResult { obj = img.gameObject, path = GetFullPath(img.transform), details = details });
            }
        }

        // --- 2. Quét Sprite Renderers ---
        foreach (var sr in renderers)
        {
            if (sr.sprite == null) continue;
            if (sr.drawMode != SpriteDrawMode.Sliced && sr.drawMode != SpriteDrawMode.Tiled) continue;

            float width = sr.size.x * Mathf.Abs(sr.transform.lossyScale.x);
            float height = sr.size.y * Mathf.Abs(sr.transform.lossyScale.y);
            float tileCount = 0;

            if (sr.drawMode == SpriteDrawMode.Tiled)
            {
                float areaObj = width * height;
                float areaSprite = (sr.sprite.bounds.size.x) * (sr.sprite.bounds.size.y);
                if (areaSprite > 0.001f) tileCount = areaObj / areaSprite;
            }

            float estimatedVerts = tileCount * 4;
            bool isMeshCrashRisk = estimatedVerts > 60000;

            if (width > sizeThreshold || height > sizeThreshold || tileCount > maxTileCount)
            {
                string details = $"Type: Sprite ({sr.drawMode}) | Size: {width:F0}x{height:F0} | Tiles: {tileCount:F0} | ~{estimatedVerts:F0} Verts";
                if (isMeshCrashRisk) 
                {
                    details = "★ CRITICAL: " + details;
                    tiledCulpritCount++;
                }
                foundObjects.Add(new ScanResult { obj = sr.gameObject, path = GetFullPath(sr.transform), details = details });
            }
        }
        Debug.Log($"[Bad Sliced Image Detector] Quét hoàn tất. Tìm thấy {foundObjects.Count} đối tượng đáng ngờ.");
        if (tiledCulpritCount > 0) Debug.LogError($"PHÁT HIỆN {tiledCulpritCount} ĐỐI TƯỢNG CÓ THỂ GÂY CRASH (QUÁ NHIỀU VERTICES). KIỂM TRA LIST!");
    }

    private string GetFullPath(Transform obj)
    {
        StringBuilder pathBuilder = new StringBuilder(obj.name);
        Transform current = obj.parent;
        while (current != null)
        {
            pathBuilder.Insert(0, current.name + "/");
            current = current.parent;
        }
        return pathBuilder.ToString();
    }
}
#endif
