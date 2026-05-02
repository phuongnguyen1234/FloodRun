using UnityEngine;
using System.Collections.Generic;
using Core;
using System;
using System.Linq;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện hiển thị danh sách map theo từng tier và thời gian hoàn thành tốt nhất của người chơi.
    /// Sử dụng trong Progress screen
    /// </summary>
    public class MapSectionUI : MonoBehaviour
    {
        [Serializable]
        public struct TierFilterConfig
        {
            public DifficultyPalette.Tier Tier;
            public DifficultyButton FilterButton;
        }

        [Header("Databases")]
        [SerializeField] private MapDatabase _mapDatabase;
        [SerializeField] private DifficultyPalette _palette;

        [Header("UI Components")]
        [SerializeField] private GameObject _progressCardPrefab;
        [SerializeField] private Transform _contentParent; // Content của Scroll View

        [Header("Filter Tabs")]
        [SerializeField] private List<TierFilterConfig> _filters;
        
        private PlayerProfile _profile;
        private DifficultyPalette.Tier? _activeTier = null;

        private void Start()
        {
            // Load dữ liệu người chơi một lần khi mở tab
            _profile = SaveSystem.LoadProfile();

            SetupFilterButtons();
            
            // Hiển thị mặc định (ví dụ: hiển thị Tier đầu tiên trong list hoặc tất cả)
            if (_filters.Count > 0)
                ApplyFilter(_filters[0].Tier);
        }

        private void SetupFilterButtons()
        {
            foreach (var config in _filters)
            {
                if (config.FilterButton == null) continue;

                // Thiết lập giao diện nút từ Palette
                string label = config.Tier.ToString();
                if (config.Tier == DifficultyPalette.Tier.CrazyPlus) label = "Crazy+";
                
                config.FilterButton.SetVisuals(label, _palette.GetColor(config.Tier));

                // Gán sự kiện click
                config.FilterButton.Button.onClick.RemoveAllListeners();
                config.FilterButton.Button.onClick.AddListener(() => ApplyFilter(config.Tier));
            }
        }

        private void ApplyFilter(DifficultyPalette.Tier tier)
        {
            _activeTier = tier;
            RefreshList();
            UpdateTabVisuals();
        }

        private void RefreshList()
        {
            // 1. Xóa danh sách cũ
            foreach (Transform child in _contentParent) Destroy(child.gameObject);

            if (_mapDatabase == null || _progressCardPrefab == null) return;

            // 2. Lọc map theo tier
            var filteredMaps = _mapDatabase.AllMaps.Where(m => _palette.GetTierFromRating(m.Difficulty) == _activeTier);

            // 3. Sinh card mới
            foreach (var map in filteredMaps)
            {
                GameObject cardObj = Instantiate(_progressCardPrefab, _contentParent);
                ProgressMapCardView cardView = cardObj.GetComponent<ProgressMapCardView>();

                if (cardView != null)
                {
                    // Tìm kỷ lục của map này trong profile
                    var record = _profile.MapRecords.FirstOrDefault(r => r.MapName == map.Name);
                    float bestTime = record != null ? record.BestTime : -1f;

                    cardView.Setup(map, bestTime);
                }
            }
        }

        private void UpdateTabVisuals()
        {
            // Bạn có thể thêm logic làm sáng nút đang chọn ở đây
            foreach (var config in _filters)
            {
                float alpha = (config.Tier == _activeTier) ? 1f : 0.5f;
                CanvasGroup cg = config.FilterButton.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = alpha;
            }
        }
    }
}