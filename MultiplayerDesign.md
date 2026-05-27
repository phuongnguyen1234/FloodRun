# 1. Tại Home Scene

## 1.1. Multiplayer Tab (DONE)

- Nút Multiplayer mở màn hình MultiplayerUI
- 2 tab chính: **Create Room** (CreateRoomSectionUI) và **Join Room** (JoinRoomSectionUI)

## 1.2. CreateRoomSectionUI (DONE)

- **Tạo phòng:**
  - Chọn số người chơi tối đa từ dropdown
  - Passcode (tùy chọn): 4-6 ký tự số, enable qua checkbox
  - ID phòng: 6 chữ số ngẫu nhiên
- **Khi ấn Create:**
  - Tạo phòng với thông tin Host từ PlayerData (avatar, tên)
  - Đưa host vào Multiplayer scene (mục 2)

## 1.3. JoinRoomSectionUI (DONE)

- **Danh sách phòng:**
  - Text: "Rooms on this network (Last updated: X seconds ago)" + nút Refresh
  - Cập nhật tự động: 30s hoặc khi có phòng mới
  - RoomList (Scroll View) hiển thị RoomItem prefabs
- **RoomItem prefab hiển thị:**
  - Avatar + tên host
  - Room ID
  - Số player hiện tại / tối đa
  - Format: 2 text "PlayerName's Room (1/6)" và "Room ID: 123456"
  - Nút Join

- **Join phòng qua ID:**
  - Input ID + nút Join riêng
  - Lỗi → NotificationModal (mục 5.1):
    - "Room is full"
    - "Room not found or no longer exist"

- **Xử lý Passcode:**
  - Nếu room có passcode → EnterPasscodeModal (mục 5.2)
  - Passcode sai → NotificationModal "Incorrect passcode"
  - Passcode đúng → JoinRoom_Loading hiện lên, tắt khi load xong

# 2. Multiplayer Scene

## 2.1. Cấu trúc Scene

- Loại: World Persistence (chứa cả Lobby + Gameplay)
- **Lobby:** Vị trí (0, 0)
- **Gameplay:** Vị trí (~1000, 1000)
- Lobby có object LobbySpawn, giống PlayerSpawn. Đây là nơi player mới vào phòng hay player respawn từ gameplay về đây
- Lobby cũng có 1 object Sprite và Text (World, không phải UI), Sprite hiển thị ảnh map đang chơi, text tên map, sprite màu độ khó làm background cho text độ khó, text "{Số player còn sống/Số player vào map ban đầu} Players", text độ khó "Easy: 1.0"
- Text số player có thể trở thành Waiting for players khi ở thời gian vote map

## 2.2. Giao Diện Chung (Common UI)

### Thanh Slider Thời Gian

- Dùng cho: đếm ngược vote map, đếm ngược map
- Màu ruột trắng: thời gian map
- Màu ruột vàng: thời gian vote map

### Nút Chat

- **Chức năng:**
  - Toggle đóng/mở hộp chat
  - Message_ScrollView + Message_Input
- **Hiển thị tin nhắn:**
  - Format: "PlayerName: Message"
  - Nếu là Host: "[Host] PlayerName: Message"
  - Input: Enter để gửi
- **Giới hạn:** 50 Chat_Line objects tối đa (destroy cũ khi thêm mới, hoặc dùng pooling)

### Nút Room Info

- **Modal hiển thị:**
  - Room ID
  - Passcode (nếu có)
  - Danh sách player (Scroll View)
  - Nút Leave room
- **PlayerItem prefab:**
  - Avatar + tên player
  - "[Host]" prefix nếu là host
  - Nút Kick (chỉ host thấy, dùng để kick clients)
- **Leave room:** mở ConfirmationModal (mục 5.3) để xác nhận

### Nút Settings

- Mở Settings_Modal (có sẵn)

### Map Voting UI

- **Hiển thị:**
  - 2 Text: "Map name (6/6)" và "Easy: 1.0"
  - Nút "Vote this map" (chỉ enable ở giai đoạn vote)
- **Chi tiết:** xem mục 3.3

## 2.3. Lobby HUD

- **Nút chính:**
  - Play/AFK (toggle, thay đổi icon/text)
  - Shop (mở CharacterUI)
  - Spectate (toggle)
- **Spectate controls:** Previous/Next buttons
- **Logic:** Nếu AFK → không đưa vào gameplay sau vote map
- **Ẩn khi:** Enter gameplay

## 2.4. Gameplay HUD

- **Dựa trên:** GameplayUIManager từ singleplayer
- **Thêm:** Số player đang sống sót
- **Gợi ý:** Tách logic chung để singleplayer & multiplayer dùng chung

# 3. Logic

## 3.1. Player Active/AFK Status

