using System.Collections.Generic;
using System.Linq;
using Core;
using Core.Interfaces;
using System.Collections;
using UnityEngine;

namespace Multiplayer
{
    public partial class MultiplayerManager
    {
        private IPlayer _currentSpectateTarget;
        private Coroutine _spectateSwitchCoroutine;

        /// <summary>
        /// Bắt đầu chế độ Spectate cho Local Player.
        /// Tìm người chơi đang InGame đầu tiên để theo dõi.
        /// </summary>
        private void StartSpectating()
        {
            if (_vcam == null) return;

            var alivePlayers = _activePlayers.Where(p => p.Status.Value == PlayerStatus.InGame).ToList();
            if (alivePlayers.Count > 0)
            {
                _currentSpectateTarget = alivePlayers[0];
                SwitchToTarget(_currentSpectateTarget);
            }
            else
            {
                // Nếu không có ai đang chơi, thoát spectate ngay
                LocalPlayer?.SetStatus(PlayerStatus.Lobby);
            }
        }

        /// <summary>
        /// Dừng chế độ Spectate.
        /// Trả camera về lại Local Player (đang đứng ở Lobby).
        /// </summary>
        private void StopSpectating()
        {
            _currentSpectateTarget = null;
            if (_vcam != null && LocalPlayer is MonoBehaviour localMono)
            {
                _vcam.Follow = localMono.transform;
                CameraHelper.WarpToTarget(_vcam, localMono);
            }
        }

        /// <summary>
        /// Chuyển đổi camera sang người chơi tiếp theo/trước đó.
        /// </summary>
        /// <param name="direction">1: Next, -1: Prev</param>
        public void CycleSpectateTarget(int direction)
        {
            if (LocalPlayer == null || LocalPlayer.Status.Value != PlayerStatus.Spectating) return;

            var alivePlayers = _activePlayers.Where(p => p.Status.Value == PlayerStatus.InGame).ToList();
            if (alivePlayers.Count == 0)
            {
                LocalPlayer.SetStatus(PlayerStatus.Lobby);
                return;
            }

            int currentIndex = alivePlayers.IndexOf(_currentSpectateTarget);
            if (currentIndex == -1) currentIndex = 0; // Target cũ đã chết/thoát, reset về 0

            int nextIndex = (currentIndex + direction + alivePlayers.Count) % alivePlayers.Count;
            _currentSpectateTarget = alivePlayers[nextIndex];

            SwitchToTarget(_currentSpectateTarget);
        }

        private void SwitchToTarget(IPlayer target)
        {
            if (_spectateSwitchCoroutine != null) StopCoroutine(_spectateSwitchCoroutine);
            _spectateSwitchCoroutine = StartCoroutine(SwitchSpectateTargetRoutine(target));
        }

        private IEnumerator SwitchSpectateTargetRoutine(IPlayer target)
        {
            if (_vcam == null || target == null || target is not MonoBehaviour targetMono) yield break;

            // 1. Gán Follow và thực hiện Warp logic
            _vcam.Follow = targetMono.transform;
            CameraHelper.WarpToTarget(_vcam, targetMono);

            // 2. QUAN TRỌNG: Đợi đến cuối frame để Cinemachine hoàn tất việc cập nhật 
            // Transform vật lý của Camera dựa trên vị trí của target.
            yield return new WaitForEndOfFrame();

            // 3. Lúc này Camera đã ở vị trí mới chính xác, Reset Parallax sẽ lấy mốc chuẩn
            if (CurrentMapManager != null)
            {
                CurrentMapManager?.PrepareMapBackgrounds();
            }

            _spectateSwitchCoroutine = null;
        }

        /// <summary>
        /// Kiểm tra liên tục xem người đang bị theo dõi có chết/thoát hay không,
        /// hoặc ván đấu đã kết thúc chưa để ngắt Spectate.
        /// (Được gọi từ Gameloop hoặc PlayerTracker)
        /// </summary>
        private void CheckSpectateState()
        {
            if (LocalPlayer == null || LocalPlayer.Status.Value != PlayerStatus.Spectating) return;

            // Tự động tắt nếu hết round
            if (CurrentState.Value != GameState.Playing)
            {
                LocalPlayer.SetStatus(PlayerStatus.Lobby);
                return;
            }

            // Nếu người đang xem không còn InGame, tự động chuyển người
            if (_currentSpectateTarget == null || _currentSpectateTarget.Status.Value != PlayerStatus.InGame)
            {
                CycleSpectateTarget(1);
            }
        }
    }
}
