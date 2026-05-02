using UnityEngine;
using TMPro;
using Core;

namespace UI
{
    /// <summary>
    /// Quản lý giao diện chọn nhân vật và mua skin.
    /// Hiện tại hỗ trợ hiển thị số xu và lệnh đóng màn hình.
    /// </summary>
    public class CharacterUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text _totalCoinsText;

        private void OnEnable()
        {
            RefreshCoins();
        }

        /// <summary>
        /// Tải dữ liệu người chơi và cập nhật hiển thị tiền xu.
        /// </summary>
        public void RefreshCoins()
        {
            PlayerProfile profile = DataManager.Instance.Profile;
            if (_totalCoinsText != null)
            {
                _totalCoinsText.text = profile.TotalCoins.ToString();
            }
        }

        public void Close()
        {
            if (HomeUIManager.Instance != null)
            {
                HomeUIManager.Instance.ShowHomeScreen();
            }

            Destroy(gameObject);
        }
    }
}