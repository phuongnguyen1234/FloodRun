using UnityEngine;
using System.Collections.Generic;
using Core.Interfaces;
using Core;
using DG.Tweening;
using System.Linq;

/// <summary>
/// Component quản lý việc vẽ đường hướng dẫn từ Player đến mục tiêu tiếp theo (Nút bấm hoặc Cửa ra).
/// </summary>
public class GoalLocator : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Material _locatorMaterial;
    [Tooltip("Khoảng cách hở từ Player tới điểm đầu của đường nối")]
    [SerializeField] private float _locatorStartOffset = 1.2f;
    [Tooltip("Offset chiều dọc để nối vào ruột nút thay vì pivot")]
    [SerializeField] private float _locatorTargetVerticalOffset = 0.2f;
    [SerializeField] private string _locatorSortingLayer = "Locator";
    [SerializeField] private Sprite _locatorDotSprite;
    [SerializeField] private float _locatorDotScale = 0.8f;

    private LineRenderer _playerPathLine;
    private LineRenderer _sequencePathLine;
    private SpriteRenderer _playerDot;

    private IGameLoopManager _gameLoopManager;
    private IMapManager _mapManager;
    
    private Vector3 _smoothedTargetPos;
    private Transform _lastTargetTransform;
    private Tweener _targetTweener;

    private void Awake()
    {
        InitLocatorLines();
    }

    private void Start()
    {
        // Tìm kiếm các manager thông qua Interface
        _gameLoopManager = FindObjectsByType<Component>().OfType<IGameLoopManager>().FirstOrDefault(); // FIX Bug 5: This is fine, it's a persistent object
    }

    private void InitLocatorLines()
    {
        _playerPathLine = CreateLine("PlayerPathLine", Color.green, 0.05f, 0);
        _sequencePathLine = CreateLine("SequencePathLine", Color.yellow, 0.05f, -1);
        
        GameObject dotGo = new GameObject("PlayerPathDot");
        dotGo.transform.SetParent(transform);
        _playerDot = dotGo.AddComponent<SpriteRenderer>();
        _playerDot.sprite = _locatorDotSprite;
        _playerDot.sortingLayerName = _locatorSortingLayer;
        _playerDot.sortingOrder = 1;
        _playerDot.transform.localScale = Vector3.one * _locatorDotScale;
        _playerDot.enabled = false;
    }

    private LineRenderer CreateLine(string name, Color color, float width, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.material = _locatorMaterial != null ? _locatorMaterial : new Material(Shader.Find("Hidden/Internal-Colored"));
        line.startColor = line.endColor = color;
        line.startWidth = line.endWidth = width;
        line.sortingLayerName = _locatorSortingLayer;
        line.sortingOrder = sortingOrder;
        line.positionCount = 0;
        return line;
    }

    private void LateUpdate()
    {
        HandleGoalLocator();
    }

    private void HandleGoalLocator()
    {
        bool isLocatorEnabled = SettingsManager.Instance != null && SettingsManager.Instance.GoalLocator;

        // Lấy tham chiếu MapManager hiện tại thông qua GameLoopManager (đã được xử lý Unity-safe null check)
        if (_gameLoopManager != null)
        {
            _mapManager = _gameLoopManager.CurrentMapManager; // Get the current map manager from the game loop manager
        }
        
        // Kiểm tra điều kiện hiển thị
        if (!isLocatorEnabled || 
            _gameLoopManager == null || 
            _mapManager == null ||
            !_mapManager.IsMapMechanicsStarted() || // FIX: Chỉ hiện khi map đã thực sự bắt đầu
            _gameLoopManager.LocalPlayer == null || 
            !_gameLoopManager.IsGameActive || 
            _gameLoopManager.LocalPlayer.IsDead || 
            _gameLoopManager.LocalPlayer.Status.Value != PlayerStatus.InGame) // FIX: Chỉ hiện khi ĐANG chơi (không hiện ở Lobby/Finished)
        {
            DisableLocator();
            return;
        }

        List<Transform> buttons = _mapManager.GetRemainingButtonTransforms();
        Vector3 playerPos = ((Component)_gameLoopManager.LocalPlayer).transform.position;
        playerPos.z = 0;
        
        Transform targetTransform = null;

        if (buttons != null && buttons.Count > 0)
        {
            bool isExplosive = false;
            if (buttons[0].TryGetComponent<IButtonController>(out var btn))
            {
                isExplosive = btn.IsExplosive;
            }

            _playerPathLine.startColor = _playerPathLine.endColor = isExplosive ? Color.red : Color.green;
            targetTransform = buttons[0];
            UpdateSequenceLine(buttons);
        }
        else
        {
            _playerPathLine.startColor = _playerPathLine.endColor = Color.white;
            targetTransform = _mapManager.GetNearestExitTransform(playerPos);
            _sequencePathLine.positionCount = 0;
        }

        if (targetTransform != null)
        {
            Vector3 realTargetPos = targetTransform.position + Vector3.up * _locatorTargetVerticalOffset;
            realTargetPos.z = 0;

            if (targetTransform != _lastTargetTransform)
            {
                bool wasFirstTarget = _lastTargetTransform == null;
                _lastTargetTransform = targetTransform;
                _targetTweener?.Kill();
                
                if (wasFirstTarget) _smoothedTargetPos = realTargetPos;

                _targetTweener = DOTween.To(() => _smoothedTargetPos, x => _smoothedTargetPos = x, realTargetPos, 0.3f)
                    .SetEase(Ease.OutQuad);
            }
            else if (_targetTweener == null || !_targetTweener.IsActive() || !_targetTweener.IsPlaying())
            {
                _smoothedTargetPos = realTargetPos;
            }
            
            float dist = Vector3.Distance(playerPos, realTargetPos);
            if (dist > _locatorStartOffset + 0.2f)
            {
                Vector3 direction = (_smoothedTargetPos - playerPos).normalized;
                Vector3 startPos = playerPos + (direction * _locatorStartOffset);
                
                _playerPathLine.positionCount = 2;
                _playerPathLine.SetPosition(0, startPos);
                _playerPathLine.SetPosition(1, _smoothedTargetPos);

                _playerDot.enabled = true;
                _playerDot.transform.position = startPos;
                _playerDot.color = _playerPathLine.startColor;
            }
            else
            {
                _playerPathLine.positionCount = 0;
                _playerDot.enabled = false;
            }
        }
        else DisableLocator();
    }

    private void UpdateSequenceLine(List<Transform> buttons)
    {
        if (buttons.Count >= 3)
        {
            _sequencePathLine.positionCount = 2;
            Vector3 b2Pos = buttons[1].position + Vector3.up * _locatorTargetVerticalOffset;
            Vector3 b3Pos = buttons[2].position + Vector3.up * _locatorTargetVerticalOffset;
            b2Pos.z = 0; b3Pos.z = 0;
            _sequencePathLine.SetPosition(0, b2Pos);
            _sequencePathLine.SetPosition(1, b3Pos);
        }
        else _sequencePathLine.positionCount = 0;
    }

    private void DisableLocator()
    {
        if (_playerPathLine != null) _playerPathLine.positionCount = 0;
        if (_sequencePathLine != null) _sequencePathLine.positionCount = 0;
        if (_playerDot != null) _playerDot.enabled = false;
        _lastTargetTransform = null;
        _targetTweener?.Kill();
    }
}