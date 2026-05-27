using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Core;  // Tham chiếu đến namespace Core
using Core.Interfaces;
using UI;
using System.Linq;
using UnityEngine.EventSystems; // Thêm namespace để bắt sự kiện kéo/vuốt

/// <summary>
/// MapSelectionUI quản lý giao diện chọn bản đồ với cơ chế carousel, hỗ trợ lọc theo độ khó và tích hợp với hệ thống lưu trữ hồ sơ người chơi.
/// </summary>
public class MapSelectionUI : MonoBehaviour, IEndDragHandler, IBeginDragHandler, IDragHandler
{
    [Header("UI References")]
    [SerializeField] private Button _backButton;
    [SerializeField] private Button _leftButton;
    [SerializeField] private Button _rightButton;
    [SerializeField] private ScrollRect _scrollRect; // Tham chiếu đến ScrollRect

    [Header("Carousel Setup")]
    private RectTransform _contentContainer;       // Sẽ lấy từ _scrollRect.content
    [SerializeField] private GameObject _mapCardPrefab;       // Prefab của 1 Map Card
    [SerializeField] private GameObject _comingSoonPrefab;    // Prefab của "Coming Soon" panel
    [SerializeField] private float _visualCardWidth = 1100f;  // Chiều rộng thực tế của Card (để căn giữa)
    [SerializeField] private float _cardStepWidth = 1150f;    // Chiều rộng Card + Spacing (để nhảy trang)
    [SerializeField] private float _scrollSpeed = 10f;        // Tốc độ lướt

    [Header("Pagination")]
    [SerializeField] private Transform _dotsContainer;
    [SerializeField] private GameObject _dotPrefab;
    [SerializeField] private Color _activeDotColor = Color.white;
    [SerializeField] private Color _inactiveDotColor = Color.gray;

    [Header("Difficulty Filter")]
    [SerializeField] private DifficultyButton _screenDifficultyButton;
    [SerializeField] private DifficultySelectionView _difficultySelectionView;
    [SerializeField] private TabController _tabController;
    [SerializeField] private DifficultyPalette _palette;

    [Header("Data")]
    // Thay List trực tiếp bằng Database
    [SerializeField] private MapDatabase _mapDatabase; 

    private PlayerProfile _profile;
    private int _currentIndex = 0;
    private Vector2 _targetPosition;
    private List<Image> _dotImages = new List<Image>();
    private List<MapData> _filteredMaps = new List<MapData>();
    private bool _isInitialized = false;
    private bool _isSnapping = false; // Trạng thái đang tự động "hút" về card

    private void Start()
    {
        if (_scrollRect != null)
        {
            _contentContainer = _scrollRect.content;
        
        // Đảm bảo Content có Pivot X = 0 để dễ tính toán từ trái sang
        _contentContainer.pivot = new Vector2(0f, 0.5f);

        Canvas.ForceUpdateCanvases();

        // Tự động thiết lập Padding để card luôn nằm giữa màn hình
        var layoutGroup = _contentContainer.GetComponent<HorizontalLayoutGroup>();
        if (layoutGroup != null)
        {
            float viewportWidth = _scrollRect.GetComponent<RectTransform>().rect.width;
            int padding = Mathf.RoundToInt((viewportWidth - _visualCardWidth) / 2f);
            layoutGroup.padding.left = padding;
            layoutGroup.padding.right = padding;
        }

        BindScrollRectEvents();
        }

        SetupButtons();
        
        // Nếu có data map, khởi tạo carousel
        if (_mapDatabase != null)
        {
            ApplyFilter(null);
        }
    }

