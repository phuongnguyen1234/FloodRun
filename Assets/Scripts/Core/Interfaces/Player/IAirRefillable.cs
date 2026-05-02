using UnityEngine;

public interface IAirRefillable
{
    // Nạp thêm khí (Bonus Air)
    bool AddBonusAir(float amount);
    
    // Phát âm thanh thu thập (để Player tự xử lý việc phát qua AudioSource của mình)
    void PlaySound(AudioClip clip);
}