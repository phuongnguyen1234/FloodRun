using UnityEngine;
using TMPro;
using Core.Interfaces;
using UnityEngine.UI;
using Core;

namespace UI.Multiplayer
{
    public class SummaryItem : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] private Image _mapImage;
        [SerializeField] private Image _overlayImage;
        [SerializeField] private DifficultyPalette _palette;
        [SerializeField] private TMP_Text _mapNameText;
        [SerializeField] private TMP_Text _rankText;
        [SerializeField] private TMP_Text _buttonsText;
        [SerializeField] private TMP_Text _timeText;
        [SerializeField] private TMP_Text _coinsText;

        public void Setup(RoundSummaryData data)
        {
            if (_mapImage != null) _mapImage.sprite = data.MapPreviewSprite;
            if (_mapNameText != null) _mapNameText.text = $"{data.MapName} [{data.Tier}]"; // Giữ nguyên định dạng này
            if (_rankText != null) _rankText.text = data.IsWin ? $"{data.Rank}" : "0";
            if (_buttonsText != null) _buttonsText.text = $"{data.ButtonsPressed}";
            if (_timeText != null) _timeText.text = FormatTime(data.FinishTime);
            if (_coinsText != null) _coinsText.text = $"{data.CoinsEarned}";
            
            if (_overlayImage != null && _palette != null)
            {
                Color themeColor = _palette.GetColor(data.Tier);
                themeColor.a = 64f / 255f; // Giữ độ trong suốt cho overlay
                _overlayImage.color = themeColor;
            }
        }

        private string FormatTime(float time)
        {
            System.TimeSpan t = System.TimeSpan.FromSeconds(time);
            return string.Format("{0:0}:{1:00}.{2:000}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
        }
    }
}