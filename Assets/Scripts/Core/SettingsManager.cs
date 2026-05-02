using UnityEngine;
using System;

namespace Core
{
    /// <summary>
    /// Quản lý lưu trữ và truy xuất các thiết lập của trò chơi qua PlayerPrefs.
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        public enum KeyBindingType
        {
            Jump,
            Dive,
            Slide
        }

        public static SettingsManager Instance { get; private set; }

        [Header("Audio")]
        public float MusicVolume = 0.7f;
        public float SfxVolume = 0.8f;

        [Header("Gameplay")]
        public bool AutoRestart = false;
        public bool GoalLocator = true;

        [Header("Key Bindings (Input System Paths)")]
        public string JumpKey = "<Keyboard>/space";
        public string DiveKey = "<Keyboard>/leftShift";
        public string SlideKey = "<Keyboard>/leftCtrl";

        public event Action OnKeyBindingsChanged;
        public event Action OnSettingsApplied;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadSettings();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void SaveSettings()
        {
            PlayerPrefs.SetFloat("MusicVolume", MusicVolume);
            PlayerPrefs.SetFloat("SfxVolume", SfxVolume);
            PlayerPrefs.SetInt("AutoRestart", AutoRestart ? 1 : 0);
            PlayerPrefs.SetInt("GoalLocator", GoalLocator ? 1 : 0);
            
            PlayerPrefs.SetString("Key_Jump", JumpKey);
            PlayerPrefs.SetString("Key_Dive", DiveKey);
            PlayerPrefs.SetString("Key_Slide", SlideKey);
            
            PlayerPrefs.Save();
            ApplySettings();
            OnKeyBindingsChanged?.Invoke(); // Thông báo rằng key bindings đã thay đổi
        }

        public void LoadSettings()
        {
            MusicVolume = PlayerPrefs.GetFloat("MusicVolume", 0.7f);
            SfxVolume = PlayerPrefs.GetFloat("SfxVolume", 0.8f);
            AutoRestart = PlayerPrefs.GetInt("AutoRestart", 0) == 1;
            GoalLocator = PlayerPrefs.GetInt("GoalLocator", 1) == 1;

            JumpKey = PlayerPrefs.GetString("Key_Jump", "<Keyboard>/space");
            DiveKey = PlayerPrefs.GetString("Key_Dive", "<Keyboard>/leftShift");
            SlideKey = PlayerPrefs.GetString("Key_Slide", "<Keyboard>/leftCtrl");
            
            ApplySettings();
        }

        public void ApplySettings()
        {
            if (BackgroundMusicManager.Instance != null)
                BackgroundMusicManager.Instance.GetAudioSource().volume = MusicVolume;
                
            OnSettingsApplied?.Invoke();
            OnKeyBindingsChanged?.Invoke(); // Cũng gọi khi load settings để áp dụng ngay
        }

        public void SetKeyBinding(KeyBindingType type, string path)
        {
            switch (type)
            {
                case KeyBindingType.Jump:
                    JumpKey = path;
                    break;
                case KeyBindingType.Dive:
                    DiveKey = path;
                    break;
                case KeyBindingType.Slide:
                    SlideKey = path;
                    break;
            }
        }
    }
}