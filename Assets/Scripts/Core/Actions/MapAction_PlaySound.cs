using UnityEngine;
using Core;
using Core.Interfaces;

/// <summary>
/// Action để phát một âm thanh SFX duy nhất. 
/// Thường dùng cho các âm thanh môi trường như tiếng nổ, tiếng chuông báo động trên Timeline.
/// </summary>
[System.Serializable]
public class MapAction_PlaySound : MapAction
{
    public AudioClip Clip;
    [Range(0, 1)]
    public float VolumeScale = 1.0f;
    public bool PlayAtCameraPosition = true;

    [Header("Filter Settings")]
    [Tooltip("Nếu gán Filter có sẵn, Action sẽ dùng Source của nó thay vì tạo mới. Để trống để dùng chế độ Fire-and-Forget.")]
    public AudioLowPassFilter TargetFilter;

    public bool UseLowPassFilter = false;
    [Range(20, 22000)]
    [Tooltip("Tần số cắt. Giá trị càng thấp âm thanh càng 'bí' (muffled).")]
    public float LowPassCutoff = 5000f;

    public override void Execute(IMapManager manager)
    {
        if (Clip == null) return;

        // Lấy volume từ SettingsManager nếu có
        float masterSfxVolume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
        float finalVolume = VolumeScale * masterSfxVolume;

        Vector3 playPos = Vector3.zero;
        if (PlayAtCameraPosition && Camera.main != null)
        {
            playPos = Camera.main.transform.position;
        }

        // Ưu tiên 1: Sử dụng Filter/Source được gán sẵn trong Inspector
        if (TargetFilter != null)
        {
            AudioSource source = TargetFilter.GetComponent<AudioSource>();
            if (source != null)
            {
                TargetFilter.cutoffFrequency = LowPassCutoff;
                source.PlayOneShot(Clip, finalVolume);
            }
        }
        // Ưu tiên 2: Tự tạo Object tạm thời nếu bật UseLowPassFilter
        else if (UseLowPassFilter)
        {
            // Nếu dùng Filter, ta phải tạo thủ công AudioSource thay vì dùng PlayClipAtPoint
            GameObject tempGO = new GameObject("MapAction_TempAudio_Filtered");
            tempGO.transform.position = playPos;

            AudioSource source = tempGO.AddComponent<AudioSource>();
            source.clip = Clip;
            source.volume = finalVolume;
            source.spatialBlend = 1.0f; // Giả lập hành vi 3D của PlayClipAtPoint

            AudioLowPassFilter filter = tempGO.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = LowPassCutoff;

            source.Play();
            Object.Destroy(tempGO, Clip.length);
        }
        // Ưu tiên 3: Phát âm thanh 3D mặc định
        else
        {
            AudioSource.PlayClipAtPoint(Clip, playPos, finalVolume);
        }
    }
}