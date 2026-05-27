# UI Architecture Pattern - Implementation Guide

## Tổng quan

Để tránh duplicate logic UI giữa Single Player và Multiplayer, chúng ta sử dụng 2 interface base: `ICommonUIManager` và `IGameplayHUDUI`. Cả `GameplayUIManager` (SP) và `MultiplayerUIManager` (MP) đều implement các interface này.

## Architecture

```
IUISfxPlayer (Base SFX player)
│
├── ICommonUIManager (Base - notification, loading)
│   ├── ShowNotification()
│   ├── ShowLoadingScreen()
│   ├── SetupLoadingScreen()
│   └── ShowBackToMainMenuLoadingScreen()
│
└── IGameplayHUDUI extends ICommonUIManager (Gameplay HUD chung)
    ├── UpdatePersonalTime()
    ├── SetMaxTime()
    ├── UpdateAirUI()
    ├── UpdateButtonProgress()
    ├── SetCountdownText()
    └── UpdateTimeSlider()
        │
        ├── GameplayUIManager : IGameplayUIManager
        │   ├── extends IGameplayHUDUI
        │   ├── ShowEndGame() [SP-specific]
        │   ├── ShowPauseMenu() [SP-specific]
        │   ├── Dev tools methods [SP-specific]
        │   └── (Single Player specific features)
        │
        └── MultiplayerUIManager : IMultiplayerUIManager
            ├── extends IGameplayHUDUI
            ├── Chat system [MP-specific]
            ├── RoomInfo, Settings [MP-specific]
            ├── SetHUDMode(), UpdatePlayStatus() [MP-specific]
            └── (Multiplayer specific features)
```

## Cách sử dụng

### 1. Trong Mechanics (chung cho SP & MP)

Thay vì chỉ dùng `IGameplayUIManager` (chỉ cho SP), mechanics có thể dùng `IGameplayHUDUI`:

```csharp
public class FloodController : MonoBehaviour {
    private IGameplayHUDUI _gameplayHUD;

    private void OnEnable() {
        _gameplayHUD = FindObjectsByType<Component>().OfType<IGameplayHUDUI>().FirstOrDefault();
    }

    private void Update() {
        if (_gameplayHUD != null) {
            _gameplayHUD.UpdateAirUI(currentAir, bonusAir, bonusMax, rate);
            _gameplayHUD.UpdateButtonProgress(pressed, total);
        }
    }
}
```

### 2. Trong GameplayManager (Single Player)

```csharp
public class GameplayManager : IGameplayManager {
    private IGameplayUIManager _uiManager;

    private void OnEnable() {
        _uiManager = FindObjectsByType<Component>().OfType<IGameplayUIManager>().FirstOrDefault();
    }

    private void OnGameEnd(bool isVictory) {
        _uiManager.ShowEndGame(isVictory, reason, mapData, time, buttons, isNewBest, coins);
        _uiManager.ShowPauseMenu(false); // SP-specific
    }
}
```

### 3. Trong MultiplayerManager (Multiplayer)

```csharp
public class MultiplayerManager : IMultiplayerManager {
    private IMultiplayerUIManager _uiManager;

    private void OnEnable() {
        _uiManager = FindObjectsByType<Component>().OfType<IMultiplayerUIManager>().FirstOrDefault();
    }

    private void OnRoundStart() {
        _uiManager.SetHUDMode(true); // Chuyển sang Gameplay HUD
        _uiManager.UpdateAlivePlayerCount(activeCount, totalCount);
    }

    private void OnChatMessage(string sender, string message, bool isHost) {
        _uiManager.AddChatMessage(sender, message, isHost); // MP-specific
    }
}
```

### 4. Gọi Common UI methods (cả SP & MP)

```csharp
// Cả hai có thể dùng (via ICommonUIManager hoặc IGameplayHUDUI)
_uiManager.ShowNotification("Game paused!", Color.yellow);
_uiManager.ShowLoadingScreen(true);
_uiManager.SetupLoadingScreen(mapData);
```

## Integration Checklist

- [x] Created `ICommonUIManager` interface (base for all UI)
- [x] Created `IGameplayHUDUI` interface (extends ICommonUIManager)
- [x] `IGameplayUIManager` extends `IGameplayHUDUI` (SP-specific)
- [x] `IMultiplayerUIManager` extends `IGameplayHUDUI` (MP-specific)
- [ ] Update `GameplayUIManager` class to implement `IGameplayUIManager`
- [ ] Update `MultiplayerUIManager` class to implement `IMultiplayerUIManager`
- [ ] Convert mechanics using `IGameplayUIManager` → `IGameplayHUDUI` (nếu không cần SP-specific)

## Next Steps: Convert Mechanics

### Mechanics hiện tại dùng UI:

Tìm kiếm: `grep -r "IGameplayUIManager" Assets/Scripts/Mechanics/`

**Quy tắc chuyển đổi:**

1. **Nếu mechanic chỉ dùng chung methods** (UpdateAirUI, UpdateButtonProgress, etc.)
   - Thay: `IGameplayUIManager` → `IGameplayHUDUI`

2. **Nếu mechanic dùng SP-specific methods** (ShowEndGame, ShowPauseMenu, dev tools)
   - Giữ: `IGameplayUIManager` (chỉ dùng trong SP)

### Example:

**Before (chỉ SP hoạt động):**

```csharp
private IGameplayUIManager _uiManager;
_uiManager = FindObjectsByType<Component>().OfType<IGameplayUIManager>().FirstOrDefault();
```

**After (hoạt động cả SP & MP):**

```csharp
private IGameplayHUDUI _uiManager;
_uiManager = FindObjectsByType<Component>().OfType<IGameplayHUDUI>().FirstOrDefault();
```

## Design Benefits

✔️ **Avoid Duplication** - Methods chung được định nghĩa 1 lần  
✔️ **Type Safety** - Compile-time checking thay vì FindObject  
✔️ **Easy Testing** - Mock implementation cho từng interface  
✔️ **Clear Responsibility** - Interface name rõ ràng về scope (Common, Gameplay HUD, SP, MP)  
✔️ **Future-proof** - Dễ thêm feature mới mà không break codebase

## Common Mistakes to Avoid

❌ **Nhầm lẫn interface:**

```csharp
// SAI: Mechanics dùng SP-specific UI
private IGameplayUIManager _uiManager; // ❌ Mechanic không phải SP-only!
```

✅ **Đúng cách:**

```csharp
// ĐÚNG: Mechanics dùng chung HUD UI
private IGameplayHUDUI _uiManager; // ✅ Hoạt động cả SP & MP
```

❌ **FindObjectsByType từ mechanics:**

```csharp
// SAI: Tìm kiếm trực tiếp
var ui = FindObjectsByType<GameplayUIManager>().FirstOrDefault();
```

✅ **Đúng cách:**

```csharp
// ĐÚNG: Tìm kiếm qua interface
var ui = FindObjectsByType<Component>().OfType<IGameplayHUDUI>().FirstOrDefault();
```
