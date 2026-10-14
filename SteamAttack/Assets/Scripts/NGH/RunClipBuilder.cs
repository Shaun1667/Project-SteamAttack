#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Hero/6. Build Run Clip (NinjaRun 허리·무릎 조정)
    /// NinjaRun.fbx(휴머노이드)를 NGH_Hero_NinjaRun 으로 복사하면서
    ///  - 허리 세우기: 몸통을 torsoRaise 도만큼 뒤로 세우고, 허벅지는 그만큼 앞으로 보정해서 발 위치는 그대로
    ///  - 팔 당기기: 팔을 armForward 도만큼 앞으로 (상체를 세우면 팔도 같이 뒤로 젖혀지므로)
    ///  - 무릎 높이 제한: 허벅지를 앞으로 드는 근육 값을 legLiftLimit 근처에서 부드럽게 막음 (치마 허리선을 허벅지가 뚫지 않게)
    /// </summary>
    public static class RunClipBuilder
    {
        const string SourcePath = "Assets/Animations/NGH/NinjaRun.fbx";
        const string OutputPath = "Assets/Animations/NGH/Hero/NGH_Hero_NinjaRun.anim";

        public const float TorsoRaise = 15f;      // 척추를 뒤로 펴는 각도 (도)
        public const float LegLiftLimit = -0.7f;  // Upper Leg Front-Back 근육의 앞으로 드는 쪽 한계 (원본 최대 -0.95)
        public const float ArmForward = 40f;      // 팔을 앞으로 당기는 각도 (도). 손이 어깨 뒤 25cm → 약 14cm
        const float LegSoftness = 0.06f;   // 한계 근처에서 꺾이지 않고 부드럽게 눌리는 폭

        static readonly string[] LegMuscles = { "Left Upper Leg Front-Back", "Right Upper Leg Front-Back" };
        static readonly string[] ArmMuscles = { "Left Arm Front-Back", "Right Arm Front-Back" };

        [MenuItem("Tools/NGH/Hero/6. Build Run Clip (NinjaRun 허리·무릎·팔 조정)")]
        public static void BuildMenu()
        {
            var clip = Build(TorsoRaise, LegLiftLimit, ArmForward, OutputPath);
            Debug.Log($"[NGH] 달리기 클립 생성: {OutputPath} (허리 +{TorsoRaise}°, 허벅지 한계 {LegLiftLimit}, 팔 앞으로 {ArmForward}°)", clip);
        }

        public static AnimationClip Build(float torsoRaiseDeg, float legLiftLimit, float armForwardDeg, string outputPath)
        {
            // 팔 근육(Arm Front-Back)은 이 클립에서 음수가 앞쪽 (확인: -20 → 손이 5cm 앞으로), 범위는 기본 최솟값(-100도) 기준
            float armShift = armForwardDeg / HumanTrait.GetMuscleDefaultMin(MuscleIndex(ArmMuscles[0]));
            var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(outputPath), frameRate = source.frameRate };

            // 허리 세우기: 몸통 방향(RootQ)을 뒤로 젖히고, 그만큼 허벅지를 앞으로 보정해서 다리는 제자리
            // (휴머노이드는 몸통 방향을 골반·가슴 평균으로 고정하므로 척추 근육만 바꾸면 골반이 대신 숙여짐)
            // 허벅지 근육은 음수가 앞으로 드는 쪽, 범위는 기본 최솟값(-90도) 기준
            float legShift = -torsoRaiseDeg / -HumanTrait.GetMuscleDefaultMin(MuscleIndex(LegMuscles[0]));
            Quaternion tilt = Quaternion.AngleAxis(-torsoRaiseDeg, Vector3.right);

            var bindings = AnimationUtility.GetCurveBindings(source);
            foreach (var binding in bindings)
            {
                if (binding.propertyName.StartsWith("RootQ.")) continue;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (LegMuscles.Contains(binding.propertyName)) curve = Map(curve, v => SoftFloor(v + legShift, legLiftLimit, LegSoftness));
                else if (ArmMuscles.Contains(binding.propertyName)) curve = Map(curve, v => v + armShift);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            TiltRoot(source, clip, bindings, tilt);
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                existing.name = clip.name;
                Object.DestroyImmediate(clip);
                clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else AssetDatabase.CreateAsset(clip, outputPath);
            AssetDatabase.SaveAssets();
            return clip;
        }

        static int MuscleIndex(string name) => System.Array.IndexOf(HumanTrait.MuscleName, name);

        // RootQ 네 커브를 키 시점마다 함께 읽어 tilt를 곱함 (캐릭터 기준 오른쪽 축으로 회전)
        static void TiltRoot(AnimationClip source, AnimationClip clip, EditorCurveBinding[] bindings, Quaternion tilt)
        {
            var comps = new[] { "x", "y", "z", "w" }.Select(c => bindings.First(b => b.propertyName == "RootQ." + c)).ToArray();
            var curves = comps.Select(b => AnimationUtility.GetEditorCurve(source, b)).ToArray();
            var times = curves.SelectMany(c => c.keys.Select(k => k.time)).Distinct().OrderBy(t => t).ToArray();
            var outKeys = new Keyframe[4][];
            for (int c = 0; c < 4; c++) outKeys[c] = new Keyframe[times.Length];
            Quaternion prev = Quaternion.identity;
            for (int i = 0; i < times.Length; i++)
            {
                float t = times[i];
                var q = tilt * new Quaternion(curves[0].Evaluate(t), curves[1].Evaluate(t), curves[2].Evaluate(t), curves[3].Evaluate(t)).normalized;
                if (i > 0 && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // 부호 뒤집힘 방지
                prev = q;
                outKeys[0][i] = new Keyframe(t, q.x); outKeys[1][i] = new Keyframe(t, q.y);
                outKeys[2][i] = new Keyframe(t, q.z); outKeys[3][i] = new Keyframe(t, q.w);
            }
            for (int c = 0; c < 4; c++)
            {
                var curve = new AnimationCurve(outKeys[c]);
                for (int i = 0; i < times.Length; i++) curve.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, comps[c], curve);
            }
        }

        // 키마다 값을 바꾸고 접선은 다시 계산 (부드러운 곡선 유지)
        static AnimationCurve Map(AnimationCurve curve, System.Func<float, float> f)
        {
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++) keys[i].value = f(keys[i].value);
            var result = new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
            for (int i = 0; i < keys.Length; i++) result.SmoothTangents(i, 0f);
            return result;
        }

        // limit보다 작아지지 않게 부드럽게 막음 (limit + softness 위쪽은 거의 그대로)
        static float SoftFloor(float v, float limit, float softness)
        {
            float x = (v - limit) / softness;
            return limit + softness * (x > 20f ? x : Mathf.Log(1f + Mathf.Exp(x)));
        }
    }
}
#endif
