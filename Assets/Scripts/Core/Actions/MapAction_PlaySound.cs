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

    public override void Execute(IMapManager manager)
    {
        if (Clip == null) return;

        // Lấy volume từ SettingsManager nếu có
        float masterSfxVolume = (SettingsManager.Instance != null) ? SettingsManager.Instance.SfxVolume : 1f;
        float finalVolume = VolumeScale * masterSfxVolume;

        if (PlayAtCameraPosition)
        {
            AudioSource.PlayClipAtPoint(Clip, Camera.main.transform.position, finalVolume);
        }
        else
        {
            // Có thể mở rộng để phát tại vị trí của một object cụ thể
            AudioSource.PlayClipAtPoint(Clip, Vector3.zero, finalVolume);
        }
    }
}