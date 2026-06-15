using UnityEngine;
using Unity.Netcode;

namespace Core.Interfaces 
{
    public enum DeathReason
    {
        Drowned,        // Default death in flood or general player death
        Explosion,      // Died due to explosion (e.g., from an explosive button)
        FellOffWorld,   // Fell below kill Y threshold
        TimeOut,        // Exceeded max time (though this is a game over, not player death)
        Reset,
        Other           // Generic or unknown reason
    }

    public enum PlayerStatus
    {
        Lobby,          // Đang ở khu vực chờ
        InGame,         // Đang thực sự tham gia chạy đua trong Map
        Spectating,     // Đang quan sát người khác
        Finished,       // Đã về đích (đợi round kết thúc)
        Dead            // Đã chết (đợi respawn về Lobby)
    }

    /// <summary>
    /// Giao diện IPlayer đại diện cho player trong game, cung cấp các thuộc tính và phương thức cần thiết để quản lý trạng thái và hành động của player.
    /// Kế thừa IPlayerAbility để có sẵn hàm Enable/DisableAbility
    /// </summary>
    public interface IPlayer : IPlayerAbility
    {
        bool IsDead { get; }
        bool IsSpawned { get; }
        bool IsLocalPlayer { get; }
        NetworkVariable<bool> NetworkIsDead { get; }
        NetworkVariable<bool> IsAFK { get; } // Trạng thái AFK của người chơi
        NetworkVariable<PlayerStatus> Status { get; } // Trạng thái/Vị trí tổng quát của người chơi
        
        void ForceDieClientRpc(DeathReason reason); // Hàm để ép client thực hiện việc chết (dùng trong trường hợp server muốn ép client chết ngay lập tức, bypass các điều kiện kiểm tra trên client)
        bool IsZiplining { get; }
        IFloodZone CurrentFlood { get; } // Thêm để FloodController có thể truy vấn

        bool IsClinging { get; }
        bool IsSwimming { get; } // Thêm thuộc tính này để BackgroundMusicManager có thể kiểm tra trạng thái bơi của player
        bool IsSubmerged { get; } // Thêm thuộc tính này để FloodController có thể kiểm tra trạng thái ngập trong nước của player
        bool IsClimbing { get; }
        GameObject gameObject { get; } // Cho phép truy cập GameObject của player
        DeathReason LastDeathReason { get; } // Lý do chết gần nhất
        void Die(DeathReason reason = DeathReason.Drowned); // Hàm để các manager bên ngoài (như MapManager) có thể ép player chết
        void Revive(); // Hàm để revive player sau khi đã chết (dùng cho respawn)
        void SetInvincible(bool isInvincible); // Set trạng thái bất tử (không trừ khí/không chết do môi trường)
        void ResetAir(); // Reset air về 100 và xóa bonus air

        // Thêm properties để MapManager đọc thông số hiển thị UI mà không cần truy cập PlayerController
        float CurrentBaseAir { get; }
        float CurrentBonusAir { get; }

        float CurrentBonusAirMax { get; }
        float CurrentAirChangeRate { get; } // Tốc độ thay đổi khí (+ hoặc - mỗi giây)

        // Cho phép MapManager hoặc UIManager cập nhật trạng thái bơi của player
        void SetInfiniteAir(bool enabled);
    
        void SetInfiniteJump(bool enabled);

        void Teleport(Vector3 position);

        void ToggleAFKStatus();
        void ToggleSpectateStatus();

        bool IsInputBlocked { get; }
        void SetInputBlocked(bool isBlocked); // Hàm để khóa/mở khóa input của player, dùng khi mở modal hoặc trong các tình huống đặc biệt khác

        /// <summary>Reset trạng thái gameplay khi bắt đầu round mới (MP).</summary>
        void PrepareForNewRound();

        // --- Attribute Setters (Unify access for Actions) ---
        void SetSpeed(float speed);
        void SetJumpForce(float force);
        void SetGravityScale(float scale);
        void ResetGravityScale();
        void SetMaxAir(float max);
        void AddAir(float amount);
        void SetFacing(bool faceRight);
        void SetStatus(PlayerStatus newStatus);
    }
}