    private void Update()
    {
        if (!_isInitialized || !_isSnapping) return;

        if (_contentContainer != null)
        {
            if (_scrollRect.velocity.magnitude > 0.1f) _scrollRect.velocity = Vector2.zero;

            float step = _scrollSpeed * Time.deltaTime;
            _contentContainer.anchoredPosition = Vector2.Lerp(_contentContainer.anchoredPosition, _targetPosition, step);

            if (Vector2.Distance(_contentContainer.anchoredPosition, _targetPosition) < 0.1f)
            {
                _contentContainer.anchoredPosition = _targetPosition;
                _isSnapping = false;

                // Logic Teleport vô tận
                int realCount = _dotImages.Count;
                if (realCount <= 1) return; // Không teleport nếu chỉ có 1 item

                // Sử dụng <= và >= để xử lý trường hợp vuốt cực nhanh vượt quá 1 index
                if (_currentIndex <= 0)
                {
                    _currentIndex = realCount;
                    TeleportToCurrentIndex();
                }
                else if (_currentIndex >= realCount + 1)
                {
                    _currentIndex = 1;
                    TeleportToCurrentIndex();
                }
            }
        }
    }

    private void TeleportToCurrentIndex()
    {
        if (_contentContainer == null) return;
        
        float targetX = _currentIndex * -_cardStepWidth;
        _targetPosition = new Vector2(targetX, _contentContainer.anchoredPosition.y);
        
        // Dịch chuyển tức thời vị trí vật lý để khớp với Index mới
        _contentContainer.anchoredPosition = _targetPosition;
        UpdateCarouselState();
    }

    private void BindScrollRectEvents()
    {
        // Vì ScrollRect nằm ở object con (Carousel_Area), nó sẽ chặn EventSystem.
        // Chúng ta cần chuyển tiếp sự kiện từ ScrollRect về script này.
        EventTrigger trigger = _scrollRect.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = _scrollRect.gameObject.AddComponent<EventTrigger>();

        // Đăng ký BeginDrag
        EventTrigger.Entry beginEntry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
        beginEntry.callback.AddListener((data) => { OnBeginDrag((PointerEventData)data); });
        trigger.triggers.Add(beginEntry);

        // Đăng ký Drag (Bắt buộc phải có để EndDrag hoạt động)
        EventTrigger.Entry dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        dragEntry.callback.AddListener((data) => { OnDrag((PointerEventData)data); });
        trigger.triggers.Add(dragEntry);

        // Đăng ký EndDrag
        EventTrigger.Entry endEntry = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
        endEntry.callback.AddListener((data) => { OnEndDrag((PointerEventData)data); });
        trigger.triggers.Add(endEntry);
    }

    private void SetupButtons()
    {
        if (_backButton != null)
        {
            _backButton.onClick.RemoveListener(OnBackButtonClicked);
            _backButton.onClick.AddListener(OnBackButtonClicked);
        }

        if (_leftButton != null)
        {
            _leftButton.onClick.AddListener(OnPreviousMap);
        }

        if (_rightButton != null)
        {
            _rightButton.onClick.AddListener(OnNextMap);
        }

        if (_screenDifficultyButton != null)
        {
            _screenDifficultyButton.Button.onClick.AddListener(OpenDifficultyModal);
        }
    }

    private void OpenDifficultyModal()
    {
        if (_tabController == null || _difficultySelectionView == null) return;

        // 1. Hiện Modal (Object chứa ModalController)
        _tabController.gameObject.SetActive(true);
        
        // 2. Cập nhật tiêu đề thủ công (vì không dùng hàm OpenContent nữa)
        if (_tabController.headerText != null) _tabController.headerText.text = "Select Difficulty";

        // 3. Setup logic cho View đang hiển thị
        _difficultySelectionView.Setup((selectedTier) => {
            ApplyFilter(selectedTier);
            _tabController.Close(); // Dùng hàm Close mới refactor
        });
    }

