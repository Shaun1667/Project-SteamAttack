#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Hero/8. Apply Roll Clip (Sprinting Forward Roll)
    /// Sprinting Forward Roll.fbx를 휴머노이드로 임포트하고, 그 클립을 NGH_Hero_Roll 에 덮어씀 (애니메이터 연결은 그대로)
    ///  - 루프·루트 처리 같은 클립 설정은 기존 NGH_Hero_Roll 값을 유지
    ///  - 구르기가 PlayerController.rollTime 안에 끝나도록 프리팹의 rollAnimSpeed를 새 클립 길이에 맞춤
    /// FBX를 바꿔 왔을 때 이 메뉴만 다시 실행하면 됩니다.
    /// </summary>
    public static class RollClipBuilder
    {
        const string SourcePath = "Assets/Animations/NGH/Sprinting Forward Roll.fbx";
        const string OutputPath = "Assets/Animations/NGH/Hero/NGH_Hero_Roll.anim";
        const string PlayerPrefabPath = "Assets/Prefabs/NGH/NGH_Player.prefab";

        [MenuItem("Tools/NGH/Hero/8. Apply Roll Clip (Sprinting Forward Roll)")]
        public static void Build()
        {
            // 1) 휴머노이드로 임포트 (믹사모 뼈대는 자동 매핑됨)
            var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
            var source = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (!source || !source.humanMotion) { Debug.LogError("[NGH] 휴머노이드 구르기 클립을 만들지 못했습니다: " + SourcePath); return; }

            // 2) 커브 복사. 설정은 기존 클립 것을 쓰되 길이만 새 클립에 맞춤
            var clip = new AnimationClip { name = "NGH_Hero_Roll", frameRate = source.frameRate };
            foreach (var b in AnimationUtility.GetCurveBindings(source))
                AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(source, b));
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath);
            var settings = AnimationUtility.GetAnimationClipSettings(existing ? existing : source);
            var srcSettings = AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = srcSettings.startTime;
            settings.stopTime = srcSettings.stopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else AssetDatabase.CreateAsset(clip, OutputPath);
            AssetDatabase.SaveAssets();

            // 3) 구르기 시간은 그대로 두고 재생 속도를 새 길이에 맞춤
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var player = root.GetComponent<PlayerController>();
                float before = player.rollAnimSpeed;
                player.rollAnimSpeed = clip.length / Mathf.Max(0.05f, player.rollTime);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[NGH] 구르기 클립 적용: {clip.length:0.00}s, Roll Anim Speed {before:0.##} → {player.rollAnimSpeed:0.##} (구르기 시간 {player.rollTime:0.##}s 유지)");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
#endif
