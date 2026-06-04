using UnityEngine;
using TMPro;
using Core;

namespace UI.Multiplayer
{
    /// <summary>
    /// Quản lý việc hiển thị thông tin trực quan trên bảng thông tin (Info Board) đặt trong không gian Lobby.
    /// </summary>
    public class LobbyInfoBoard : MonoBehaviour
    {
        [Header("World UI References")]
        [SerializeField] private TMP_Text _worldMapNameText;
        [SerializeField] private SpriteRenderer _worldMapPreviewImage;
        [SerializeField] private TMP_Text _worldPlayerCountText;
        [SerializeField] private SpriteRenderer _worldDifficultyColorImage;
        [SerializeField] private TMP_Text _worldDifficultyText;

        [Header("Fallback")]
        [SerializeField] private Sprite _defaultMapPreview;

        private void Awake()
        {
            SetVisibility(true);
            UpdateMapInfo(null, 1f, null);
            SetPlayerCountText("Waiting for players...");
        }

        public void SetVisibility(bool show)
        {
            gameObject.SetActive(show);
        }

        public void UpdateMapInfo(MapData data, float difficulty, DifficultyPalette palette)
        {
            SetVisibility(true);
            UpdateMapName(data != null ? data.Name : "");
            UpdateMapPreview(data != null ? data.MapPreviewImage : null);
            UpdateDifficultyDisplay(difficulty, palette);
        }

        public void UpdateMapName(string name)
        {
            if (_worldMapNameText != null)
                _worldMapNameText.text = !string.IsNullOrEmpty(name) ? name : "";
        }

        public void UpdateMapPreview(Sprite sprite)
        {
            if (_worldMapPreviewImage != null)
            {
                _worldMapPreviewImage.sprite = sprite != null ? sprite : _defaultMapPreview;
                _worldMapPreviewImage.enabled = _worldMapPreviewImage.sprite != null;
            }
        }

        public void UpdateDifficultyDisplay(float difficulty, DifficultyPalette palette)
        {
            Color themeColor = palette != null ? palette.GetColorFromRating(difficulty) : Color.white;
            if (_worldDifficultyColorImage != null) _worldDifficultyColorImage.color = themeColor;

            if (_worldDifficultyText != null)
            {
                string tierName = "—";
                if (palette != null)
                {
                    var tier = palette.GetTierFromRating(difficulty);
                    tierName = tier == DifficultyPalette.Tier.CrazyPlus ? "Crazy+" : tier.ToString();
                }
                _worldDifficultyText.text = $"{tierName}: {difficulty:0.##}";
            }
        }

        public void SetPlayerCountText(string text)
        {
            if (_worldPlayerCountText == null) return;
            _worldPlayerCountText.text = string.IsNullOrWhiteSpace(text)
                ? "Waiting for players..."
                : text;
        }
    }
}