- **Mặc định khi vào:**
  - Thành viên bình thường: Active
  - Host (lần đầu sau tạo phòng): AFK (chờ player khác)
- **Khi không có player active:** Phiên tạm dừng trước giai đoạn vote map
- **Note:** Tìm logic tốt hơn nếu cần (thay vì host mặc định AFK). Hoặc logic có thể là Host ấn nút Start game riêng để bắt đầu vote map

## 3.2. Game Loop

1. **Bắt đầu vote map**
2. **Sau khi chốt map:** Khởi tạo map, đưa tất cả player active vào
3. **Gameplay:** Diễn ra như singleplayer
4. **Round kết thúc khi:**
   - Tất cả player sống sót/chết
   - Hết thời gian map
   - Số player đang chơi = 0 (do chết, thoát phòng, v.v.)
   - Player chết → Respawn ở Lobby → Hiện SummaryModal
5. **Sau khi kết thúc round:**
   - Tính lại độ khó phiên
   - Nếu player active ≥ 1 → Quay về bước 1

### Edge Cases

- **Join trong round:** Đưa vào Lobby
- **Load map timeout (>10s):** Đưa vào Lobby + thông báo "Loading timeout" (không tính vào hiệu suất)

## 3.3. Map Voting & Difficulty Scaling

- **Difficulty range:** 1.0 - 4.99 (hiện chưa có map difficulty ≥ 5)
- **Thay đổi sau mỗi round:**
  - 100% player sống sót → +0.4 độ khó
  - Giảm dần theo số player chết
  - 0% player sống sót → -0.5 độ khó
  - _Player thoát phòng giữa chừng/disconnect → tính là chết_

- **Voting flow:**
  1. 3 map ngẫu nhiên với tier tương ứng độ khó hiện tại
  2. Map có vote cao nhất → Được chọn
  3. Hòa ≥ 2 map → Chọn ngẫu nhiên
  4. _Player vote rồi thoát → Vote vẫn tính_

- **MapSelection prefab hiển thị:**
  - Ảnh map, tên map, độ khó, số lượt vote
  - Click → Đổi màu viền + Enable nút "Vote this map"
  - Confirm vote → Disable nút, text "Voted"

## 3.4. Summary Modal (Khi player chết)

- **Danh sách map chơi (Scroll View):**
  - Mỗi item: Tên map, tier độ khó
  - Thứ tự về đích của player
  - Số button đã ấn
  - Số xu nhận được
  - Thời gian hoàn thành
- **Dưới cùng:**
  - Tổng map hoàn thành (Win streak)
  - Tổng xu nhận được

## 3.5. Spectate Player

- **Chỉ player ở Lobby mới spectate được** (via LobbyHUD)
- **Điều khiển:** Previous/Next buttons để chuyển player
- **Khi player được spectate:**
  - Chết/Thoát phòng → Tự động chuyển player khác (hoặc hủy nếu không còn)
- **Nút spectate disabled** nếu không có ai để spectate

# 4. Lưu Ý Chung

- **Networking:** Sử dụng Netcode for Gameobjects
- **Hệ thống hiện tại:** Singleplayer - cần tách riêng khi nâng lên multiplayer để tránh lỗi
- **Code organization:** Core folder chứa class/interface dùng chung, tránh tham chiếu vòng
- **Achievement & Stats:** Hoạt động như singleplayer
- **Kỷ lục thời gian per map:** Chỉ tính từ singleplayer
- **Xu (Currency):**
  - Cộng vào profile player
  - Commit chỉ khi hiện SummaryModal (lần thất bại cuối)
- **Button Press Notification:** Chỉ hiển thị với người ấn
  - Nút thường: +5 xu
  - Group button: +10 xu
- **Pause:** Multiplayer không thể pause game (chỉ pause gameloop khi không ai active)
- **Host disconnect:** Kick tất cả clients, quay về Home/Multiplayer + thông báo "Sorry, the host has left the room"
- **Kiến trúc phụ thuộc** Logic manager (GameplayManager, MultiplayerManager) có trách nhiệm điều khiển dữ liệu, logic và ra lệnh cho UI manager (GameplayUIManager, MultiplayerUIManager). Khi sử dụng, gọi interface của chúng, không được gọi trực tiếp class thực thi (Vì Manager logic thuộc assembly Scene, UI manager thuộc assembly UI). Các UI con (sub modal, màn hình con cần truy cập qua UI manager)

# 5. Scripts Bổ Sung

## 5.1. NotificationModal

- **Thành phần:** Nội dung thông báo + nút đóng
- **Sử dụng:** Modal chung toàn cục
- _Note: BaseModal đã handle đóng_

## 5.2. EnterPasscodeModal

