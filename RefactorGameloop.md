# Refactor gameloop

- Lý do refactor: file MultiplayerManager.cs hoạt động chưa tốt, fix bug mãi chưa được

## Giai đoạn Intermission:

- Là giai đoạn đợi có ít nhất 1 player không AFK
- Nếu có bất kỳ player nào join phòng, đặt trạng thái là AFK
- Hệ thống lắng nghe event nếu có bất kỳ player nào thay đổi trạng thái

## Giai đoạn Voting:

- Bắt đầu khi hệ thống nhận thấy ít nhất 1 player active
- Lấy tier và độ khó của phiên hiện tại để lấy 3 map bất kỳ
- Hiển thị nút Vote map với tất cả mọi người ở tất cả trạng thái, đang ở lobby hay gameplay. Ấn nút Vote map để hiển thị VoteMapModal. Player vào phòng trong giai đoạn này cũng được thấy nút Vote
- Mỗi player được vote 1 lần, modal giữ trạng thái vote (chưa vote/đã vote)
- Sau 10s, kiểm tra lại phải có ít nhất 1 player active. Nếu không thì quay về Intermission
- Map có lượt vote cao nhất sẽ được chọn để chơi. Nếu có ít nhất 2 map có lượt vote cao nhất, lấy random

## Giai đoạn Setup round:

- Hiển thị loading map panel cho tất cả player active
- Clear map trước đó (nếu có), xóa sạch tham chiếu liên quan đến round trước.
- Load map được chọn, setup tham chiếu dữ liệu map để quản lý. Lưu ý thứ tự gán tham chiếu
- Teleport active player vào PlayerSpawn của map
- Đưa tất cả active player vào danh sách player tham gia round ban đầu và danh sách player còn lại
- Hiển thị và làm sạch GameplayHUD: Hiển thị air (số và slider), tổng số player còn lại, số thứ tự nút cần ấn (1), ẩn các image lá cờ, setup thời gian cá nhân và thời gian kỷ lục trên map (Giống singleplayer)
- Reset air, khóa input, bật bất tử
- Gán camera cho các lớp background có ParallaxEffect
- Do mỗi player sẽ tự load map của riêng mình, nên có tối đa 20s để load và setup. Nếu hết thời gian, hiển thị thông báo float Load timeout! và quay về lobby, loại bỏ khỏi danh sách player tham gia round ban đầu và danh sách player còn lại.
- Player nào đã tự mình load xong map, setup lần đầu xong thì ẩn map loading panel. Báo cho manager là player này đã khởi động xong.
- Hiển thị thông báo Waiting for players (số player đã load xong/số player tham gia ban đầu), đợi đến khi tất cả player load xong hoặc không còn ai đang load nữa thì bắt đầu đếm ngược 3s

## Giai đoạn Playing:

- Kết thúc 3s đếm ngược, bắt đầu round với luồng như singleplayer.
- Player nào ấn nút thì gửi lên server để validate, nếu ok thì mới kích hoạt button cho tất cả, hiển thị thông báo float Pressed button X cho player ấn nút đó, đồng thời cập nhật thứ tự nút cần ấn trong gameplayHUD tất cả player.
- Trong quá trình diễn ra round, hệ thống lắng nghe thay đổi danh sách player còn lại. Bất kỳ player nào win map, chết (do map hoặc reset character), thoát phòng đều sẽ bắn event tương ứng. Mỗi khi danh sách player còn lại thay đổi, kiểm tra điều kiện kết thúc round. Player chết thì cứ bắn event đã, respawn sau bao lâu thì kệ họ
- Điều kiện kết thúc round: tất cả player win, tất cả player chết, 1 phần player chết và win, hết thời gian tối đa map, không còn ai trong danh sách player còn lại.
- Khi kết thúc round, tính độ khó round tiếp theo dựa trên scale: 100% win thì +0.4, 0% win thì -0.5
- Quay về Voting

* Đây mới là core loop, là mục tiêu chính và tối thiểu cần đạt được. Ngoài ra sẽ phải có thêm SummaryModal cho player khi chết, thống kê lượt chơi... nhưng sẽ triển khai sau khi core loop ổn định
* Multiplayer không có pause gameplay
