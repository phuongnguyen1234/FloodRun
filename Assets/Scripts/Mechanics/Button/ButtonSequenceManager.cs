using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;

/// <summary>
/// ButtonSequenceManager là một MonoBehaviour quản lý một chuỗi các ButtonController.
/// Nó đảm bảo rằng các nút được kích hoạt theo đúng thứ tự đã định sẵn
/// </summary>
public class ButtonSequenceManager : MonoBehaviour, IButtonSequenceManager
{
    [Header("Sequence Configuration")]
    [Tooltip("Kéo thả các Button vào đây theo đúng thứ tự bạn muốn kích hoạt (0 -> 1 -> 2...)")]
    [SerializeField] private List<ButtonController> _buttons;

    [Header("Sequence Completed")]
    [Tooltip("Sự kiện này sẽ được gọi khi nút CUỐI CÙNG trong danh sách được kích hoạt")]
    [SerializeField] private UnityEvent _onSequenceComplete;

    [Header("Audio Settings")]
    [SerializeField] private float _basePitch = 1.0f;
    [SerializeField] private float _pitchStep = 0.1f;
    [SerializeField] private float _maxPitch = 3.0f;

    // Thực thi interface IButtonSequenceManager
    public UnityEvent OnSequenceComplete => _onSequenceComplete;
    
    // Public getters cho UI
    public int CurrentIndex => _currentIndex;
    public int TotalButtons => _buttons != null ? _buttons.Count : 0;

    public Transform GetCurrentButtonTransform()
    {
        if (_currentIndex >= 0 && _currentIndex < _buttons.Count) return _buttons[_currentIndex].transform;
        return null;
    }

    public List<Transform> GetRemainingButtonTransforms()
    {
        if (_buttons == null) return new List<Transform>();
        return _buttons.Skip(_currentIndex).Where(b => b != null).Select(b => b.transform).ToList();
    }

    public void TriggerCurrentButton()
    {
        // CẢI TIẾN: Gọi hàm Activate() thay vì Interact() để tránh tạo vòng lặp vô hạn
        if (_currentIndex >= 0 && _currentIndex < _buttons.Count)
            _buttons[_currentIndex].Activate();
    }

    private int _currentIndex = 0;

    private void Start()
    {
        InitializeSequence();
    }

    private void InitializeSequence()
    {
        // Reset index về 0
        _currentIndex = 0;

        // Duyệt qua tất cả các nút để setup trạng thái ban đầu
        for (int i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i] == null) continue;
            
            // Đảm bảo xóa listener cũ nếu có (tránh lỗi khi reload scene hoặc reset)
            _buttons[i].OnButtonActivated.RemoveListener(OnButtonTriggered);

            if (i == 0)
            {
                // Nút đầu tiên: Cho phép ấn (Normal) và lắng nghe sự kiện
                ActivateCurrentButtonStep(i);
            }
            else
            {
                // Các nút sau: Tắt đi (Inactive)
                // Đảm bảo gọi SetState để cập nhật visual (ẩn Normal marker, hiện Inactive marker)
                _buttons[i].SetState(ButtonController.ButtonState.Inactive);
            }
        }
    }

    private void ActivateCurrentButtonStep(int index)
    {
        if (index >= _buttons.Count) return;

        ButtonController btn = _buttons[index];
        
        // 1. Chuyển nút hiện tại sang Normal (sáng đèn, sẵn sàng tương tác)
        btn.SetState(ButtonController.ButtonState.Normal);

        // Cài đặt Pitch cho âm thanh bấm nút tăng dần
        float targetPitch = Mathf.Min(_basePitch + (index * _pitchStep), _maxPitch);
        btn.SetActivationPitch(targetPitch);

        // 2. Lắng nghe sự kiện khi nút này được Player kích hoạt
        // Remove trước để chắc chắn không add 2 lần
        btn.OnButtonActivated.RemoveListener(OnButtonTriggered);
        btn.OnButtonActivated.AddListener(OnButtonTriggered);
    }

    private void OnButtonTriggered()
    {
        // Dọn dẹp listener của nút vừa ấn xong để tránh lỗi logic
        if (_currentIndex < _buttons.Count)
        {
            _buttons[_currentIndex].OnButtonActivated.RemoveListener(OnButtonTriggered);
        }

        // Tăng index để xử lý nút tiếp theo
        _currentIndex++;

        // Nếu còn nút trong danh sách thì bật nút đó lên, ngược lại thì báo hoàn thành
        if (_currentIndex < _buttons.Count)
        {
            ActivateCurrentButtonStep(_currentIndex);
        }
        else
        {
            _onSequenceComplete?.Invoke();
        }
    }
}
