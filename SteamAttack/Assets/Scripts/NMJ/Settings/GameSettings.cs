using System;
using System.Collections.Generic;
using NGH;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NMJ
{
    /// <summary>
    /// 환경 설정 값 (회의 1.6): 소리(마스터 70·배경음·효과음) / 게임(마우스 감도) / 화면(해상도·전체 화면/창)
    ///
    /// - 값은 PlayerPrefs에 저장되어 다음 실행에도 남는다. 게임을 켜면(어느 씬이든) 저장된 값을 바로 적용한다.
    /// - 소리는 오디오 믹서 없이 나눈다:
    ///   효과음 = AudioListener 전체 볼륨(마스터 × 효과음). 팀원 코드의 AudioSource는 건드리지 않는다.
    ///   배경음 = NmjMusic이 붙은 소스만. 전체 볼륨을 무시하고 (원래 볼륨 × 마스터 × 배경음)으로 직접 맞춘다.
    /// - 마우스 감도는 NGH ThirdPersonCamera의 기본 감도에 곱한다 (조준 배율 SensitivityMultiplier와는 따로).
    /// - 해상도·화면 모드는 설정 화면에서 바꿀 때만 적용한다 (켤 때는 유니티가 마지막 창 상태를 스스로 되살림).
    /// </summary>
    public static class GameSettings
    {
        public const float DefaultMaster = 0.7f;
        public const float DefaultMusic = 1f;
        public const float DefaultSfx = 1f;
        public const float DefaultMouse = 1f;
        public const float MinMouse = 0.2f, MaxMouse = 2f;

        private const string KeyMaster = "NMJ.Settings.Master";
        private const string KeyMusic = "NMJ.Settings.Music";
        private const string KeySfx = "NMJ.Settings.Sfx";
        private const string KeyMouse = "NMJ.Settings.Mouse";

        public static float Master { get; private set; } = DefaultMaster;
        public static float Music { get; private set; } = DefaultMusic;
        public static float Sfx { get; private set; } = DefaultSfx;
        public static float MouseSensitivity { get; private set; } = DefaultMouse;

        /// <summary>값이 바뀔 때 (배경음 소스 등이 다시 맞춘다)</summary>
        public static event Action Changed;

        // 카메라마다 처음 감도(인스펙터 값)를 기억해 두고 거기에 곱한다
        private static readonly Dictionary<ThirdPersonCamera, float> baseSensitivity = new Dictionary<ThirdPersonCamera, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            baseSensitivity.Clear();
            Changed = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Load();
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAudio();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        // ───────── 값 바꾸기 (바로 적용·저장) ─────────

        public static void SetMaster(float v) { Master = Mathf.Clamp01(v); Commit(); }
        public static void SetMusic(float v) { Music = Mathf.Clamp01(v); Commit(); }
        public static void SetSfx(float v) { Sfx = Mathf.Clamp01(v); Commit(); }
        public static void SetMouseSensitivity(float v) { MouseSensitivity = Mathf.Clamp(v, MinMouse, MaxMouse); Commit(); }

        /// <summary>소리·감도를 처음 값으로 (해상도·화면 모드는 그대로)</summary>
        public static void ResetToDefaults()
        {
            Master = DefaultMaster;
            Music = DefaultMusic;
            Sfx = DefaultSfx;
            MouseSensitivity = DefaultMouse;
            Commit();
        }

        private static void Commit()
        {
            Save();
            Apply();
        }

        // ───────── 화면 ─────────

        /// <summary>고를 수 있는 해상도 (가로×세로가 같은 것은 하나로, 작은 것부터)</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            var now = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(now)) list.Add(now);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public static bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        public static void SetDisplay(Vector2Int size, bool fullscreen)
        {
            Screen.SetResolution(size.x, size.y, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        // ───────── 적용 ─────────

        public static void Apply()
        {
            ApplyAudio();
            ApplyMouse();
            Changed?.Invoke();
        }

        private static void ApplyAudio()
        {
            AudioListener.volume = Master * Sfx;
        }

        private static void ApplyMouse()
        {
            foreach (ThirdPersonCamera cam in UnityEngine.Object.FindObjectsByType<ThirdPersonCamera>(FindObjectsInactive.Include))
            {
                if (!baseSensitivity.TryGetValue(cam, out float baseValue))
                {
                    baseValue = cam.mouseSensitivity;
                    baseSensitivity[cam] = baseValue;
                }
                cam.mouseSensitivity = baseValue * MouseSensitivity;
            }
        }

        /// <summary>배경음 소스에 줄 볼륨 (원래 볼륨에 곱함)</summary>
        public static float MusicGain => Master * Music;

        // ───────── 저장 ─────────

        private static void Load()
        {
            Master = PlayerPrefs.GetFloat(KeyMaster, DefaultMaster);
            Music = PlayerPrefs.GetFloat(KeyMusic, DefaultMusic);
            Sfx = PlayerPrefs.GetFloat(KeySfx, DefaultSfx);
            MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeyMouse, DefaultMouse), MinMouse, MaxMouse);
        }

        private static void Save()
        {
            PlayerPrefs.SetFloat(KeyMaster, Master);
            PlayerPrefs.SetFloat(KeyMusic, Music);
            PlayerPrefs.SetFloat(KeySfx, Sfx);
            PlayerPrefs.SetFloat(KeyMouse, MouseSensitivity);
            PlayerPrefs.Save();
        }
    }
}
