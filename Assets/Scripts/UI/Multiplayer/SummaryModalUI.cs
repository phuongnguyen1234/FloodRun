using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Core.Interfaces;

namespace UI.Multiplayer
{
    public class SummaryModalUI : MonoBehaviour
    {
        [SerializeField] private Transform _container;
        [SerializeField] private GameObject _itemPrefab;
        [SerializeField] private TMP_Text _totalWinsText;
        [SerializeField] private TMP_Text _totalCoinsText;

        public void Show(List<RoundSummaryData> results)
        {
            if (_container == null) { Debug.LogError("SummaryModalUI: Container is missing!"); return; }
            
            // Dọn dẹp các item cũ trước khi hiển thị chuỗi mới
            foreach (Transform child in _container) Destroy(child.gameObject);

            int totalWins = 0;
            int totalCoins = 0;

            if (results == null || results.Count == 0) Debug.LogWarning("SummaryModalUI: Show called with empty results.");

            foreach (var res in results)
            {
                GameObject go = Instantiate(_itemPrefab, _container);
                if (go.TryGetComponent<SummaryItem>(out var item)) item.Setup(res);
                
                if (res.IsWin) totalWins++;
                totalCoins += res.CoinsEarned;
            }

            if (_totalWinsText != null) _totalWinsText.text = $"{totalWins}";
            if (_totalCoinsText != null) _totalCoinsText.text = $"{totalCoins}";
        }

        public void Close() => gameObject.SetActive(false);
    }
}