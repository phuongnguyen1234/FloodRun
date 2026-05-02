using UnityEngine;

/// <summary>
/// Công cụ hỗ trợ MapEventSatellite truy cập các thuộc tính khó như Alpha hoặc BodyType.
/// Sẽ được update liên tục để hỗ trợ nhiều thuộc tính hơn theo yêu cầu của Designer.
/// </summary>
public class MapPropertyHelper : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _sprite;
    [SerializeField] private Collider2D _collider;
    [SerializeField] private Rigidbody2D _rb;

    private void Awake()
    {
        if (_sprite == null) _sprite = GetComponent<SpriteRenderer>();
        if (_collider == null) _collider = GetComponent<Collider2D>();
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
    }

    public void SetAlpha(float alpha)
    {
        if (_sprite == null) return;
        Color c = _sprite.color;
        _sprite.color = new Color(c.r, c.g, c.b, alpha);
    }
    
    public void SetBodyTypeDynamic()
    {
        if (_rb != null) _rb.bodyType = RigidbodyType2D.Dynamic;
    }

    public void SetBodyTypeKinematic()
    {
        if (_rb != null) _rb.bodyType = RigidbodyType2D.Kinematic;
    }
}
