using UnityEngine;
using Core.Interfaces;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// Siêu vệ tinh tổng quát: Ánh xạ một chuỗi lệnh (String Command) tới một UnityEvent.
/// Giúp Designer có thể điều khiển mọi thuộc tính của mọi Component mà không cần viết code.
/// </summary>
public class MapEventSatellite : MonoBehaviour, IMapCommandHandler
{
    [System.Serializable]
    public struct CommandMapping
    {
        [Tooltip("Tên lệnh nhận từ Action (ví dụ: 'Open', 'RedLight', 'StartParticles')")]
        public string Command;
        [Tooltip("Hành động sẽ thực thi khi nhận lệnh trên.")]
        public UnityEvent Response;
    }

    [Header("Event Mappings")]
    [SerializeField] private List<CommandMapping> _mappings = new List<CommandMapping>();

    public void HandleCommand(string command)
    {
        foreach (var mapping in _mappings)
        {
            if (mapping.Command == command)
            {
                mapping.Response?.Invoke();
                // Không break để cho phép nhiều phản hồi cho cùng một lệnh nếu cần
            }
        }
    }
}