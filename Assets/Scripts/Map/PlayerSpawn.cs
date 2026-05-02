using UnityEngine;

/// <summary>
/// Lớp PlayerSpawn chịu trách nhiệm quản lý vị trí spawn của người chơi trên bản đồ.
/// </summary>
public class PlayerSpawn : MonoBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("Độ dài đoạn thẳng spawn tính từ tâm object sang hai bên (trục X).")]
    [SerializeField] private float _spawnWidth = 2f;
    
    [Header("Visuals")]
    [SerializeField] private Color _gizmoColor = Color.green;

    /// <summary>
    /// Trả về một vị trí ngẫu nhiên nằm trên đoạn thẳng spawn.
    /// </summary>
    public Vector3 GetRandomSpawnPosition()
    {
        float randomX = Random.Range(-_spawnWidth, _spawnWidth);
        return transform.position + new Vector3(randomX, 0f, 0f);
    }

    // Vẽ Gizmos trong Editor để dễ dàng nhìn thấy vùng spawn
    private void OnDrawGizmos()
    {
        Gizmos.color = _gizmoColor;
        Vector3 center = transform.position;
        Vector3 left = center - new Vector3(_spawnWidth, 0f, 0f);
        Vector3 right = center + new Vector3(_spawnWidth, 0f, 0f);

        Gizmos.DrawLine(left, right);
        Gizmos.DrawSphere(left, 0.1f);
        Gizmos.DrawSphere(right, 0.1f);
        Gizmos.DrawWireSphere(center, 0.2f);
    }
}
