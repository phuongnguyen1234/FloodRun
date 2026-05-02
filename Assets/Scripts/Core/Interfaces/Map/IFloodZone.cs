using UnityEngine;

public enum FloodType
{
    Water,
    Lava,
    Acid,
    NoSwim,
}

public interface IFloodZone
{
    FloodType Type { get; }
    float AirDrainRate { get; }
    AudioClip SplashSound { get; }
    bool NoSwim { get; }
    bool ApplyDepthMultiplier { get; }
    float GetDepthMultiplier(float playerY);
}