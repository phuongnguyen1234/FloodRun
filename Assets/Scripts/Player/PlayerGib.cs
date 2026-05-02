using UnityEngine;

/// <summary>
/// Đây là script cho các mảnh vụn (gib) của nhân vật khi bị giết. Nó sẽ tự hủy sau một khoảng thời gian nhất định và có hiệu ứng mờ dần trước khi biến mất hoàn toàn.
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
public class PlayerGib : MonoBehaviour
{
    [SerializeField] private float _lifeTime = 3f;
    [SerializeField] private float _fadeOutDuration = 0.5f;
    private SpriteRenderer _sr;
    private float _timer;

    void Awake() => _sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        _timer += Time.deltaTime;

        // Tự hủy khi hết tổng thời gian tồn tại
        if (_timer >= _lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        // Chỉ bắt đầu mờ dần (fade out) khi thời gian còn lại ít hơn _fadeOutDuration
        float fadeStartTime = _lifeTime - _fadeOutDuration;
        if (_timer > fadeStartTime && _fadeOutDuration > 0)
        {
            Color c = _sr.color;
            // Sử dụng InverseLerp để tính toán alpha dựa trên tiến trình của giai đoạn fade
            float alpha = Mathf.InverseLerp(_lifeTime, fadeStartTime, _timer);
            _sr.color = new Color(c.r, c.g, c.b, alpha);
        }
    }
}