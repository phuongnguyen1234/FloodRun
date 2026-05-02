using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Script tự động thiết lập scene "Home" làm scene khởi đầu mỗi khi nhấn Play trong Editor.
/// Giúp quy trình phát triển chuyên nghiệp hơn, đảm bảo luồng khởi tạo dữ liệu luôn đúng.
/// </summary>
[InitializeOnLoad]
public static class PlayModeStartSceneLoader
{
    static PlayModeStartSceneLoader()
    {
        // Tìm kiếm file scene có tên "Home" trong project
        string[] guids = AssetDatabase.FindAssets("Home t:Scene");
        
        if (guids.Length > 0)
        {
            // Lấy đường dẫn của scene đầu tiên tìm thấy
            string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            
            // Thiết lập scene này là scene bắt đầu khi nhấn Play
            EditorSceneManager.playModeStartScene = sceneAsset;
            
            Debug.Log($"<color=green>[DevSystem]</color> Play Mode sẽ luôn bắt đầu từ Scene: <b>{scenePath}</b>");
        }
        else
        {
            Debug.LogWarning("[DevSystem] Không tìm thấy scene nào tên 'Home' để thiết lập Play Mode Start Scene.");
        }
    }
}