    private void InitializeCarousel(DifficultyPalette.Tier? filter = null)
    {
        // 1. Xóa nội dung cũ (nếu có)
        foreach (Transform child in _contentContainer) Destroy(child.gameObject);
        foreach (Transform child in _dotsContainer) Destroy(child.gameObject);
        _dotImages.Clear();

        // Load dữ liệu người chơi một lần duy nhất khi khởi tạo hoặc lọc Carousel
        _profile = SaveSystem.LoadProfile();

        _filteredMaps.Clear();
        foreach (var map in _mapDatabase.AllMaps)
        {
            if (filter == null || _palette.GetTierFromRating(map.Difficulty) == filter)
            {
                _filteredMaps.Add(map);
            }
        }

        int realCount = _filteredMaps.Count + (_comingSoonPrefab != null ? 1 : 0);
        if (realCount == 0) return;

        bool isInfinite = realCount > 1;

        // 3. [Infinite] Chỉ tạo bản sao nếu có nhiều hơn 1 item
        if (isInfinite) SpawnCarouselItem(realCount - 1); // Buffer đầu

        // 4. Tạo các item thật và Dots
        for (int i = 0; i < realCount; i++)
        {
            SpawnCarouselItem(i);
            
            GameObject dot = Instantiate(_dotPrefab, _dotsContainer);
            Image dotImg = dot.GetComponent<Image>();
            if (dotImg != null) _dotImages.Add(dotImg);
        }

        // 5. [Infinite] Chỉ tạo bản sao nếu có nhiều hơn 1 item
        if (isInfinite) SpawnCarouselItem(0); // Buffer cuối

        Canvas.ForceUpdateCanvases();

        // 6. Nếu vô hạn thì bắt đầu từ 1 (để chừa buffer), nếu không thì bắt đầu từ 0
        _currentIndex = isInfinite ? 1 : 0;

        // TỰ ĐỘNG CHỌN MAP HIỆN TẠI: Nếu có map đang được chọn trong LevelManager, tìm index của nó
        if (LevelManager.SelectedMap != null)
        {
            int mapIndex = _filteredMaps.IndexOf(LevelManager.SelectedMap);
            if (mapIndex != -1)
            {
                // Nếu Infinite, index thực tế = mapIndex + 1 (do có buffer ở đầu)
                _currentIndex = isInfinite ? mapIndex + 1 : mapIndex;
            }
        }

        UpdateCarouselState();
        _isInitialized = true;
        
        _contentContainer.anchoredPosition = _targetPosition;
        _isSnapping = false;
    }

    private void SpawnCarouselItem(int index)
    {
        if (index < _filteredMaps.Count)
        {
            GameObject card = Instantiate(_mapCardPrefab, _contentContainer);
            SetupMapCard(card, _filteredMaps[index]);
        }
        else if (_comingSoonPrefab != null)
        {
            Instantiate(_comingSoonPrefab, _contentContainer);
        }
    }

