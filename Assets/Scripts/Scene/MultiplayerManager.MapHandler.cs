using UnityEngine;
using Unity.Netcode;
using Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using Core;
using Unity.Collections;

namespace Multiplayer
{
    public partial class MultiplayerManager
    {
        [Header("Settings & Databases")]
        [SerializeField] private MapDatabase _mapDatabase;
        [SerializeField] private DifficultyPalette _palette;
        private IMapManager _currentMapManager;
        private GameObject _currentMapInstance;
        
        /// <summary>
        /// Trả về IMapManager hiện tại một cách an toàn.
        /// Nếu map đã bị hủy (Destroyed), thuộc tính này sẽ trả về null thay vì gây MissingReferenceException.
        /// </summary>
        public IMapManager CurrentMapManager => (_currentMapManager != null && (_currentMapManager as MonoBehaviour) != null) ? _currentMapManager : null;

        private readonly HashSet<GameObject> _registeredMapPrefabs = new();

        public NetworkVariable<FixedString64Bytes> NetCurrentMapName = new(new FixedString64Bytes(""));

        private void RegisterAllMapNetworkPrefabs()
        {
            if (_mapDatabase?.AllMaps == null) return;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            foreach (MapData map in _mapDatabase.AllMaps)
            {
                if (map?.MapPrefab == null) continue;
                if (!map.MapPrefab.TryGetComponent<NetworkObject>(out _)) continue;

                if (_registeredMapPrefabs.Contains(map.MapPrefab)) continue;

                try
                {
                    nm.AddNetworkPrefab(map.MapPrefab);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[MultiplayerManager] Map prefab '{map.Name}' already registered or failed: {ex.Message}");
                }

                _registeredMapPrefabs.Add(map.MapPrefab);
            }
        }

        /// <summary>
        /// Hủy map đang spawn trên Server và xóa tham chiếu ở mọi peer.
        /// </summary>
        private void CleanupCurrentMap()
        {
            if (IsServer && _currentMapInstance != null)
            {
                if (_currentMapInstance.TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
                    netObj.Despawn(true);
                else
                    Destroy(_currentMapInstance);
            }

            _currentMapInstance = null;
            _currentMapManager = null;
        }

        private void ClearLocalMapReference()
        {
            _currentMapManager = null;
        }

        private bool TryResolveMapManager(string mapName, ulong mapNetworkObjectId, out IMapManager manager)
        {
            manager = null;
            if (string.IsNullOrEmpty(mapName)) return false;

            MapData expectedData = _mapDatabase?.AllMaps.FirstOrDefault(m => m.Name == mapName);
            var spawnManager = NetworkManager.Singleton?.SpawnManager;

            if (spawnManager != null)
            {
                if (mapNetworkObjectId != 0
                    && spawnManager.SpawnedObjects.TryGetValue(mapNetworkObjectId, out NetworkObject netObj))
                {
                    manager = FindMapManagerOn(netObj.gameObject);
                    if (manager != null) return true;
                }

                foreach (NetworkObject spawned in spawnManager.SpawnedObjects.Values)
                {
                    IMapManager candidate = FindMapManagerOn(spawned.gameObject);
                    if (candidate != null && MapManagerMatchesName(candidate, mapName, expectedData))
                    {
                        manager = candidate;
                        return true;
                    }
                }
            }

            foreach (IMapManager candidate in Resources.FindObjectsOfTypeAll<MonoBehaviour>().OfType<IMapManager>())
            {
                if (candidate is not MonoBehaviour mb) continue;
                if (mb.gameObject.scene.name == "DontDestroyOnLoad") continue;
                if (!MapManagerMatchesName(candidate, mapName, expectedData)) continue;

                manager = candidate;
                return true;
            }

            return false;
        }

        private static bool MapManagerMatchesName(IMapManager manager, string mapName, MapData expectedData)
        {
            MapData data = manager?.GetMapData();
            if (data == null) return false;

            return data.Name == mapName
                || (expectedData != null && data.Name == expectedData.Name)
                || (expectedData != null && ReferenceEquals(data, expectedData));
        }

        /// <summary>
        /// Tìm IMapManager của round hiện tại — khớp tên map, tránh lấy nhầm map round trước còn đang destroy.
        /// </summary>
        private static IMapManager FindMapManagerOn(GameObject root)
        {
            if (root == null) return null;
            return root.GetComponentsInChildren<MonoBehaviour>(true).OfType<IMapManager>().FirstOrDefault();
        }

        private static PlayerSpawn FindPlayerSpawnOnMap(IMapManager map)
        {
            if (map is not MonoBehaviour mapRoot) return null;
            return mapRoot.GetComponentsInChildren<PlayerSpawn>(true).FirstOrDefault(s => s.IsMapSpawn);
        }

        private void TeleportLocalPlayerToMapSpawn(IMapManager map)
        {
            // FIX: Không check IsAFK ở đây vì Server đã xác nhận Status là InGame cho những người tham gia.
            // Check Status đảm bảo Client tuân thủ đúng mệnh lệnh từ Server.
            if (LocalPlayer == null || map == null) return;
            if (!_localIsRoundParticipant && LocalPlayer.Status.Value != PlayerStatus.InGame) return;

            PlayerSpawn mapSpawn = FindPlayerSpawnOnMap(map);
            Vector3 spawnPos = mapSpawn != null ? mapSpawn.GetRandomSpawnPosition() : map.GetPlayerSpawnPosition();
            LocalPlayer.Teleport(spawnPos);
            if (mapSpawn != null) LocalPlayer.SetFacing(mapSpawn.IsFacingRight);

            if (_vcam == null) _vcam = FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
            if (_vcam != null && LocalPlayer is MonoBehaviour playerMono)
            {
                // Ngắt Follow để tránh Cinemachine tự động nội suy (Damping) từ Lobby sang Map
                _vcam.Follow = null;
                CameraHelper.WarpToTarget(_vcam, playerMono);
            }
        }

        /// <summary>
        /// Cập nhật LobbyInfoBoard trên Client với thông tin map đã chọn và độ khó. Hàm này được gọi từ Server sau khi map đã được chọn và round bắt đầu.
        /// </summary>
        /// <param name="mapName"></param>
        /// <param name="difficulty"></param>
        [Rpc(SendTo.ClientsAndHost)]
        private void UpdateLobbyWorldUIClientRpc(FixedString64Bytes mapName, float difficulty)
        {
            MapData data = _mapDatabase.AllMaps.FirstOrDefault(m => m.Name == mapName.ToString());
            _uiManager?.UpdateLobbyWorldMapInfo(data, difficulty);
        }
    }
}