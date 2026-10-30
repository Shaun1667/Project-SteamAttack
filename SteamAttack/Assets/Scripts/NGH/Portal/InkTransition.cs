using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGH
{
    /// <summary>
    /// 먹 번짐 화면 전환: 씬을 옮길 때 화면 가장자리부터 먹물이 번져 덮고, 새 씬이 열리면 먹이 걷힘.
    ///  - CoverThen(할 일): 화면을 먹으로 다 덮은 뒤 할 일(씬 불러오기)을 부름. 새 씬이 불러와지면 저절로 걷힘
    ///  - 씬을 옮겨도 사라지지 않는 오브젝트 하나를 처음 쓸 때 만든다 (씬에 미리 둘 필요 없음)
    ///  - 화면 효과는 NGH_ScreenFX 셰이더가 전역 값 _NGH_InkWipe 를 읽어 그림
    /// </summary>
    public class InkTransition : MonoBehaviour
    {
        [Tooltip("먹이 화면을 덮는 시간 (실제 초)")]
        public float coverTime = 0.5f;
        [Tooltip("새 씬에서 먹이 다 덮인 채로 기다리는 시간 (실제 초) — 첫 프레임 끊김 가리기")]
        public float holdTime = 0.15f;
        [Tooltip("먹이 걷히는 시간 (실제 초)")]
        public float revealTime = 0.8f;

        static readonly int InkId = Shader.PropertyToID("_NGH_InkWipe");
        static InkTransition _instance;

        bool _covered, _busy;

        public static bool IsBusy => _instance != null && _instance._busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            Shader.SetGlobalFloat(InkId, 0f);
        }

        static InkTransition Instance
        {
            get
            {
                if (_instance) return _instance;
                var go = new GameObject("NGH_InkTransition");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<InkTransition>();
                return _instance;
            }
        }

        /// <summary>화면을 먹으로 덮은 뒤 onCovered 를 부른다 (보통 SceneManager.LoadScene). 새 씬이 불러와지면 먹이 걷힌다</summary>
        public static void CoverThen(Action onCovered)
        {
            InkTransition t = Instance;
            if (t._busy) return;
            t.StartCoroutine(t.Cover(onCovered));
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        IEnumerator Cover(Action onCovered)
        {
            _busy = true;
            for (float t = 0f; t < coverTime; t += Time.unscaledDeltaTime)
            {
                Shader.SetGlobalFloat(InkId, Mathf.SmoothStep(0f, 1f, t / coverTime));
                yield return null;
            }
            Shader.SetGlobalFloat(InkId, 1f);
            _covered = true;
            onCovered?.Invoke();
            // 씬을 안 옮기는 일이었으면(불러오기 실패 등) 잠시 뒤 스스로 걷음
            yield return new WaitForSecondsRealtime(1.5f);
            if (_covered) yield return Reveal();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_covered) return;
            StopAllCoroutines();
            StartCoroutine(Reveal());
        }

        IEnumerator Reveal()
        {
            _covered = false;
            Shader.SetGlobalFloat(InkId, 1f);
            yield return new WaitForSecondsRealtime(holdTime);
            for (float t = 0f; t < revealTime; t += Time.unscaledDeltaTime)
            {
                Shader.SetGlobalFloat(InkId, 1f - Mathf.SmoothStep(0f, 1f, t / revealTime));
                yield return null;
            }
            Shader.SetGlobalFloat(InkId, 0f);
            _busy = false;
        }
    }
}
