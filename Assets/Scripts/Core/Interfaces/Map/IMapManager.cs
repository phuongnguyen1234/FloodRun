using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Interfaces
{
    public interface IMapManager
    {
        MapData GetMapData();
        double GetMapStartTime();
        float GetMaxMapTime();
        Vector3 GetPlayerSpawnPosition();
        Vector3 GetPlayerSpawnCenter();
        void StartMapMechanics();
        
        // Cho UI hiển thị nút bấm
        int GetButtonsActivatedCount();
        int GetTotalButtonsCount();

        // Cho phép GameplayManager lấy nhạc nền của Map
        AudioClip GetMapMusic();

        // Cho phép GameplayManager kiểm tra xem DevTool có được bật không
        bool IsDevToolEnabled();

        // Lấy vị trí của nút hiện tại đang cần kích hoạt
        Transform GetNextButtonTransform();

        void TriggerCurrentButton();

        // Dừng tất cả các sự kiện chạy theo thời gian
        void HaltMapTimelines();

        // Lấy vị trí của các nút còn lại chưa được kích hoạt (cho UI hiển thị)
        List<Transform> GetRemainingButtonTransforms();

        // Lấy vị trí của Exit gần nhất (cho UI hiển thị)
        Transform GetNearestExitTransform(Vector3 playerPosition);

        // Hỗ trợ chạy Coroutine cho các Action cần thời gian (như Fade UI)
        Coroutine StartCoroutine(IEnumerator routine);

        // Hệ thống quản lý đối tượng theo ID để Scale
        void RegisterMapObject(string id, MonoBehaviour obj);
        void UnregisterMapObject(string id, MonoBehaviour obj);
        List<T> GetMapObjectsByID<T>(string id) where T : class;

        float GetKillYThreshold();

        void PrepareMapBackgrounds();

        bool IsMapMechanicsStarted();

    }

    public interface IMapCommandHandler
    {
        void HandleCommand(string command);
    }
}