using UnityEngine;
using Core;
using System;
using System.Collections.Generic;

/// <summary>
/// View để chọn độ khó, có thể chọn một tier cụ thể hoặc "All Maps" (tất cả các tier).
/// </summary>
public class DifficultySelectionView : MonoBehaviour
{
    [Serializable]
    public struct TierButtonMapping
    {
        public DifficultyButton Button;
        public bool IsAllMaps; // Nếu tích vào đây thì nút này sẽ hiện "All Maps"
        public DifficultyPalette.Tier Tier;
    }

    [SerializeField] private List<TierButtonMapping> _mappings;
    [SerializeField] private DifficultyPalette _palette;

    // Định nghĩa màu xám chuẩn 141, 141, 141 bằng Color32
    private readonly Color _defaultGray = new Color32(141, 141, 141, 255);

    private Action<DifficultyPalette.Tier?> _onTierSelected;
    private bool _isInitialized = false;

    public void Setup(Action<DifficultyPalette.Tier?> onSelect)
    {
        _onTierSelected = onSelect;

        // Chỉ khởi tạo Visual và Event một lần duy nhất
        if (!_isInitialized && _mappings != null)
        {
            foreach (var map in _mappings)
            {
                if (map.Button == null) continue;

                // 1. Thiết lập Visual
                string label;
                Color color;

                if (map.IsAllMaps)
                {
                    label = "All";
                    color = _palette != null ? _palette.DefaultColor : _defaultGray;
                }
                else
                {
                    label = (map.Tier == DifficultyPalette.Tier.CrazyPlus) ? "Crazy+" : map.Tier.ToString();
                    color = (_palette != null) ? _palette.GetColor(map.Tier) : _defaultGray;
                }

                map.Button.SetVisuals(label, color);

                // 2. Gán sự kiện Click: Tạo một biến local cố định cho scope này
                DifficultyPalette.Tier? currentTier = map.IsAllMaps ? null : (DifficultyPalette.Tier?)map.Tier;
                
                if (map.Button.Button != null)
                {
                    map.Button.Button.onClick.AddListener(() => {
                        _onTierSelected?.Invoke(currentTier);
                    });
                }
            }
            _isInitialized = true;
        }
    }
}