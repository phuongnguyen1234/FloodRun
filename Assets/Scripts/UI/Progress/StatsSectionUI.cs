using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện hiển thị thông tin thống kê của người chơi, bao gồm:
    /// - Tên người chơi (có thể chỉnh sửa)
    /// - Tổng số lần thắng duy nhất 
    /// - Tổng số coins đã kiếm được
    /// - Số lượng map thắng duy nhất theo từng Tier (dựa trên MapRecords và MapDatabase)
    /// Lưu ý: Dữ liệu được load từ DataManager.Instance.Profile và cập nhật lên UI. 
    /// Khi người chơi chỉnh sửa tên, sẽ cập nhật trực tiếp vào Profile và lưu lại. 
    /// Số liệu thống kê về map thắng duy nhất được tính toán dựa trên MapRecords để đảm bảo luôn chính xác, 
    /// kể cả khi file save cũ chưa có TierWins. Các Text hiển thị số lượng map thắng theo
    /// từng Tier cần được gán thủ công trong Inspector thông qua List<TierStatUI>.
    /// </summary>
    public class StatsSectionUI : MonoBehaviour
    {
        [Serializable]
        public struct TierStatUI
        {
            public DifficultyPalette.Tier Tier;
            public TMP_Text CountText;
        }

        [Header("Databases")]
        [SerializeField] private MapDatabase _mapDatabase;
        [SerializeField] private DifficultyPalette _palette;

        [Header("Player Info")]
        [SerializeField] private Image _characterImage; // Placeholder cho chức năng Character sau này
        [SerializeField] private TMP_Text _playerNameText;
        [SerializeField] private TMP_Text _totalWinsText;
        [SerializeField] private TMP_Text _totalCoinsText;

        [Header("Edit Name Components")]
        [SerializeField] private TMP_InputField _playerNameInput;
        [SerializeField] private Button _editButton;
        [SerializeField] private Button _saveButton;
        [SerializeField] private GameObject _displayContainer; // Chứa Text + Nút Edit
        [SerializeField] private GameObject _editContainer;    // Chứa InputField + Nút Save

        [Header("Tier Statistics")]
        [Tooltip("Gán các Text tương ứng với từng độ khó vào đây")]
        [SerializeField] private List<TierStatUI> _tierStatTexts;

        private void Start()
        {
            // Gán sự kiện cho các nút
            if (_editButton != null) _editButton.onClick.AddListener(() => ToggleEditMode(true));
            if (_saveButton != null) _saveButton.onClick.AddListener(SavePlayerName);
            
            // Cho phép lưu bằng phím Enter trên bàn phím
            if (_playerNameInput != null) _playerNameInput.onSubmit.AddListener((_) => SavePlayerName());

            // Đảm bảo ban đầu ở chế độ hiển thị
            ToggleEditMode(false);

            RefreshStats();
        }

        /// <summary>
        /// Load dữ liệu từ SaveSystem và cập nhật lên giao diện.
        /// </summary>
        public void RefreshStats()
        {
            PlayerProfile profile = DataManager.Instance.Profile;

            // 1. Cập nhật thông tin cơ bản
            if (_playerNameText != null)
            {
                _playerNameText.text = profile.PlayerName;
                if (_playerNameInput != null) _playerNameInput.text = profile.PlayerName;
            }
            
            if (_totalWinsText != null)
                _totalWinsText.text = profile.UniqueWinsCount.ToString();

            if (_totalCoinsText != null)
                _totalCoinsText.text = profile.TotalCoins.ToString();

            // 2. Tính toán số lượng map thắng duy nhất theo từng Tier từ MapRecords
            // Cách này đảm bảo dữ liệu luôn đúng kể cả khi file save cũ chưa có TierWins
            Dictionary<DifficultyPalette.Tier, int> uniqueTierCounts = new Dictionary<DifficultyPalette.Tier, int>();

            if (_mapDatabase != null && _palette != null)
            {
                foreach (var record in profile.MapRecords)
                {
                    var mapData = _mapDatabase.AllMaps.Find(m => m.Name == record.MapName);
                    if (mapData != null)
                    {
                        var tier = _palette.GetTierFromRating(mapData.Difficulty);
                        if (uniqueTierCounts.ContainsKey(tier)) uniqueTierCounts[tier]++;
                        else uniqueTierCounts[tier] = 1;
                    }
                }
            }

            // 3. Cập nhật lên các Text đã gán
            foreach (var uiMapping in _tierStatTexts)
            {
                if (uiMapping.CountText == null) continue;

                int count = uniqueTierCounts.ContainsKey(uiMapping.Tier) ? uniqueTierCounts[uiMapping.Tier] : 0;
                uiMapping.CountText.text = count.ToString();
            }
        }

        private void ToggleEditMode(bool isEditing)
        {
            if (_displayContainer != null) _displayContainer.SetActive(!isEditing);
            if (_editContainer != null) _editContainer.SetActive(isEditing);

            if (isEditing && _playerNameInput != null)
            {
                _playerNameInput.Select();
                _playerNameInput.ActivateInputField();
            }
        }

        private void SavePlayerName()
        {
            if (_playerNameInput == null) return;

            string newName = _playerNameInput.text.Trim();
            
            if (!string.IsNullOrEmpty(newName))
            {
                // 1. Cập nhật Profile trong DataManager và lưu xuống ổ cứng
                DataManager.Instance.Profile.PlayerName = newName;
                DataManager.Instance.SaveData();

                // 2. Cập nhật UI
                if (_playerNameText != null) _playerNameText.text = newName;
                
                Debug.Log($"[StatsUI] Player name updated to: {newName}");
            }

            // 3. Thoát chế độ sửa
            ToggleEditMode(false);
        }
    }
}