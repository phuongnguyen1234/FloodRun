using UnityEngine;
using UnityEditor;
using System.IO;
using Core;
using System.Linq;

namespace EditorTools
{
    /// <summary>
    /// Công cụ hỗ trợ chụp ảnh preview từ Map Prefab và gán vào MapData.
    /// </summary>
    public class MapPreviewCaptureTool : EditorWindow
    {
        private MapData _targetMapData;
        private int _captureWidth = 1280;
        private int _captureHeight = 720; // Tỉ lệ 16:9
        private float _orthoSize = 12f;
        private Vector2 _cameraOffset = new Vector2(0, 5f);
        private Color _gizmoColor = Color.red; // Màu Gizmo vùng chụp

        private GameObject _spawnedMap;
        private Camera _captureCamera;

        [MenuItem("Tools/Flood Run/Map Preview Capture")]
        public static void ShowWindow()
        {
            GetWindow<MapPreviewCaptureTool>("Map Preview Capture");
        }

        private void OnEnable()
        {
            // Đăng ký sự kiện vẽ vào Scene View
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            GUILayout.Label("Map Preview Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("Công cụ này sẽ instantiate Map Prefab vào Scene hiện tại để chụp ảnh.", MessageType.Info);
            _targetMapData = (MapData)EditorGUILayout.ObjectField("Map Data Asset", _targetMapData, typeof(MapData), false);
            _captureWidth = EditorGUILayout.IntField("Width (px)", _captureWidth);
            _captureHeight = EditorGUILayout.IntField("Height (px)", _captureHeight);
            _orthoSize = EditorGUILayout.FloatField("Camera Ortho Size", _orthoSize);
            _cameraOffset = EditorGUILayout.Vector2Field("Camera Offset (X, Y)", _cameraOffset);
            _gizmoColor = EditorGUILayout.ColorField("Gizmo Color", _gizmoColor);

            EditorGUILayout.Space();

            if (_spawnedMap == null)
            {
                if (GUILayout.Button("Enable Preview Mode", GUILayout.Height(40)))
                {
                    SetupPreview();
                }
            }
            else
            {
                GUI.color = Color.green;
                if (GUILayout.Button("Capture Snapshot", GUILayout.Height(40)))
                {
                    Capture();
                }
                GUI.color = Color.white;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Snap to Scene View"))
                {
                    SnapCameraToScene();
                }
                
                if (GUILayout.Button("Exit Preview (Cleanup)"))
                {
                    Cleanup();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("TIP: Chọn object 'PreviewCaptureCamera' trong Hierarchy để di chuyển/xoay camera tự do bằng các công cụ Move/Rotate của Unity.", MessageType.Info);
                
                // Đồng bộ Ortho size real-time
                if (_captureCamera != null)
                {
                    _captureCamera.orthographicSize = _orthoSize;
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_captureCamera == null) return;

            // Thiết lập màu sắc và ma trận vẽ theo Camera
            Handles.color = _gizmoColor;
            Matrix4x4 cubeTransform = Matrix4x4.TRS(_captureCamera.transform.position, _captureCamera.transform.rotation, Vector3.one);
            Handles.matrix = cubeTransform;

            // Tính toán kích thước khung hình dựa trên Orthographic Size và Aspect Ratio
            float height = _orthoSize * 2;
            float width = height * ((float)_captureWidth / _captureHeight);

            // Vẽ khung dây (Wire Cube) đại diện cho vùng chụp
            Handles.DrawWireCube(Vector3.zero, new Vector3(width, height, 0));
            Handles.Label(new Vector3(-width / 2, height / 2, 0), "Capture Area");
        }

        private void SetupPreview()
        {
            if (_targetMapData == null || _targetMapData.MapPrefab == null)
            {
                EditorUtility.DisplayDialog("Lỗi", "Vui lòng gán MapData và Prefab tương ứng.", "OK");
                return;
            }
            
            Cleanup(); // Xóa cái cũ nếu có

            _spawnedMap = (GameObject)PrefabUtility.InstantiatePrefab(_targetMapData.MapPrefab);
            _spawnedMap.name = "[PREVIEW] " + _targetMapData.MapPrefab.name;

            SetupCamera();
        }

        private void SetupCamera()
        {
            if (_captureCamera == null)
            {
                _captureCamera = FindObjectsByType<Camera>(FindObjectsSortMode.None)
                    .FirstOrDefault(c => c.name == "PreviewCaptureCamera");
            }

            if (_captureCamera != null)
            {
                return;
            }

            // 2. Nếu chưa có thì mới tạo mới
            GameObject camObj = new GameObject("PreviewCaptureCamera");
            
            _captureCamera = camObj.AddComponent<Camera>();
            _captureCamera.orthographic = true;
            _captureCamera.orthographicSize = _orthoSize;
            _captureCamera.backgroundColor = new Color(0.5607843f, 0.8862745f, 0.9294117f, 1f); // Màu #8FE2ED
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.nearClipPlane = 0.1f;
            _captureCamera.farClipPlane = 1000f;
            _captureCamera.cullingMask = ~0; // Render TẤT CẢ các layer
            _captureCamera.allowHDR = false; // Tắt HDR để tránh lỗi lệch màu/đen ảnh khi ReadPixels
            _captureCamera.allowMSAA = false; // Tắt MSAA để render ổn định hơn trong Editor
            _captureCamera.useOcclusionCulling = false;

            // 3. Xác định vị trí Camera
            MapManager mapManager = _spawnedMap.GetComponentInChildren<MapManager>();
            Vector3 camPos = Vector3.zero;

            if (mapManager != null)
            {
                // Sử dụng vị trí Center để camera luôn căn giữa vùng spawn
                camPos = mapManager.GetPlayerSpawnCenter();
            }
            else
            {
                Renderer[] renderers = _spawnedMap.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    camPos = b.center;
                }
            }
            
            camPos += (Vector3)_cameraOffset;
            camPos.z = -100f; // Đưa camera ra xa để nhìn về Z=0
            _captureCamera.transform.position = camPos;
            
            // Tự động focus vào camera để người dùng di chuyển luôn
            Selection.activeGameObject = _captureCamera.gameObject;
            EditorGUIUtility.PingObject(_captureCamera.gameObject);
        }

        private void SnapCameraToScene()
        {
            if (_captureCamera == null || SceneView.lastActiveSceneView == null) return;
            
            _captureCamera.transform.position = new Vector3(SceneView.lastActiveSceneView.pivot.x, SceneView.lastActiveSceneView.pivot.y, -100f);
            // Nếu scene view là 2D, giữ nguyên góc quay thẳng
            if (SceneView.lastActiveSceneView.in2DMode)
            {
                _captureCamera.transform.rotation = Quaternion.identity;
                _captureCamera.transform.position = new Vector3(_captureCamera.transform.position.x, _captureCamera.transform.position.y, -10f);
            }
            else
            {
                _captureCamera.transform.rotation = SceneView.lastActiveSceneView.rotation;
            }
            
            _orthoSize = SceneView.lastActiveSceneView.size;
        }

        private void Capture()
        {
            if (_targetMapData == null || _captureCamera == null) return;

            // Lưu lại cường độ ánh sáng môi trường cũ
            Color oldAmbient = RenderSettings.ambientLight;
            // Ép ánh sáng môi trường lên trắng để Sprite không bị đen trong Prefab Stage
            RenderSettings.ambientLight = Color.white;

            // 4. Render ra Texture
            RenderTexture rt = RenderTexture.GetTemporary(_captureWidth, _captureHeight, 24, RenderTextureFormat.Default);
            rt.Create(); // Đảm bảo Texture được tạo vật lý trước khi render
            
            _captureCamera.targetTexture = rt;
            _captureCamera.Render();

            RenderTexture.active = rt;
            Texture2D screenShot = new Texture2D(_captureWidth, _captureHeight, TextureFormat.RGB24, false);
            screenShot.ReadPixels(new Rect(0, 0, _captureWidth, _captureHeight), 0, 0);
            screenShot.Apply();
            
            _captureCamera.targetTexture = null;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            // Khôi phục lại ánh sáng môi trường
            RenderSettings.ambientLight = oldAmbient;

            // 5. Lưu thành file PNG
            string pascalName = ToPascalCase(_targetMapData.Name);
            string folderPath = $"Assets/Maps/{pascalName}/Images";

            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            
            string fullPath = Path.Combine(folderPath, $"{_targetMapData.name}_Preview.png");
            File.WriteAllBytes(fullPath, screenShot.EncodeToPNG());

            AssetDatabase.Refresh();

            // 7. Cấu hình file ảnh thành Sprite và gán vào MapData
            TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }

            _targetMapData.MapPreviewImage = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
            EditorUtility.SetDirty(_targetMapData);
            AssetDatabase.SaveAssets();

            Debug.Log($"[MapPreviewCaptureTool] Đã lưu ảnh preview tại: {fullPath}");
            EditorUtility.DisplayDialog("Thành công!", $"Đã cập nhật ảnh preview cho {_targetMapData.name}", "OK");
        }

        private void Cleanup()
        {
            if (_captureCamera != null) DestroyImmediate(_captureCamera.gameObject);
            _captureCamera = null;
            
            if (_spawnedMap != null) DestroyImmediate(_spawnedMap);
            _spawnedMap = null;

            // Tìm kiếm và dọn dẹp triệt để các object "ma" trong mọi scene
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                if (go.name == "PreviewCaptureCamera" || go.name.StartsWith("[PREVIEW]"))
                {
                    if (go.scene.isLoaded) DestroyImmediate(go);
                }
            }
        }

        private void OnDestroy()
        {
            // Tự động dọn dẹp khi đóng cửa sổ tool
            Cleanup();
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            string[] words = input.Split(new[] { ' ', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            return string.Join("", words);
        }
    }
}