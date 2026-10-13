#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 대기·걷기·달리기·구르기 클립을 Mixamo FBX(Idle, Walking, Fast Run, Sprinting Forward Roll)에서 구워 NGH_PlayerAnimator에 연결.
    ///  메뉴: Tools/NGH/Build Locomotion Clips (Idle·Walk·Run·Roll)
    ///  - 원본 FBX를 휴머노이드로 임포트 → NGH_ChgHumanAvatar로 플레이어(Generic) 뼈대에 옮겨 구움 (앞으로 가는 이동은 빼고 제자리 동작)
    ///  - Locomotion 블렌드 트리의 대기(0)·걷기(0.5)·달리기(1) 자리와 Roll 상태에 연결
    ///  - 구르기가 rollTime 안에 끝나도록 프리팹의 rollAnimSpeed를 새 클립 길이에 맞춤
    /// </summary>
    public static class LocomotionClipBuilder
    {
        const string SrcDir = "Assets/Animations/NGH/";
        const string ControllerPath = "Assets/Animations/NGH/NGH_PlayerAnimator.controller";
        const string PlayerPrefabPath = "Assets/Prefabs/NGH/NGH_Player.prefab";
        const string LocomotionState = "Locomotion";
        const string RollState = "Roll";
        const float SrcFps = 30f;   // MocapComboBuilder의 원본 프레임 단위

        static readonly (string fbx, string outName, bool loop)[] Clips =
        {
            ("Idle", "NGH_Anim_Idle", true),
            ("Walking", "NGH_Anim_Walk", true),
            ("Fast Run", "NGH_Anim_FastRun", true),
            ("Sprinting Forward Roll", "NGH_Anim_Roll", false),
        };

        [MenuItem("Tools/NGH/Build Locomotion Clips (Idle·Walk·Run·Roll)")]
        public static void BuildAll()
        {
            var baked = new AnimationClip[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
            {
                string path = SrcDir + Clips[i].fbx + ".fbx";
                var src = ImportAsHumanoid(path);
                if (!src) return;
                float frames = src.length * SrcFps;
                var seg = new MocapComboBuilder.Segment
                {
                    outName = Clips[i].outName, source = path, loop = Clips[i].loop, chainFrame = frames,
                    retime = new[] { Vector2.zero, new Vector2(frames, src.length) },
                };
                baked[i] = MocapComboBuilder.Build(seg);
                if (!baked[i]) return;
            }
            AssetDatabase.SaveAssets();
            Assign(baked[0], baked[1], baked[2], baked[3]);
        }

        // 휴머노이드로 바뀐 CHG 클립을 쓰는 상태들: 제너릭 플레이어는 휴머노이드 클립을 재생하지 못해 T포즈가 됨 → 플레이어 뼈대로 다시 구움
        static readonly (string src, string outName, string state, bool loop)[] StateClips =
        {
            ("Assets/Animations/CHG/chg_anim_drawsword.fbx",   "NGH_Anim_Draw",        "Draw",        false),
            ("Assets/Animations/CHG/chg_anim_sheathsword.fbx", "NGH_Anim_Sheath",      "Sheath",      false),
            ("Assets/Animations/CHG/chg_anim_hit.fbx",         "NGH_Anim_HitSmall",    "HitSmall",    false),
            ("Assets/Animations/CHG/chg_anim_hit2.fbx",        "NGH_Anim_HitLarge",    "HitLarge",    false),
            ("Assets/Animations/CHG/chg_anim_death.fbx",       "NGH_Anim_Death",       "Death",       false),
            ("Assets/Animations/CHG/chg_anim_standup.fbx",     "NGH_Anim_StandUp",     "StandUp",     false),
            ("Assets/Animations/CHG/chg_anim_shot.fbx",        "NGH_Anim_Shot",        "Shot",        false),
            ("Assets/Animations/CHG/chg_anim_leftwalk.fbx",    "NGH_Anim_StrafeLeft",  "StrafeLeft",  true),
            ("Assets/Animations/CHG/chg_anim_rightwalk.fbx",   "NGH_Anim_StrafeRight", "StrafeRight", true),
        };

        [MenuItem("Tools/NGH/Bake Humanoid State Clips (Draw·Sheath·Hit·Death·StandUp·Shot·Strafe)")]
        public static void BakeStateClips()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var states = controller.layers[0].stateMachine.states;
            var log = new System.Text.StringBuilder("[NGH] 상태 클립 다시 굽기:");
            foreach (var (srcPath, outName, stateName, loop) in StateClips)
            {
                var src = AssetDatabase.LoadAllAssetsAtPath(srcPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (!src) { Debug.LogError("[NGH] 원본 클립 없음: " + srcPath); continue; }
                if (!src.humanMotion) { log.Append($" {stateName}(제너릭이라 그대로)"); continue; }
                float frames = src.length * SrcFps;
                var seg = new MocapComboBuilder.Segment
                {
                    outName = outName, source = srcPath, loop = loop, chainFrame = frames,
                    retime = new[] { Vector2.zero, new Vector2(frames, src.length) },
                };
                var baked = MocapComboBuilder.Build(seg);
                if (!baked) continue;
                var state = states.FirstOrDefault(s => s.state.name == stateName).state;
                if (state == null) { Debug.LogError("[NGH] 애니메이터에 상태가 없음: " + stateName); continue; }
                state.motion = baked;
                EditorUtility.SetDirty(state);
                log.Append($" {stateName} {baked.length:0.00}s;");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        static AnimationClip ImportAsHumanoid(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) { Debug.LogError("[NGH] 원본 FBX를 찾지 못했습니다: " + path); return null; }
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            var avatar = assets.OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[NGH] 휴머노이드 아바타를 만들지 못했습니다 (뼈대 자동 매핑 실패): " + path);
                return null;
            }
            var clip = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (!clip) Debug.LogError("[NGH] FBX 안에 애니메이션이 없습니다: " + path);
            return clip;
        }

        static void Assign(AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip roll)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var states = controller.layers[0].stateMachine.states;

            var tree = states.First(s => s.state.name == LocomotionState).state.motion as BlendTree;
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                // 이전 걷기 클립용 배속(1.6)은 새 클립에 맞지 않아 1로 되돌림
                if (Mathf.Approximately(children[i].threshold, 0f)) { children[i].motion = idle; children[i].timeScale = 1f; }
                else if (Mathf.Approximately(children[i].threshold, 0.5f)) { children[i].motion = walk; children[i].timeScale = 1f; }
                else if (Mathf.Approximately(children[i].threshold, 1f)) { children[i].motion = run; children[i].timeScale = 1f; }
            }
            tree.children = children;
            states.First(s => s.state.name == RollState).state.motion = roll;
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(controller);

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var player = root.GetComponent<PlayerController>();
                float before = player.rollAnimSpeed;
                player.rollAnimSpeed = roll.length / Mathf.Max(0.05f, player.rollTime);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[NGH] 이동 모션 연결 완료: 대기 {idle.length:0.00}s, 걷기 {walk.length:0.00}s, 달리기 {run.length:0.00}s, 구르기 {roll.length:0.00}s " +
                          $"(Roll Anim Speed {before:0.##} → {player.rollAnimSpeed:0.##}, 구르기 시간 {player.rollTime:0.##}s 유지)");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
