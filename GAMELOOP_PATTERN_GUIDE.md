# Generic Gameloop Pattern - Implementation Guide

## Tổng quan

Để tránh duplicate logic giữa Single Player và Multiplayer, chúng ta sử dụng interface base `IGameLoopManager`. Cả `SingleplayerManager` (SP) và `MultiplayerManager` (MP) đều implement interface này.

## Architecture

```
IGameLoopManager (Base Interface)
├── LocalPlayer { get; }
├── AllPlayers { get; }
├── IsGameActive { get; }
├── IsPaused { get; }
├── IsHost { get; }
└── IsMultiplayer { get; }
    ├── SingleplayerManager : IGameplayManager, IGameLoopManager
    │   └── (Single Player specific: IsHost=true, IsMultiplayer=false)
    │
    └── MultiplayerManager : IMultiplayerManager, IGameLoopManager
        └── (Multiplayer: IsHost=IsServer, IsMultiplayer=true)
```

## Cách sử dụng

### 1. Mechanics phụ thuộc vào Game State

Thay vì:

```csharp
public class SomeController : MonoBehaviour {
    private IGameplayManager _gameplayManager;

    private void OnEnable() {
        _gameplayManager = FindObjectsByType<Component>().OfType<IGameplayManager>().FirstOrDefault();
    }
}
```

**Sử dụng:**

```csharp
public class SomeController : MonoBehaviour {
    private IGameLoopManager _gameLoopManager;

    private void OnEnable() {
        _gameLoopManager = FindObjectsByType<Component>().OfType<IGameLoopManager>().FirstOrDefault();
    }

    private void Update() {
        if (_gameLoopManager?.LocalPlayer != null && _gameLoopManager.LocalPlayer.IsSubmerged) {
            // Do something
        }
    }
}
```

### 2. Kiểm tra Mode (SP vs MP)

```csharp
if (_gameLoopManager.IsMultiplayer) {
    // MP specific logic
    foreach (var player in _gameLoopManager.AllPlayers) {
        // Handle multiple players
    }
} else {
    // SP specific logic
}
```

### 3. Kiểm tra Host Role

```csharp
if (_gameLoopManager.IsHost) {
    // Server-side logic (SP always true, MP only host)
    // Example: Process game state changes
}
```

## Integration Checklist

- [x] Created `IGameLoopManager` interface
- [x] `IGameplayManager` extends `IGameLoopManager`
- [x] `IMultiplayerManager` extends `IGameLoopManager`
- [x] `SingleplayerManager` implements `IGameplayManager` + `IGameLoopManager` properties
- [x] `MultiplayerManager` implements `IMultiplayerManager` + `IGameLoopManager` properties
- [x] Updated `FloodController` to use `IGameLoopManager` instead of `IGameplayManager`

## Next Steps: Convert Other Systems

Any system currently using `IGameplayManager` should be converted to use `IGameLoopManager`:

1. **Search for usages:** `grep -r "IGameplayManager" Assets/Scripts/`
2. **Replace with:** `IGameLoopManager`
3. **Test in both modes:** Single Player scene + Multiplayer room

### Example systems to convert:

- Player controllers
- UI managers
- Camera systems
- Audio managers
- Any mechanic that needs "Is player currently in game?"
