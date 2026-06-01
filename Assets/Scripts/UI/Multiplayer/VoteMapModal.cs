using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Core.Interfaces;
using Core;
using System.Linq;

namespace UI.Multiplayer
{
    public class VoteMapModal : MonoBehaviour
    {
        [SerializeField] private TMP_Text _headerText;
        [SerializeField] private Transform _mapGrid;
        [SerializeField] private MapSelection _mapSelectionPrefab;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private MapDatabase _mapDatabase;
        [SerializeField] private DifficultyPalette _palette;

        private IMultiplayerManager _manager;
        private List<MapSelection> _spawnedItems = new List<MapSelection>();
        private int _selectedIndex = -1;
        private bool _hasVoted = false;

        public void SetManager(IMultiplayerManager manager) => _manager = manager;

        /// <summary>
        /// Được gọi từ MultiplayerUIManager khi bắt đầu một round vote mới.
        /// </summary>
        public void ResetVoteStatus()
        {
            _hasVoted = false;
        }

        public void Setup()
        {
            // Chỉ reset index nếu chưa vote. Nếu đã vote rồi thì giữ nguyên để hiển thị outline.
            if (!_hasVoted) _selectedIndex = -1;

            if (_confirmButton != null) 
            {
                _confirmButton.interactable = false;
                var txt = _confirmButton.GetComponentInChildren<TMP_Text>();
                if (txt != null) txt.text = _hasVoted ? "Voted" : "Confirm Vote";
            }

            // Clear cũ
            foreach (var item in _spawnedItems) Destroy(item.gameObject);
            _spawnedItems.Clear();

            if (_manager == null || _mapDatabase == null) return;

            // Header text
            float diff = _manager.Difficulty.Value;
            string tierName = _palette.GetTierFromRating(diff).ToString();
            _headerText.text = $"Vote a Map [{tierName}: {diff:F2}]";

            // Spawn 3 map từ NetworkList
            var votingList = _manager.VotingMapNames;
            for (int i = 0; i < votingList.Count; i++)
            {
                string mapName = votingList[i].ToString();
                MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == mapName);
                
                var item = Instantiate(_mapSelectionPrefab, _mapGrid);
                item.Setup(i, data, OnMapSelected);
                
                // Nếu đã vote rồi thì khóa tương tác ngay từ đầu khi mở lại modal
                if (_hasVoted) 
                {
                    item.SetInteraction(false);
                    if (i == _selectedIndex)
                    {
                        item.SetSelected(true); // Khôi phục màu outline cho map đã vote
                    }
                }

                _spawnedItems.Add(item);
            }
        }

        private void Update()
        {
            if (_manager == null) return;

            // Tự đóng khi hết giờ (Logic slider đã chuyển sang Global ở MultiplayerUIManager)
            float elapsed = _manager.NetworkTime.Value;
            if (elapsed >= 10f) gameObject.SetActive(false);

            // Cập nhật số vote realtime
            var votes = _manager.MapVotes;
            for (int i = 0; i < _spawnedItems.Count; i++)
            {
                _spawnedItems[i].UpdateVotes(votes[i]);
            }
        }

        private void OnMapSelected(int index)
        {
            if (_hasVoted) return;

            _selectedIndex = index;
            for (int i = 0; i < _spawnedItems.Count; i++)
            {
                _spawnedItems[i].SetSelected(i == index);
            }

            if (_confirmButton != null) _confirmButton.interactable = true;
        }

        public void OnConfirmClick()
        {
            if (_selectedIndex == -1 || _hasVoted) return;

            _hasVoted = true;
            if (_confirmButton != null) 
            {
                _confirmButton.interactable = false;
                var txt = _confirmButton.GetComponentInChildren<TMP_Text>();
                if (txt != null) txt.text = "Voted";
            }

            foreach (var item in _spawnedItems) item.SetInteraction(false);

            // Gửi vote lên server
            _manager.SubmitVote(_selectedIndex);
        }
    }
}