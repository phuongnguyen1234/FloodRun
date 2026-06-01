using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;

[System.Serializable]
public class MapAction_SpawnObject : MapAction
{
    public GameObject PrefabToSpawn;
    public Vector3 LocalPosition;

    public override void Execute(IMapManager manager, float elapsedTime = 0f)
    {
        // Ép kiểu sang các class cần thiết để truy cập thuộc tính
        MonoBehaviour mb = manager as MonoBehaviour;
        NetworkBehaviour nb = manager as NetworkBehaviour;

        if (mb == null) return;

        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        
        // Nếu có mạng, chỉ Server mới được Spawn
        if (isNetworkActive && (nb == null || !nb.IsServer))
        {
            return; 
        }

        if (PrefabToSpawn != null)
        {
            GameObject go = Object.Instantiate(PrefabToSpawn, mb.transform);
            go.transform.localPosition = LocalPosition;

            // Chỉ gọi Spawn nếu mạng đang chạy
            if (isNetworkActive && go.TryGetComponent<NetworkObject>(out var netObj))
            {
                netObj.Spawn();
            }
        }
    }
}