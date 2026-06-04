using UnityEngine;

[ExecuteAlways]
public class SpriteMaskImageFitter : MonoBehaviour
{
    [SerializeField] private SpriteMask mask;
    [SerializeField] private SpriteRenderer imageRenderer;

    private Sprite lastSprite;

    private void Update()
    {
        if (imageRenderer == null)
            return;

        if (lastSprite != imageRenderer.sprite)
        {
            lastSprite = imageRenderer.sprite;
            Refresh();
        }
    }

    private void OnEnable()
    {
        Refresh();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Refresh();
    }
#endif

    public void Refresh()
    {
        if (mask == null || imageRenderer == null || imageRenderer.sprite == null)
            return;

        Transform image = imageRenderer.transform;

        Bounds maskBounds = mask.bounds;
        Bounds spriteBounds = imageRenderer.sprite.bounds;

        // Đưa về scale 1 để tính toán kích thước gốc của Sprite trong không gian World
        image.localScale = Vector3.one;

        if (spriteBounds.size.x == 0 || spriteBounds.size.y == 0) return;

        float scaleX = maskBounds.size.x / spriteBounds.size.x;
        float scaleY = maskBounds.size.y / spriteBounds.size.y;

        // Cover: Lấy giá trị lớn nhất để phủ kín Mask
        float scale = Mathf.Max(scaleX, scaleY);

        // Điều chỉnh localScale dựa trên lossyScale của cha để đảm bảo kích thước World chuẩn
        if (image.parent != null)
        {
            Vector3 parentScale = image.parent.lossyScale;
            image.localScale = new Vector3(
                parentScale.x != 0 ? scale / parentScale.x : scale,
                parentScale.y != 0 ? scale / parentScale.y : scale,
                1f);
        }
        else
        {
            image.localScale = Vector3.one * scale;
        }

        image.position = maskBounds.center;
    }
}