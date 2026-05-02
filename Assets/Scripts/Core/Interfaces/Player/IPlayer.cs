using UnityEngine;

namespace Core.Interfaces 
{
    /// <summary>
    /// Giao diện IPlayer đại diện cho player trong game, cung cấp các thuộc tính và phương thức cần thiết để quản lý trạng thái và hành động của player.
    /// Kế thừa IPlayerAbility để có sẵn hàm Enable/DisableAbility
    /// </summary>
    public interface IPlayer : IPlayerAbility
    {
        bool IsDead { get; }

        bool IsZiplining { get; }
        IFloodZone CurrentFlood { get; } // Thêm để FloodController có thể truy vấn

        bool IsClinging { get; }
        bool IsSwimming { get; } // Thêm thuộc tính này để BackgroundMusicManager có thể kiểm tra trạng thái bơi của player
        bool IsSubmerged { get; } // Thêm thuộc tính này để FloodController có thể kiểm tra trạng thái ngập trong nước của player
        GameObject gameObject { get; } // Cho phép truy cập GameObject của player
        void Die(); // Hàm để các manager bên ngoài (như MapManager) có thể ép player chết
        void SetInvincible(bool isInvincible); // Set trạng thái bất tử (không trừ khí/không chết do môi trường)

        // Thêm properties để MapManager đọc thông số hiển thị UI mà không cần truy cập PlayerController
        float CurrentBaseAir { get; }
        float CurrentBonusAir { get; }

        float CurrentBonusAirMax { get; }
        float CurrentAirChangeRate { get; } // Tốc độ thay đổi khí (+ hoặc - mỗi giây)

        // Cho phép MapManager hoặc UIManager cập nhật trạng thái bơi của player
        void SetInfiniteAir(bool enabled);
    
        void SetInfiniteJump(bool enabled);

        void Teleport(Vector3 position);
    }
}