- **Thành phần:** Input field + nút Confirm

## 5.3. ConfirmationModal

- **Thành phần:** Lời nhắc + 2 nút Yes/No
- **Sử dụng:** Modal chung toàn cục
- **Logic:** Nút Yes bắn event (listen ở nơi cần), No chỉ đóng modal

## 5.4. UI Scripts (nếu cần)

- ScrollView items
- Custom controls

## 5.5. Group Button Enhancement

- **Thêm vào ButtonController:** `IsGroupButton` (bool)
- **Hiển thị:**
  - Group button có ruột + mark màu xanh dương
  - Khi chuyển sang normal: hiển thị text "X/Y" (số player đã ấn / cần ấn)
- **Logic kích hoạt:**
  - Mặc định: Cần 2 player
  - Nếu player sống sót ≤ 3: Giảm còn 1 player
- **Singleplayer:** Group button cũng xuất hiện (mặc định "0/1")

## 5.6. Visual Player - Multiplayer Enhancement

- **Settings option:** "Show players" với 3 chế độ:
  - **Visible:** Luôn thấy các player khác
  - **Nearby:** Chỉ thấy rõ nếu xa, càng gần càng mờ cho đến vô hình (chỉ thấy nametag)
  - **None:** Chỉ hiển thị nametag player khác
- **Local player:** Hiển thị với outline bao quanh sprite (để highlight)

---

# 6. Edge Cases & Thiếu Sót Tiềm Năng

## 6.1. Room Creation & Joining

- ✅ **Min/Max players:** Max = 1 được (solo play với độ khó động)
- ✅ **Passcode:** 4-6 ký tự số (không case-sensitive)
- ✅ **Room name:** Không đặt tên riêng (format cố định: "PlayerName's Room")
- ✅ **Room expiry:** Tự động xóa nếu host offline
- ⚠️ **Race condition:** Nếu A & B join cùng lúc khi room còn 1 slot
  - **Gợi ý xử lý:**
    - **Option 1 (Server authority):** Server nhận 2 request, increment player count → check limit → reply (accept 1, reject 1)
    - **Option 2 (Host authority):** Host là NetworkObject owner, Host increment & broadcast player list → B không được add vì hết slot
    - **Option 3 (Timestamp-based):** Nếu join request cùng frame, dùng player ID để deterministic order
  - **Khuyến nghị:** Dùng Host authority (Option 2) vì simpler & Host là source of truth anyway

## 6.2. Network Sync & Disconnection

- ✅ **Reconnect logic:** Player join lại phòng với flow bình thường, mặc định vào Lobby (không mid-match reconnect)
- ❓ **Network timeout:** Nên đặt bao nhiêu lâu?
  - **Khuyến nghị:** 10-15 giây (Netcode for GameObjects mặc định ~10s). Áp dụng cho cả graceful disconnect detection
- ✅ **Graceful disconnect:** Chỉ cách duy nhất là Leave room (xác nhận modal)
- ✅ **Host migration:** Kick tất cả (đã đề cập trong thiết kế)

## 6.3. Voting & Map Selection

- ✅ **Vote after joining:** Player join giữa vote → có thể vote được, hiện nút "Vote this map" bình thường
- ✅ **Tie logic:** 2 maps hòa → random. 3+ maps hòa → cũng random (đã đề cập)
- ✅ **Vote manipulation:** Player vote rồi thoát → vote vẫn tính (công bằng, không ai cố tình làm nhiều lần)
- ✅ **Empty lobby voting:** Nếu vote map nhưng không ai active → **Skip round** và **giữ nguyên độ khó**

## 6.4. Difficulty Scaling

- ✅ **Clamping:** Min 1.0, Max 4.99
- ✅ **Rounding:** 2 chữ số thập phân (1.4 + 0.4 = 1.8, không làm tròn lên)
- ✅ **Formula:** Dựa trên % survivors
  - 100% sống → +0.4
  - Chia theo %: `survivors_count / total_count * 0.4`
    - Ví dụ: 3/5 = 60% → +0.24
    - Ví dụ: 2/4 = 50% → +0.2
  - 0% sống → -0.5

## 6.5. Player State & Respawning

- ✅ **Spectate while waiting:** Có, player ở Lobby có LobbyHUD với nút Spectate (đã đề cập)
- ✅ **All players die simultaneously:** Không quan trọng, respawn asynchronously (vẫn back to Lobby)
- ✅ **Rejoin after death:** Die → Lobby → Leave → Rejoin = **Active** (coi là join mới)

## 6.6. Chat & Communication

- ✅ **Chat persistence:** Host giữ chat data → có thể xem chat cũ khi rejoin
- ⏳ **Chat spam:** Cần limit
  - **Gợi ý:** 1 message / 1 giây per player (rate limit), max 1 message / 0.5s globally
  - Hoặc: Ignore message nếu text giống 100% với message trước đó (spam detection)
