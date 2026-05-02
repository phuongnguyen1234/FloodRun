using UnityEngine;
using TMPro;
using System.Collections;
using Core.Interfaces;

/// <summary>
/// Action để điều khiển các thành phần UI như hiển thị, ẩn, thay đổi text.
/// </summary>
[System.Serializable]
public class MapAction_UIControl : MapAction
{
    public enum UICommand
    {
        ToggleCanvasGroup,
        SetText,
        PlayAnimation
    }

    [Tooltip("Hành động UI muốn thực hiện.")]
    public UICommand Command;

    [Header("Canvas Group Settings")]
    [Tooltip("Kéo CanvasGroup của panel UI vào đây.")]
    public CanvasGroup TargetCanvasGroup;
    [Tooltip("Bật (hiện) hay tắt (ẩn) panel.")]
    public bool SetVisible = true;
    [Tooltip("Thời gian để fade in/out. Đặt là 0 để thay đổi tức thì.")]
    public float FadeDuration = 0.5f;

    [Header("Text Settings")]
    [Tooltip("Kéo component TextMeshProUGUI vào đây.")]
    public TextMeshProUGUI TargetText;
    [Tooltip("Nội dung text mới.")]
    [TextArea(3, 10)]
    public string NewText;

    [Header("Animation Settings")]
    [Tooltip("Kéo Animator của UI element vào đây.")]
    public Animator TargetAnimator;
    [Tooltip("Tên của trigger trong Animator.")]
    public string AnimationTriggerName;

    public override void Execute(IMapManager manager)
    {
        switch (Command)
        {
            case UICommand.ToggleCanvasGroup:
                if (TargetCanvasGroup != null && manager != null)
                {
                    manager.StartCoroutine(FadeCanvasGroup(TargetCanvasGroup, SetVisible, FadeDuration));
                }
                break;

            case UICommand.SetText:
                if (TargetText != null) TargetText.text = NewText;
                break;

            case UICommand.PlayAnimation:
                if (TargetAnimator != null && !string.IsNullOrEmpty(AnimationTriggerName)) TargetAnimator.SetTrigger(AnimationTriggerName);
                break;
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, bool visible, float duration)
    {
        float startAlpha = cg.alpha;
        float targetAlpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;

        if (duration <= 0) { cg.alpha = targetAlpha; yield break; }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }
        cg.alpha = targetAlpha;
    }
}