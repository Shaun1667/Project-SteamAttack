using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 플레이 시간 (엔딩 크레딧에 표시).
    ///
    /// - 타이틀에서 '게임 시작'을 누르면 0부터 다시 잰다. 타이틀 없이 게임 씬에서 바로 켜도 켠 때부터 잰다.
    /// - ESC 메뉴로 멈춘 시간은 빼고, 실제로 흐른 시간(슬로 모션과 관계없이)으로 잰다.
    /// - 엔딩 크레딧이 시작되면 멈춘다.
    /// </summary>
    public static class PlayTimer
    {
        /// <summary>잰 시간(초)</summary>
        public static float Seconds { get; private set; }
        public static bool Running { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Seconds = 0f;
            Running = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRunner()
        {
            var go = new GameObject("NMJ_PlayTimer") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        /// <summary>0부터 다시 재기 시작 (타이틀의 '게임 시작')</summary>
        public static void Restart() { Seconds = 0f; Running = true; }

        /// <summary>재기를 멈춘다 (타이틀 화면, 엔딩 크레딧)</summary>
        public static void Stop() => Running = false;

        /// <summary>"4분 52초" 꼴</summary>
        public static string Format(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return s >= 60 ? $"{s / 60}분 {s % 60:00}초" : $"{s}초";
        }

        private class Runner : MonoBehaviour
        {
            private void Update()
            {
                // 씬 로딩처럼 오래 멈췄던 프레임은 0.25초까지만 센다
                if (Running && !PauseMenu.IsOpen) Seconds += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            }
        }
    }
}
