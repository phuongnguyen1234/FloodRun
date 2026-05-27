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

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerName);
            serializer.SerializeValue(ref IsHost);
        }

        public bool Equals(PlayerNetworkData other) => ClientId == other.ClientId;
    }

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

        // Bạn có thể thêm các hàm như StartVote, KickPlayer tại đây
        void RequestStartGame();
        void SendChatMessage(string message);
        
        // Để các UI biết đang ở giai đoạn nào (Lobby/Voting/Playing)
        int GetCurrentGameState(); 
        void SetRoomInfo(string roomId, string passcode);

        void RequestKickPlayer(ulong clientId);
        void RequestLeaveRoom();
        void RequestResetPlayer();
    }
}