using System.Collections.Generic;
using NGH;
using UnityEngine;
using UnityEngine.SceneManagement;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 플레이어 증기통 값 맞추기 (임시 다리, NGH 요청 '증기탱크하나로정리'가 처리되면 할 일이 없어짐)
    ///
    /// 지금 플레이어 아래에 YPH.SteamTank가 2개 있다. 칼 적중 회복·화승총은 한쪽, 받아치기·시간 역행·HUD 게이지는 다른 쪽을 본다.
    /// 그래서 칼로 증기를 채워도 게이지가 안 차고, 모은 증기로 시간 역행이 안 되는 일이 생긴다.
    ///
    /// 씬이 열리면 플레이어 아래 탱크를 모두 찾아, 어느 한쪽 값이 바뀌면 나머지도 같은 값으로 맞춘다.
    /// 맞출 때는 RestorePressure(소비 아님)를 써서 다른 통의 배출구가 분출하지 않는다.
    /// 탱크가 1개면 아무것도 하지 않는다.
    /// </summary>
    public class NmjSteamTankSync : MonoBehaviour
    {
        private readonly List<SteamTank> tanks = new List<SteamTank>();
        private readonly Dictionary<SteamTank, System.Action<float, float>> handlers = new Dictionary<SteamTank, System.Action<float, float>>();
        private bool syncing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Attach();

        private static void Attach()
        {
            PlayerController p = FindAnyObjectByType<PlayerController>();
            if (p == null) return;
            GameObject root = p.transform.root.gameObject;
            SteamTank[] found = root.GetComponentsInChildren<SteamTank>(true);
            if (found.Length < 2 || root.GetComponent<NmjSteamTankSync>() != null) return;
            var sync = root.AddComponent<NmjSteamTankSync>();
            sync.Bind(found);
        }

        private void Bind(SteamTank[] found)
        {
            // 처음엔 가장 많이 찬 값으로 맞춘다
            float start = 0f;
            foreach (SteamTank t in found) start = Mathf.Max(start, t.CurrentPressure);
            foreach (SteamTank t in found)
            {
                tanks.Add(t);
                SteamTank captured = t;
                System.Action<float, float> h = (cur, max) => OnChanged(captured, cur);
                handlers[t] = h;
                t.OnPressureChanged += h;
            }
            SyncFrom(null, start);
            Debug.Log($"[NMJ] 증기통 {found.Length}개 값을 맞춥니다 (하나로 합치는 요청: NGH_요청_20261017_증기탱크하나로정리)", this);
        }

        private void OnChanged(SteamTank source, float pressure)
        {
            if (syncing) return;
            SyncFrom(source, pressure);
        }

        private void SyncFrom(SteamTank source, float pressure)
        {
            syncing = true;
            try
            {
                foreach (SteamTank t in tanks)
                    if (t != null && t != source && !Mathf.Approximately(t.CurrentPressure, pressure)) t.RestorePressure(pressure);
            }
            finally { syncing = false; }
        }

        private void OnDestroy()
        {
            foreach (var kv in handlers) if (kv.Key != null) kv.Key.OnPressureChanged -= kv.Value;
        }
    }
}
