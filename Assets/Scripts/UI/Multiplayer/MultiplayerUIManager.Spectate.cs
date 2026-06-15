using Core.Interfaces;
using UnityEngine;

namespace UI.Multiplayer
{
    public partial class MultiplayerUIManager
    {
        public void UpdateSpectateStatus(bool isSpectating)
        {
            if (_spectateStatusText != null)
                _spectateStatusText.text = isSpectating ? "Stop spectating" : "Spectate";

            if (_spectateIcon != null)
            {
                _spectateIcon.sprite = isSpectating ? _stopSpectateSprite : _spectateSprite;
                _spectateIcon.color = isSpectating ? _spectateActiveColor : _spectateNormalColor;
            }

            // Spectate_Btns (chứa 2 nút next/previous) chỉ hiện khi đang spectate
            if (_spectateControls != null)
            {
                _spectateControls.SetActive(isSpectating);
            }
        }

        #region Lobby HUD Actions

        /// <summary>
        /// Kích hoạt/Tắt chế độ theo dõi (Spectate) cho Local Player.
        /// Nút này chỉ có thể bấm khi đang ở Lobby và trò chơi đang diễn ra (GameState.Playing).
        /// </summary>
        public void OnSpectateToggleClick()
        {
            PlayClickSound();
            _logicManager?.LocalPlayer?.ToggleSpectateStatus();
        }

        /// <summary>
        /// Chuyển camera theo dõi sang người chơi còn sống tiếp theo.
        /// Thường được gán vào nút "Next" trong Spectate Controls.
        /// </summary>
        public void OnNextSpectateClick()
        {
            PlayClickSound();
            _logicManager?.CycleSpectateTarget(1);
        }

        /// <summary>
        /// Chuyển camera theo dõi sang người chơi còn sống trước đó.
        /// Thường được gán vào nút "Prev" trong Spectate Controls.
        /// </summary>
        public void OnPrevSpectateClick()
        {
            PlayClickSound();
            _logicManager?.CycleSpectateTarget(-1);
        }

        #endregion
    }
}