- ✅ **Chat visibility:** Tất cả player trong phòng

## 6.7. Xu (Currency) & Progress Tracking

- ✅ **Coin sync:** Commit khi thất bại lần cuối (SummaryModal), không dây dưa gameloop tiếp theo
- ✅ **Retry logic:** Khi thất bại lần cuối, xu được tính vào profile tại thời điểm đó
- ✅ **Button press xu:** Hiển thị +5 xu tạm thời khi ấn, tổng hợp trong SummaryModal để cộng thật
  - Nếu player lag → vẫn hiển thị +5 xu, sau này tổng hợp lại

## 6.8. Loading & Timeout

- ✅ **Timer starts:** Khi host load map xong
- ✅ **Partial client load:** Client A timeout → vào Lobby, Client B OK → vào Gameplay
  - Không ảnh hưởng round (teleport all active players ngay sau vote)
- ✅ **Timeout recovery:** A timeout vào Lobby
  - Nếu trong round sau: player active (bỏ A) = 0 → **round kết thúc, không trừ difficulty**
  - Ngược lại: round diễn ra bình thường với các player load được

## 6.9. Spectate Feature

- ✅ **Spectate order:** Host giữ danh sách player, **sort theo tên tăng dần** (alphabetically)
- ✅ **Spectate cascade:** A spectate B → B dies → auto switch to C → C leaves → continue hoặc exit nếu không còn
- ✅ **Spectate HUD:** Vẫn giữ **LobbyHUD của người spectate** (người theo dõi)

## 6.10. UI/UX Edge Cases

- ❓ **Button state consistency:** Non-issue (cần clarify nút nào)
- ✅ **Modal stacking:** Đã handle via BaseModal variant
- ✅ **Input buffer:** Non-issue - Join phòng là từ Home scene, server-side handling nên không lag input

## 6.11. Host Authority

- ✅ **Kick authority:** Host kick người khác, **không thể kick bản thân**
- ✅ **Host idle:** **Không ảnh hưởng** - gameloop chạy miễn số player active ≥ 1 (like server)
- ✅ **Host role change:** **Không dùng host migration** - chỉ kick all nếu host disconnect

## 6.12. Performance & Scalability

- ❓ **Max players tested:** Chưa test, chưa biết bottleneck
- ❓ **Concurrent rooms:** Host-client architecture, local network only (không clear limit)
- ✅ **Spectate rendering:** Mỗi player render trên màn hình riêng → **không ảnh hưởng** (local rendering)

## Gợi Ý Ưu Tiên Fix

**Critical (Must have):**

- Định nghĩa disconnect & reconnect logic
- Host migration strategy hoặc confirm kick-all logic
- Difficulty clamping (min/max)
- Vote behavior khi join mid-vote

**Important (Should have):**

- Chat spam prevention
- Coin sync confirmation
- Loading timeout edge cases
- Graceful modal conflict handling

**Nice-to-have:**

- Chat persistence
- Detailed logging cho debugging
- Rate limiting trên actions

---

# 7. Clarification Summary

## Fully Resolved (Phần 1-3) ✅

- ✅ 6.1 - Room creation, passcode, expiry, race condition → Host authority
- ✅ 6.2 - Reconnect (join lại), disconnect (Leave room), host (kick all), timeout = 10-15s
- ✅ 6.3 - Vote joining, tie logic (3+ random), empty lobby → skip + keep difficulty
- ✅ 6.4 - Difficulty: clamp [1.0-4.99], 2 decimals, survivors% \* 0.4 formula
- ✅ 6.5 - Respawn (async), spectate (LobbyHUD), rejoin = active
- ✅ 6.6 - Chat persistence (host), all players see, **need spam limit (1msg/s per player)**
- ✅ 6.7 - Currency: commit last failure, button presses show +5 temp → summarize in modal
- ✅ 6.8 - Load timer (after host), partial OK, timeout → skip if 0 active
- ✅ 6.9 - Spectate: **sorted by name**, cascade with auto-switch, keep LobbyHUD
- ✅ 6.10 - UI: BaseModal handles stacking, input buffer non-issue (server-side join)
- ✅ 6.11 - Host: kick others only, idle OK (gameloop = server), no migration
- ⏳ 6.12 - Performance: untested (max players, concurrent rooms), spectate rendering OK

## To-Do (Implementation)

1. **6.6 - Chat spam prevention:** Implement 1 msg/1s rate limit per player
2. **6.12 - Load testing:** Test max 6 players + stress test concurrent rooms
3. **UI button states:** Clarify which buttons need cross-client sync (if any)