    // Hàm này giả định Prefab MapCard có các component con cơ bản
    private void SetupMapCard(GameObject card, MapData mapData)
    {
        // Sử dụng MapCardView để quản lý hiển thị
        var view = card.GetComponent<MapCardView>();
        if (view != null)
        {
            // Kiểm tra xem map này đã có trong danh sách kỷ lục chưa
            bool isCompleted = _profile != null && _profile.MapRecords.Exists(r => r.MapName == mapData.Name);
            view.Setup(mapData, isCompleted);
        }

        // Gán sự kiện cho chính thẻ Card (Yêu cầu Prefab MapCard có component Button ở Root)
        var btn = card.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() => OnMapSelected(mapData));
        }
    }

    public void OnNextMap()
    {
        if (!_isInitialized) return;

        int realCount = _dotImages.Count;
        if (realCount <= 1) return; // Khóa nút nếu chỉ có 1 item

        // Nếu đang ở bản sao cuối, dịch chuyển tức thời về item thật trước khi cộng tiếp
        if (_currentIndex >= realCount + 1)
        {
            _currentIndex = 1;
            TeleportToCurrentIndex();
        }
        _currentIndex++;
        UpdateCarouselState();
        _isSnapping = true;
    }

    public void OnPreviousMap()
    {
        if (!_isInitialized) return;

        int realCount = _dotImages.Count;
        if (realCount <= 1) return; // Khóa nút nếu chỉ có 1 item

        // Nếu đang ở bản sao đầu (index 0), dịch chuyển về item thật cuối trước khi trừ tiếp
        if (_currentIndex <= 0)
        {
            _currentIndex = realCount;
            TeleportToCurrentIndex();
        }

        _currentIndex--;
        UpdateCarouselState();
        _isSnapping = true;
    }

    private void ApplyFilter(DifficultyPalette.Tier? tier)
    {
        if (_screenDifficultyButton != null)
        {
            // Xác định Text hiển thị
            string label = tier.HasValue 
                ? (tier.Value == DifficultyPalette.Tier.CrazyPlus ? "Crazy+" : tier.Value.ToString()) 
                : "All";
            
            // Xác định màu sắc từ Palette
            Color color = (tier.HasValue && _palette != null) 
                ? _palette.GetColor(tier.Value) 
                : (_palette != null ? _palette.DefaultColor : Color.gray); 
            
            _screenDifficultyButton.SetVisuals(label, color);
        }

        InitializeCarousel(tier);
    }

    private void UpdateCarouselState()
    {
        float targetX = _currentIndex * -_cardStepWidth; 
        _targetPosition = new Vector2(targetX, _contentContainer.anchoredPosition.y);

        int realCount = _dotImages.Count;
        if (realCount > 0)
        {
            for (int i = 0; i < realCount; i++)
            {
                int activeDotIndex;
                
                if (realCount > 1) // Logic cho Infinite
                {
                    if (_currentIndex == 0) activeDotIndex = realCount - 1;
                    else if (_currentIndex > realCount) activeDotIndex = 0;
                    else activeDotIndex = _currentIndex - 1;
                }
                else // Logic cho Single Item
                {
                    activeDotIndex = 0;
                }

                _dotImages[i].color = (i == activeDotIndex) ? _activeDotColor : _inactiveDotColor;
            }
        }

        // Nút điều hướng ẩn hẳn nếu không có gì để lướt (chỉ có 1 item)
        bool canNavigate = realCount > 1;
        if (_leftButton != null) _leftButton.gameObject.SetActive(canNavigate);
        if (_rightButton != null) _rightButton.gameObject.SetActive(canNavigate);
    }

    private void OnBackButtonClicked()
    {
        // Quay lại màn hình Home
        HomeUIManager.Instance.ShowHomeScreen();
    }

    public void OnMapSelected(MapData mapData)
    {
        Debug.Log($"Map selected: {mapData.Name}");
        
        // Tìm ILevelLoader thông qua interface để tránh phụ thuộc trực tiếp vào class HomeManager
        var loader = FindObjectsByType<Component>().OfType<ILevelLoader>().FirstOrDefault();
        loader?.LoadLevel(mapData);
    }

    // --- Xử lý sự kiện Slide (Vuốt/Kéo) ---

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Khi người dùng bắt đầu chạm, dừng việc tự động hút (nếu đang chạy)
        _isSnapping = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Không cần làm gì ở đây, nhưng phải có hàm này để EventSystem hoạt động
    }

    public void OnEndDrag(PointerEventData eventData)
{
    // Tính toán Index dựa trên vị trí thực tế sau khi vuốt
    float currentX = _contentContainer.anchoredPosition.x;
    float dragDistance = eventData.pressPosition.x - eventData.position.x;
    
    // Ngưỡng vuốt nhạy hơn (ví dụ 10% chiều rộng màn hình)
    float swipeThreshold = Screen.width * 0.1f;

    if (Mathf.Abs(dragDistance) > swipeThreshold)
    {        
        if (dragDistance > 0) OnNextMap();
        else OnPreviousMap();
    }
    else
    {
        // Tính toán index dựa trên vị trí thực tế
        int targetIdx = Mathf.RoundToInt(Mathf.Abs(currentX) / _cardStepWidth);
        int realCount = _dotImages.Count;

        // Nếu chỉ có 1 item, luôn hút về vị trí 0
        if (realCount <= 1)
        {
            _currentIndex = 0;
            UpdateCarouselState();
            _isSnapping = true;
            return;
        }

        // Xử lý tràn biên ngay khi thả tay để tránh bị "khựng"
        if (targetIdx <= 0)
        {
            // Teleport sang bản sao ở cuối để tiếp tục trượt mượt mà
            _currentIndex = 0; 
            UpdateCarouselState();
            _isSnapping = true;
        }
        else if (targetIdx >= realCount + 1)
        {
            _currentIndex = realCount + 1;
            UpdateCarouselState();
            _isSnapping = true;
        }
        else
        {
            _currentIndex = targetIdx;
            UpdateCarouselState();
            _isSnapping = true;
        }
    }
}
}