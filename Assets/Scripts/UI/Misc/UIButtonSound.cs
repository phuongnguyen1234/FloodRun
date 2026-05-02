using UnityEngine;
using UnityEngine.UI;
using Core.Interfaces;
using System.Linq;

/// <summary>
/// Thành phần giúp Button Prefab tự động tìm Manager để phát âm thanh khi click.
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonSound : MonoBehaviour
{
    private IUISfxPlayer _sfxPlayer;

    private void Start()
    {
        // Tìm bất kỳ Component nào thực thi IUISfxPlayer (có thể là HomeUIManager hoặc GameplayUIManager)
        if (_sfxPlayer == null)
        {
            _sfxPlayer = FindObjectsByType<Component>(FindObjectsSortMode.None).OfType<IUISfxPlayer>().FirstOrDefault();
        }

        // Đăng ký sự kiện Click
        GetComponent<Button>().onClick.AddListener(() => _sfxPlayer?.PlayClickSound());
    }
}