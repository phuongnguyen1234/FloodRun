using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core;

namespace UI.Multiplayer
{
    public class MapSelection : MonoBehaviour
    {
        [SerializeField] private Image _outline;
        [SerializeField] private Image _previewImage;
        [SerializeField] private TMP_Text _mapNameText;
        [SerializeField] private TMP_Text _voteCountText;
        [SerializeField] private TMP_Text _difficultyText;
        [SerializeField] private Button _selectionButton;

        [SerializeField] private Color _normalColor = Color.gray;
        [SerializeField] private Color _selectedColor = Color.yellow;

        private int _index;
        private System.Action<int> _onClicked;

        public void Setup(int index, MapData data, System.Action<int> onClicked)
        {
            _index = index;
            _onClicked = onClicked;
            
            if (data != null)
            {
                if (_previewImage != null) _previewImage.sprite = data.MapPreviewImage;
                if (_mapNameText != null) _mapNameText.text = data.Name;
                if (_difficultyText != null) _difficultyText.text = data.Difficulty.ToString("F1");
            }

            SetSelected(false);
            UpdateVotes(0);

            _selectionButton.onClick.RemoveAllListeners();
            _selectionButton.onClick.AddListener(() => _onClicked?.Invoke(_index));
        }

        public void SetSelected(bool isSelected)
        {
            if (_outline != null) _outline.color = isSelected ? _selectedColor : _normalColor;
        }

        public void UpdateVotes(int count)
        {
            if (_voteCountText != null) _voteCountText.text = count.ToString();
        }

        public void SetInteraction(bool interactive)
        {
            if (_selectionButton != null) _selectionButton.interactable = interactive;
        }
    }
}