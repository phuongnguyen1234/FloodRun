#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Tự động chạy bản Build (.exe) khi nhấn Play trong Editor để test Multiplayer mà không tốn RAM cho Editor thứ 2.
/// </summary>
[InitializeOnLoad]
public class MultiplayerAutoRun
{
    private const string PREF_KEY = "MultiplayerAutoRun_Enabled";
    private static string BuildPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds/Win/FloodRun_Client.exe");

    static MultiplayerAutoRun()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Multiplayer/Toggle Auto-Run Client")]
    public static void ToggleAutoRun()
    {
        bool current = EditorPrefs.GetBool(PREF_KEY, false);
        EditorPrefs.SetBool(PREF_KEY, !current);
        UnityEngine.Debug.Log($"[Multiplayer] Auto-run Client is now: {(!current ? "ENABLED" : "DISABLED")}");
    }

    [MenuItem("Multiplayer/Toggle Auto-Run Client", true)]
    public static bool ToggleAutoRunValidate()
    {
        Menu.SetChecked("Multiplayer/Toggle Auto-Run Client", EditorPrefs.GetBool(PREF_KEY, false));
        return true;
    }

    [MenuItem("Multiplayer/Build Client Now")]
    public static void BuildClient()
    {
        string buildFolder = Path.GetDirectoryName(BuildPath);
        if (!Directory.Exists(buildFolder)) Directory.CreateDirectory(buildFolder);

        BuildPlayerOptions buildPlayerOptions = new()
        {
            scenes = GetEnabledScenePaths(),
            locationPathName = BuildPath,
            target = BuildTarget.StandaloneWindows64,
            // Development: Cho phép Debug. CompressWithLz4: Build nhanh hơn so với nén mặc định.
            options = BuildOptions.Development | BuildOptions.CompressWithLz4
        };

        UnityEngine.Debug.Log("[Multiplayer] Starting Client Build...");
        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            UnityEngine.Debug.Log("[Multiplayer] Build Succeeded!");
        else
            UnityEngine.Debug.LogError("[Multiplayer] Build Failed!");
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        // Chỉ chạy khi bắt đầu nhấn Play (trước khi vào Play Mode)
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            if (EditorPrefs.GetBool(PREF_KEY, false))
            {
                RunStandalone();
            }
        }
    }

    private static void RunStandalone()
    {
        if (!File.Exists(BuildPath))
        {
            UnityEngine.Debug.LogWarning("[Multiplayer] Build file not found. Please 'Build Client Now' first.");
            return;
        }

        UnityEngine.Debug.Log("[Multiplayer] Launching Standalone Client...");
        ProcessStartInfo startInfo = new(BuildPath)
        {
            // Thêm tham số để game nhận biết đây là bản build phụ dùng để test
            Arguments = "-isClientInstance",
            WindowStyle = ProcessWindowStyle.Normal
        };
        Process.Start(startInfo);
    }

    private static string[] GetEnabledScenePaths()
    {
        var scenes = EditorBuildSettings.scenes;
        var paths = new System.Collections.Generic.List<string>();
        foreach (var scene in scenes)
        {
            if (scene.enabled) paths.Add(scene.path);
        }
        return paths.ToArray();
    }
}
#endif