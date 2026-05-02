using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SimpleMover: Di chuyển một object qua một chuỗi điểm (waypoints) với các tùy chọn lặp lại khác nhau.
/// </summary>
public class SimpleMover : MonoBehaviour
{
    public enum LoopType
    {
        PingPong,   // A -> B -> C -> B -> A (Đi đi về về)
        Loop,       // A -> B -> C -> A (Đi vòng tròn)
        Once        // A -> B -> C -> Dừng
    }

    [Header("Settings")]
    [Tooltip("Tốc độ di chuyển.")]
    [SerializeField] private float _moveSpeed = 3f;
    [Tooltip("Thời gian chờ khi đến mỗi điểm.")]
    [SerializeField] private float _waitTime = 1f;
    [Tooltip("Thời gian chờ ban đầu trước khi bắt đầu di chuyển.")]
    [SerializeField] private float _startDelay = 0f;
    [SerializeField] private LoopType _loopType = LoopType.PingPong;
    [SerializeField] private bool _autoStart = true;
    [Tooltip("Nếu True: Waypoint là khoảng cách cộng thêm (Offset). Nếu False: Waypoint là tọa độ thế giới (Absolute).")]
    [SerializeField] private bool _useRelativeCoordinates = true;
    
    [Header("Path Settings")]
    [Tooltip("Danh sách các điểm đến (Offset tương đối so với vị trí bắt đầu). Ví dụ: (0, 5, 0) sẽ làm object đi lên 5 đơn vị.")]
    [SerializeField] private List<Vector3> _waypoints = new List<Vector3>();

    private Vector3 _startPosition;
    private List<Vector3> _globalWaypoints = new List<Vector3>();
    private int _currentIndex = 0;
    private int _direction = 1; // 1: Xuôi, -1: Ngược
    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        // Nếu là platform chở người chơi, nên để Kinematic để vật lý ổn định
        if (_rb != null) _rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void Start()
    {
        _startPosition = transform.position;

        // Xây dựng danh sách điểm trong World Space
        // Điểm đầu tiên luôn là vị trí xuất phát
        _globalWaypoints.Add(_startPosition);

        // Cộng offset vào vị trí xuất phát để ra các điểm tiếp theo
        foreach (var point in _waypoints)
        {
            if (_useRelativeCoordinates)
            {
                _globalWaypoints.Add(_startPosition + point);
            }
            else
            {
                _globalWaypoints.Add(point);
            }
        }

        if (_autoStart)
        {
            StartCoroutine(MoveRoutine());
        }
    }

    public void StartMoving()
    {
        StopAllCoroutines();
        StartCoroutine(MoveRoutine());
    }

    public void StopMoving()
    {
        StopAllCoroutines();
    }

    private IEnumerator MoveRoutine()
    {
        if (_startDelay > 0)
            yield return new WaitForSeconds(_startDelay);

        while (true)
        {
            // 1. Xác định điểm đến tiếp theo
            int nextIndex = GetNextIndex();
            
            // Nếu LoopType là Once và đã đi hết đường -> Dừng
            if (nextIndex == -1) yield break;

            Vector3 targetPos = _globalWaypoints[nextIndex];
            
            // 2. Di chuyển đến đích
            while (Vector3.Distance(transform.position, targetPos) > 0.01f)
            {
                // Dùng MovePosition nếu có Rigidbody (tốt cho Platform chở Player)
                // Dùng Transform nếu là vật trang trí
                Vector3 newPos = Vector3.MoveTowards(transform.position, targetPos, _moveSpeed * Time.deltaTime);
                
                if (_rb != null) _rb.MovePosition(newPos);
                else transform.position = newPos;

                yield return null; // Chờ frame tiếp theo
            }

            // Snap vị trí cuối cùng cho chính xác
            if (_rb != null) _rb.MovePosition(targetPos);
            else transform.position = targetPos;

            _currentIndex = nextIndex;

            // 3. Chờ tại điểm đến
            if (_waitTime > 0)
                yield return new WaitForSeconds(_waitTime);
        }
    }

    private int GetNextIndex()
    {
        int next = _currentIndex + _direction;

        if (_loopType == LoopType.PingPong)
        {
            // Nếu chạm cuối hoặc chạm đầu -> Đổi hướng
            if (next >= _globalWaypoints.Count || next < 0)
            {
                _direction *= -1;
                next = _currentIndex + _direction;
            }
        }
        else if (_loopType == LoopType.Loop)
        {
            // Nếu vượt quá -> Quay về 0
            if (next >= _globalWaypoints.Count)
            {
                next = 0;
            }
        }
        else if (_loopType == LoopType.Once)
        {
            // Nếu vượt quá -> Trả về -1 để báo dừng
            if (next >= _globalWaypoints.Count)
            {
                return -1;
            }
        }

        return next;
    }

    // Vẽ đường đi trong Editor để dễ hình dung
    private void OnDrawGizmosSelected()
    {
        if (_waypoints == null || _waypoints.Count == 0) return;

        Gizmos.color = Color.yellow;
        Vector3 basePos = Application.isPlaying ? _startPosition : transform.position;
        Vector3 prevPos = basePos;

        // Vẽ đường từ gốc đến điểm 1, điểm 2...
        foreach (var point in _waypoints)
        {
            Vector3 target;
            if (_useRelativeCoordinates)
            {
                // Nếu là Relative, phải cộng vào vị trí gốc (basePos)
                target = basePos + point;
            }
            else
            {
                // Nếu là Absolute, dùng luôn tọa độ đó
                target = point;
            }
            
            Gizmos.DrawLine(prevPos, target);
            Gizmos.DrawWireSphere(target, 0.2f);
            prevPos = target;
        }

        // Nếu là Loop, vẽ đường nối điểm cuối về điểm đầu
        if (_loopType == LoopType.Loop && _waypoints.Count > 0)
        {
            Gizmos.DrawLine(prevPos, basePos);
        }
    }
}
