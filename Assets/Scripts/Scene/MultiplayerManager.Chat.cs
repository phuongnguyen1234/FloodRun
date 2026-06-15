using Unity.Netcode;

namespace Multiplayer
{
    /// <summary>
    /// Partial class xử lý các logic mạng liên quan đến hệ thống Chat của Multiplayer.
    /// Nhận thông điệp từ local player, chuyển tiếp lên Server và broadcast về tất cả các Client.
    /// </summary>
    public partial class MultiplayerManager
    {
        /// <summary>
        /// Gửi tin nhắn chat từ Local Player.
        /// </summary>
        /// <param name="message">Nội dung tin nhắn</param>
        public void SendChatMessage(string message) 
        { 
            if (string.IsNullOrWhiteSpace(message)) return;
            SendChatMessageServerRpc(message);
        }

        /// <summary>
        /// ServerRpc nhận tin nhắn từ một Client bất kỳ.
        /// Trích xuất thông tin người gửi từ PlayerDataList và gọi ClientRpc để broadcast tin nhắn.
        /// </summary>
        /// <param name="message">Nội dung tin nhắn</param>
        /// <param name="rpcParams">Thông số RPC tự động (chứa ID của người gửi)</param>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SendChatMessageServerRpc(string message, RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            
            string senderName = "Player " + senderId;
            bool isHost = false;
            
            foreach (var data in PlayerDataList)
            {
                if (data.ClientId == senderId)
                {
                    senderName = data.PlayerName.ToString();
                    isHost = data.IsHost;
                    break;
                }
            }

            ReceiveChatMessageClientRpc(senderName, message, isHost);
        }

        /// <summary>
        /// ClientRpc gửi thông tin chat từ Server về tất cả các Client và Host để hiển thị lên UI.
        /// </summary>
        /// <param name="senderName">Tên người gửi đã được phân giải tên</param>
        /// <param name="message">Nội dung tin nhắn</param>
        /// <param name="isHost">Người gửi có phải là Host hay không</param>
        [Rpc(SendTo.ClientsAndHost)]
        private void ReceiveChatMessageClientRpc(string senderName, string message, bool isHost)
        {
            _uiManager?.AddChatMessage(senderName, message, isHost);
        }
    }
}
