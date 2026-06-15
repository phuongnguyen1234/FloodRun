using Unity.Netcode;
using Unity.Collections;
using System;
using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// Cấu trúc dữ liệu người chơi đồng bộ qua mạng.
    /// </summary>
    public struct PlayerNetworkData : INetworkSerializable, IEquatable<PlayerNetworkData>
    {
        public ulong ClientId;
        public FixedString32Bytes PlayerName;
        public bool IsHost;
        public bool IsAFK;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerName);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref IsAFK);
        }

        public bool Equals(PlayerNetworkData other) => ClientId == other.ClientId;
    }

    /// <summary>
    /// Các trạng thái chính của Session trong Multiplayer.
    /// Intermission: Chờ giữa các ván (tương đương Lobby cũ).
    /// Voting: Đang bình chọn map.
    /// Playing: Đang chạy map.
    /// </summary>
    public enum GameState { 
        /// <summary>
        /// Giai đoạn chờ giữa các ván đấu, tương đương với Lobby truyền thống. 
        /// Tại đây, người chơi có thể trò chuyện, chuẩn bị và chờ đợi cho đến khi ván đấu tiếp theo bắt đầu. 
        /// Đây cũng là lúc để hiển thị thông tin về ván đấu sắp tới, như map sẽ chơi, thời gian bắt đầu, và danh sách người chơi đã sẵn sàng.
        /// </summary>
        Intermission, 

        /// <summary>
        /// Giai đoạn bình chọn map, nơi người chơi có thể lựa chọn map tiếp theo sẽ chơi. 
        /// Trong giai đoạn này, UI sẽ hiển thị danh sách các map có thể bình chọn cùng với số phiếu bầu của mỗi map. 
        /// Người chơi có thể thay đổi phiếu bầu của mình cho đến khi thời gian bình chọn kết thúc. 
        /// Sau khi kết thúc, map có số phiếu cao nhất sẽ được chọn để bắt đầu ván đấu tiếp theo.
        /// </summary>
        Voting, 

        /// <summary>
        /// Giai đoạn chơi, nơi ván đấu đang diễn ra. 
        /// Trong giai đoạn này, người chơi sẽ tham gia vào map đã được chọn và trải nghiệm gameplay. 
        /// UI sẽ hiển thị thông tin về thời gian còn lại của ván đấu, điểm số, và các yếu tố liên quan đến gameplay. 
        /// Khi ván đấu kết thúc, hệ thống sẽ tự động chuyển sang giai đoạn Intermission để chuẩn bị cho ván đấu tiếp theo.
        /// </summary>
        Playing }



    /// <summary>
    /// Interface dành riêng cho các tính năng quản lý mạng và phòng.
    /// Extends IGameLoopManager để cung cấp các tính năng gameplay cơ bản.
    /// </summary>
    public interface IMultiplayerManager : IGameLoopManager
    {
        NetworkVariable<float> NetworkTime { get; }
        NetworkVariable<FixedString32Bytes> RoomId { get; }
        NetworkVariable<FixedString32Bytes> Passcode { get; }
        NetworkList<PlayerNetworkData> PlayerDataList { get; }

        NetworkVariable<float> Difficulty { get; }
        NetworkList<FixedString64Bytes> VotingMapNames { get; }
        NetworkList<int> MapVotes { get; }

        void RequestStartGame();
        void SubmitVote(int mapIndex);
        void SendChatMessage(string message);
        
        /// <summary>
        /// Yêu cầu chuyển hướng theo dõi Camera sang một người chơi khác đang còn sống.
        /// </summary>
        /// <param name="direction">Hướng chuyển: 1 (Người kế tiếp), -1 (Người trước đó)</param>
        void CycleSpectateTarget(int direction);
        
        // Để các UI biết đang ở giai đoạn nào (Intermission/Voting/Playing) và lấy enum GameState
        GameState GetCurrentGameState(); 
        void SetRoomInfo(string roomId, string passcode);

        void RequestKickPlayer(ulong clientId);
        void RequestLeaveRoom();
        void RequestResetPlayer();
    }
}