using UnityEngine;
using UnityEngine.UI;
using Core;

namespace UI
{
    /// <summary>
    /// Script quản lý việc đồng bộ hóa dữ liệu giữa UI (Slider/Toggle) và SettingsManager.
    /// Gắn script này vào root của Prefab Settings Content.
    /// </summary>
    public class SettingsView : MonoBehaviour
    {
        [Header("Audio Sliders")]
        [SerializeField] private Slider _musicSlider;
        [SerializeField] private Slider _sfxSlider;

        [Header("Gameplay Toggles")]
        [SerializeField] private Toggle _autoRestartToggle;
        [SerializeField] private Toggle _goalLocatorToggle;

        private void OnEnable()
        {
            LoadCurrentSettings();
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            
            // CẢI THIỆN HIỆU SUẤT: 
            // Chỉ ghi dữ liệu xuống ổ cứng (I/O) một lần duy nhất khi đóng bảng Settings
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.SaveSettings();
        }

        private void LoadCurrentSettings()
        {
            if (SettingsManager.Instance == null) return;

            // Đọc dữ liệu từ SettingsManager và đổ vào UI
            if (_musicSlider != null) _musicSlider.value = SettingsManager.Instance.MusicVolume;
            if (_sfxSlider != null) _sfxSlider.value = SettingsManager.Instance.SfxVolume;
            if (_autoRestartToggle != null) _autoRestartToggle.isOn = SettingsManager.Instance.AutoRestart;
            if (_goalLocatorToggle != null) _goalLocatorToggle.isOn = SettingsManager.Instance.GoalLocator;
        }

        private void SubscribeEvents()
        {
            // Đăng ký sự kiện thay đổi giá trị của UI
            if (_musicSlider != null) _musicSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
            if (_sfxSlider != null) _sfxSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
            if (_autoRestartToggle != null) _autoRestartToggle.onValueChanged.AddListener(OnAutoRestartChanged);
            if (_goalLocatorToggle != null) _goalLocatorToggle.onValueChanged.AddListener(OnGoalLocatorChanged);
        }

        private void UnsubscribeEvents()
        {
            if (_musicSlider != null) _musicSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);
            if (_sfxSlider != null) _sfxSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
            if (_autoRestartToggle != null) _autoRestartToggle.onValueChanged.RemoveListener(OnAutoRestartChanged);
            if (_goalLocatorToggle != null) _goalLocatorToggle.onValueChanged.RemoveListener(OnGoalLocatorChanged);
        }

        private void OnMusicVolumeChanged(float value)
        {
            SettingsManager.Instance.MusicVolume = value;
            // Chỉ áp dụng hiệu ứng âm thanh tức thì để người chơi nghe thử, không ghi file
            SettingsManager.Instance.ApplySettings(); 
        }

        private void OnSfxVolumeChanged(float value)
        {
            SettingsManager.Instance.SfxVolume = value;
            SettingsManager.Instance.ApplySettings();
        }

        private void OnAutoRestartChanged(bool value)
        {
            SettingsManager.Instance.AutoRestart = value;
            // Checkbox không gây tốn hiệu suất như Slider nhưng ta cũng gom nhóm vào OnDisable để đồng bộ
            SettingsManager.Instance.ApplySettings();
        }

        private void OnGoalLocatorChanged(bool value)
        {
            SettingsManager.Instance.GoalLocator = value;
            SettingsManager.Instance.ApplySettings();
        }
    }
}