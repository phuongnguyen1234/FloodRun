using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic; // Thêm để sử dụng HashSet

/// <summary>
/// Công cụ giúp tách các sub-sprites từ một Texture (Sprite Mode: Multiple) thành các file PNG riêng biệt.
/// </summary>
public class SpriteExporter : EditorWindow
{
    [MenuItem("Tools/Flood Run/Export Selected Sprites to PNG")]
    public static void ExportSprites()
    {
        // Lấy danh sách các object đang được chọn trong Project window
        Object[] selectedObjects = Selection.objects;

        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("Sprite Exporter", "Vui lòng chọn ít nhất một Texture (Sprite Sheet) hoặc một Sprite con trong Project window.", "OK");
            return;
        }

        int totalExported = 0;
        // Sử dụng HashSet để tránh xử lý trùng lặp Texture2D nếu nhiều sub-sprite từ cùng một sheet được chọn
        HashSet<Texture2D> texturesToProcess = new HashSet<Texture2D>();

        foreach (Object obj in selectedObjects)
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);
            Texture2D texture = obj as Texture2D;

            if (texture != null)
            {
                texturesToProcess.Add(texture);
            }
            else
            {
                Sprite sprite = obj as Sprite;
                if (sprite != null && sprite.texture != null)
                {
                    texturesToProcess.Add(sprite.texture);
                }
            }
        }

        if (texturesToProcess.Count == 0)
        {
            EditorUtility.DisplayDialog("Sprite Exporter", "Không tìm thấy Texture (Sprite Sheet) hoặc Sprite hợp lệ nào trong lựa chọn của bạn.", "OK");
            return;
        }

        foreach (Texture2D texture in texturesToProcess)
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath)) continue; // Không nên xảy ra với asset hợp lệ

            TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) continue;

            bool oldReadable = ti.isReadable;
            if (!oldReadable)
            {
                ti.isReadable = true;
                ti.SaveAndReimport();
                AssetDatabase.Refresh(); // Đảm bảo Unity tải lại asset với cài đặt mới
            }

            // Load tất cả các assets tại đường dẫn này (bao gồm cả các sub-sprites)
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            string outputFolder = Path.Combine(Path.GetDirectoryName(assetPath), texture.name + "_Exported");

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            foreach (Object asset in subAssets)
            {
                if (asset is Sprite sprite)
                {
                    ExportSubSprite(sprite, outputFolder);
                    totalExported++;
                }
            }

            // Trả lại trạng thái Read/Write ban đầu để tối ưu bộ nhớ nếu cần
            if (!oldReadable)
            {
                ti.isReadable = false;
                ti.SaveAndReimport();
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[SpriteExporter] Đã xuất thành công {totalExported} ảnh PNG.");
        EditorUtility.DisplayDialog("Thành công!", $"Đã tách {totalExported} sprites vào thư mục tương ứng.", "OK");
    }

    private static void ExportSubSprite(Sprite sprite, string folder)
    {
        Texture2D sourceTex = sprite.texture;
        Rect r = sprite.rect;

        // Tạo texture mới dựa trên kích thước của sub-sprite
        Texture2D newTex = new Texture2D((int)r.width, (int)r.height, sourceTex.format, false);

        // Copy pixels từ texture gốc sang texture mới
        Color[] pixels = sourceTex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
        newTex.SetPixels(pixels);
        newTex.Apply();

        // Encode thành PNG và lưu file
        byte[] bytes = newTex.EncodeToPNG();
        string fileName = sprite.name + ".png";
        File.WriteAllBytes(Path.Combine(folder, fileName), bytes);

        DestroyImmediate(newTex);
    }
}
