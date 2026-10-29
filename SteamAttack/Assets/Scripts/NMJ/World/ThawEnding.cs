using System.Collections.Generic;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 해빙 엔딩 연출 (통합 기획서 4-3의 2) — 보스 처치 문구가 뜨는 순간부터 몇 초에 걸쳐 멈춘 도성이 풀린다.
    ///
    ///   안개   회청빛 → 새벽 한지빛          빛   차가운 흰빛 → 낮게 깔린 따뜻한 빛
    ///   주변광 차갑게 → 따뜻하게             눈   허공에 멈춘 눈송이가 다시 떨어진다
    ///
    /// 월드에 이미 있는 안개·빛·눈 설정을 그대로 바꾸므로 추가 에셋이 없다. NMJ_Main 씬에 둔다.
    /// (인스펙터 메뉴 '미리 보기'로 바로 볼 수 있다)
    /// </summary>
    public class ThawEnding : MonoBehaviour
    {
        [Header("연결 (비우면 자동)")]
        [SerializeField] private Light sun;
        [Tooltip("다시 떨어지게 할 눈 파티클. 비우면 이름에 '눈'이 들어간 파티클을 모두 찾는다")]
        [SerializeField] private List<ParticleSystem> snow = new List<ParticleSystem>();

        [Header("풀리는 모습")]
        [SerializeField, Min(0.5f)] private float duration = 6f;
        [SerializeField] private Color dawnFog = new Color(0.93f, 0.89f, 0.80f, 1f);       // 새벽 한지빛
        [SerializeField] private Color dawnAmbient = new Color(0.70f, 0.64f, 0.58f, 1f);
        [SerializeField] private Color dawnSun = new Color(1f, 0.86f, 0.68f, 1f);
        [SerializeField, Min(0f)] private float dawnSunIntensity = 1.3f;
        [Tooltip("해의 높이(도). 낮을수록 길게 깔리는 아침 빛")]
        [SerializeField] private float dawnSunPitch = 14f;
        [Tooltip("다시 떨어지는 눈의 중력 배율")]
        [SerializeField] private float snowFallGravity = 0.06f;

        private bool started;
        private float timer;
        private Color fog0, amb0, sun0;
        private float sunI0;
        private Quaternion sunRot0, sunRot1;
        private readonly List<float> gravity0 = new List<float>();
        private readonly List<Camera> cams = new List<Camera>();
        private readonly List<Color> camBg0 = new List<Color>();
        private bool wasShowing;

        private void Start()
        {
            if (sun == null)
                foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (snow.Count == 0)
                foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
                    if (ps.name.Contains("눈")) snow.Add(ps);
        }

        private void Update()
        {
            bool showing = BossDefeatBanner.IsShowing;
            if (showing && !wasShowing) Begin();
            wasShowing = showing;
            if (!started || timer >= duration) return;

            timer += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / duration));
            RenderSettings.fogColor = Color.Lerp(fog0, dawnFog, k);
            // 하늘(카메라 배경)도 안개와 같은 색으로 따라간다
            for (int i = 0; i < cams.Count; i++) if (cams[i] != null) cams[i].backgroundColor = Color.Lerp(camBg0[i], dawnFog, k);
            RenderSettings.ambientLight = Color.Lerp(amb0, dawnAmbient, k);
            if (sun != null)
            {
                sun.color = Color.Lerp(sun0, dawnSun, k);
                sun.intensity = Mathf.Lerp(sunI0, dawnSunIntensity, k);
                sun.transform.rotation = Quaternion.Slerp(sunRot0, sunRot1, k);
            }
            for (int i = 0; i < snow.Count; i++)
            {
                if (snow[i] == null) continue;
                var main = snow[i].main;
                main.gravityModifierMultiplier = Mathf.Lerp(gravity0[i], snowFallGravity, k);
            }
        }

        [ContextMenu("미리 보기")]
        private void Begin()
        {
            if (started) return;
            started = true;
            NGH.ReviewedAudio.Play("Initial/09_World/ThawEnding_v01");
            timer = 0f;
            fog0 = RenderSettings.fogColor;
            amb0 = RenderSettings.ambientLight;
            if (sun != null)
            {
                sun0 = sun.color;
                sunI0 = sun.intensity;
                sunRot0 = sun.transform.rotation;
                Vector3 e = sunRot0.eulerAngles;
                sunRot1 = Quaternion.Euler(dawnSunPitch, e.y, e.z);
            }
            cams.Clear();
            camBg0.Clear();
            foreach (Camera c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                if (c.clearFlags == CameraClearFlags.SolidColor) { cams.Add(c); camBg0.Add(c.backgroundColor); }
            gravity0.Clear();
            foreach (ParticleSystem ps in snow) gravity0.Add(ps != null ? ps.main.gravityModifierMultiplier : 0f);
        }
    }
}
