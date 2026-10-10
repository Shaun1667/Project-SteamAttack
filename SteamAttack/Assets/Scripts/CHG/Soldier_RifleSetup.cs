#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using NGH;

namespace CHG
{
    /// <summary>
    /// 원거리 잡몹(화승총 병사, chg_Soldier_Rifle) 세팅
    ///   1) 모델 크기 1.8배 (키 약 1.8m)
    ///   2) Basic Shooter Pack 애니메이션을 Humanoid로 바꾸고 반복 설정
    ///   3) Animator Controller (chg_Soldier_RifleAnimator)
    ///   4) 총알 프리팹 (EnemyBullet) + 재질
    ///   5) 프리팹 (chg_Soldier_Rifle): 콜라이더·Rigidbody·AI(원거리)·HP·피격·체력바, 총구 위치(chg_FirePoint)
    /// 처음 한 번은 Unity가 스크립트를 불러올 때 자동 실행되고(프리팹이 없을 때만),
    /// 이후엔 메뉴 CHG/Build Soldier_Rifle (원거리 병사) 로 다시 만들 수 있습니다.
    /// 다시 만들면 프리팹을 새로 만들기 때문에 직접 붙인 총·갓·탈과 Inspector 값이 초기화됩니다 (확인 창이 뜹니다).
    ///
    /// Animator 파라미터 (CHG_EnemyAI가 사용)
    ///   MoveX / MoveZ (float) : 몸 기준 좌우 / 앞뒤 이동 속도 (m/s) → 2D 블렌드 (대기·걷기·달리기·뒤로·옆걸음)
    ///   Speed (float)         : 이동 속도 값 (0이면 멈춤) — 비전투 대기 ↔ 이동 전환용
    ///   InCombat (bool)       : 전투 중이면 true → 조준 대기 / 비전투면 false → 평상시 대기(chg_Rifle Idle, 총 내림)
    ///   Kick (trigger)        : 근접 킥 (패링 가능 강공격) — 'chg_Rifle Turn And Kick'에서 킥 부분만 잘라 씀
    ///   KickSpeed (float)     : 킥 재생 속도 (준비 자세 때 느리게, 기본 1)
    ///   Fire (trigger)        : 사격 → 끝나면 자동으로 장전 → 이동으로 복귀
    ///   Parried (trigger)     : 패링당함 (비틀거림)
    ///   Die (trigger)         : 사망 (끝 자세 유지)
    /// </summary>
    [InitializeOnLoad]
    public static class Soldier_RifleSetup
    {
        const string ModelPath = "Assets/Arts/Models/CHG/chg_Soldier2.fbx";
        const string AnimDir = "Assets/Animations/CHG/";
        const string PackDir = AnimDir + "Basic Shooter Pack/";
        const string ControllerPath = AnimDir + "chg_Soldier_RifleAnimator.controller";
        const string PrefabPath = "Assets/Prefabs/CHG/chg_Soldier_Rifle.prefab";
        const string BulletPath = "Assets/Prefabs/CHG/chg_EnemyBullet.prefab";
        const string MatDir = "Assets/Materials/CHG/";
        const string AutoKey = "chg_Soldier2_v1";
        const string CalmIdleKey = "chg_Soldier2_calmidle_v1";   // 기존 컨트롤러에 비전투 대기 추가 (한 번)
        const string KickKey = "chg_Soldier_Rifle_kick_v1";       // 기존 컨트롤러·프리팹에 킥 추가 (한 번)
        const string KickClipFixKey = "chg_Soldier_Rifle_kickclip_v2"; // 킥 클립 위치 기준 수정 (한 번, 클립 설정만)
        const string KickClipPath = PackDir + "chg_Rifle Turn And Kick.fbx";
        const string HitboxPrefabPath = "Assets/Prefabs/CHG/CHG_EnemyAttackHitbox.prefab";
        // 원본 클립에서 킥 부분만: 시작 프레임으로부터 32~56 프레임 (돌기·마무리 제외, 약 0.8초)
        const int KickStartOffset = 32;
        const int KickLength = 24;
        const float ModelScale = 1.8f;

        // 이름, 파일(확장자 제외), 반복 여부
        static readonly string[][] PackClips =
        {
            new[] { "Idle", "chg_rifle aiming idle", "loop" },
            new[] { "CalmIdle", "chg_Rifle Idle", "loop" },
            new[] { "Walk", "chg_walking", "loop" },
            new[] { "WalkBack", "chg_walking backwards", "loop" },
            new[] { "Run", "chg_rifle run", "loop" },
            new[] { "RunBack", "chg_run backwards", "loop" },
            new[] { "StrafeL", "chg_strafe left", "loop" },
            new[] { "StrafeR", "chg_strafe right", "loop" },
            new[] { "Fire", "chg_firing rifle", "" },
            new[] { "Reload", "chg_reloading", "" },
        };

        // 근접 병사와 같이 쓰는 클립 (이미 Humanoid, 설정은 건드리지 않음)
        const string ParriedClipPath = AnimDir + "chg_ReactLargeFromRight.fbx";
        const string DeathClipPath = AnimDir + "chg_DeathForward.fbx";

        static Soldier_RifleSetup()
        {
            EditorApplication.delayCall += () =>
            {
                UpgradeCalmIdleOnce();
                UpgradeKickOnce();
                FixKickClipOnce();
                if (EditorPrefs.GetBool(AutoKey, false)) return;
                if (!File.Exists(ModelPath) || !File.Exists(PackDir + "chg_firing rifle.fbx")) return;
                EditorPrefs.SetBool(AutoKey, true);
                if (File.Exists(PrefabPath)) return;   // 이미 있으면 건드리지 않음
                BuildInternal();
            };
        }

        [MenuItem("CHG/Build Soldier_Rifle (원거리 병사)")]
        public static void Build()
        {
            if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog("chg_Soldier_Rifle 다시 만들기",
                    "프리팹을 새로 만들면 직접 붙인 총·갓·탈과 Inspector에서 바꾼 값이 초기화됩니다.\n계속할까요?", "다시 만들기", "취소"))
            {
                return;
            }
            BuildInternal();
        }

        static void BuildInternal()
        {
            // 1) 모델 크기
            SetupModel();

            // 2) 애니메이션
            var clip = new Dictionary<string, AnimationClip>();
            foreach (var c in PackClips)
            {
                string path = PackDir + c[1] + ".fbx";
                SetupAnimation(path, c[2] == "loop");
                var a = LoadClip(path);
                if (!a) { Debug.LogError("[CHG] 원거리 병사 애니메이션을 찾지 못했습니다: " + path); return; }
                clip[c[0]] = a;
            }
            clip["Parried"] = LoadClip(ParriedClipPath);
            clip["Death"] = LoadClip(DeathClipPath);
            if (File.Exists(KickClipPath))
            {
                SetupKickClip(KickClipPath);
                clip["Kick"] = LoadClip(KickClipPath);
            }
            if (!clip["Parried"] || !clip["Death"]) { Debug.LogError("[CHG] 패링당함/사망 클립을 찾지 못했습니다 (chg_ReactLargeFromRight / chg_DeathForward)."); return; }

            // 3) Animator Controller
            var ctrl = BuildController(clip);

            // 4) 총알
            var bullet = BuildBullet();

            // 5) 프리팹
            BuildPrefab(ctrl, bullet);
            AssetDatabase.SaveAssets();
        }

        static AnimationClip LoadClip(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
        }

        // ---------------------------------------------------------------- 모델 / 애니메이션
        static void SetupModel()
        {
            var mi = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (mi == null) return;
            bool changed = false;
            if (!Mathf.Approximately(mi.globalScale, ModelScale)) { mi.globalScale = ModelScale; changed = true; }
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        static void SetupAnimation(string path, bool loop)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();   // Humanoid 로 바꾼 뒤의 기본 클립 정보를 다시 읽기 위해 먼저 저장
                mi = AssetImporter.GetAtPath(path) as ModelImporter;
            }
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;        // 회전은 자세에 고정 (방향은 코드가 정함)
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;         // 높이도 자세에 고정
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;     // 앞뒤·좌우 이동은 빼냄 → 제자리 동작 (실제 이동은 코드가 함)
                c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        // ---------------------------------------------------------------- Animator
        static AnimatorController BuildController(Dictionary<string, AnimationClip> clip)
        {
            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("InCombat", AnimatorControllerParameterType.Bool);
            foreach (var t in new[] { "Fire", "Parried", "Die" })
                ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;
            BlendTree tree;
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            // 위치 = 각 클립의 실제 이동 속도 (m/s, 몸 기준 X 좌우 / Z 앞뒤)
            tree.AddChild(clip["Idle"], new Vector2(0f, 0f));
            tree.AddChild(clip["Walk"], new Vector2(0f, 1.0f));
            tree.AddChild(clip["Run"], new Vector2(0f, 3.2f));
            tree.AddChild(clip["WalkBack"], new Vector2(0f, -1.1f));
            tree.AddChild(clip["RunBack"], new Vector2(0f, -2.8f));
            tree.AddChild(clip["StrafeL"], new Vector2(-1.4f, 0f));
            tree.AddChild(clip["StrafeR"], new Vector2(0.76f, 0f));
            loco.writeDefaultValues = false;
            sm.defaultState = loco;

            var fire = State(sm, "Fire", clip["Fire"]);
            var reload = State(sm, "Reload", clip["Reload"]);
            var parried = State(sm, "Parried", clip["Parried"]);
            var death = State(sm, "Death", clip["Death"]);

            AnyTo(sm, fire, "Fire", 0.05f);
            AnyTo(sm, parried, "Parried", 0.05f);
            AnyTo(sm, death, "Die", 0.1f);

            // 사격 → 장전 → 이동
            Exit(fire, reload, 0.05f);
            Exit(reload, loco, 0.2f);
            Exit(parried, loco, 0.2f);
            // Death 는 끝 자세 유지 (나가는 연결 없음)

            AddCalmIdle(sm, loco, clip["CalmIdle"]);
            if (clip.TryGetValue("Kick", out var kickClip) && kickClip)
            {
                AddKickState(ctrl, sm, loco, kickClip);
            }

            EditorUtility.SetDirty(ctrl);
            Debug.Log("[CHG][검증] chg_Soldier_RifleAnimator 생성: 상태 " + sm.states.Length + "개, 이동 블렌드 클립 " + tree.children.Length + "개 (" + ControllerPath + ")");
            return ctrl;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip c)
        {
            var s = sm.AddState(name);
            s.motion = c;
            s.writeDefaultValues = false;
            return s;
        }

        static void AnyTo(AnimatorStateMachine sm, AnimatorState s, string trigger, float dur)
        {
            var t = sm.AddAnyStateTransition(s);
            t.hasExitTime = false; t.hasFixedDuration = true; t.duration = dur; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        }

        static void Exit(AnimatorState from, AnimatorState to, float dur)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true; t.exitTime = 1f; t.hasFixedDuration = true; t.duration = dur;
        }

        // ---------------------------------------------------------------- 비전투 대기
        // 비전투이고 멈춰 있으면 평상시 대기(총 내림), 전투가 시작되거나 움직이면 Locomotion(조준 대기·이동)
        static void AddCalmIdle(AnimatorStateMachine sm, AnimatorState loco, AnimationClip calmClip)
        {
            var calm = State(sm, "CalmIdle", calmClip);
            sm.defaultState = calm;

            var toLocoCombat = calm.AddTransition(loco);
            toLocoCombat.hasExitTime = false; toLocoCombat.hasFixedDuration = true; toLocoCombat.duration = 0.25f;
            toLocoCombat.AddCondition(AnimatorConditionMode.If, 0, "InCombat");

            var toLocoMove = calm.AddTransition(loco);
            toLocoMove.hasExitTime = false; toLocoMove.hasFixedDuration = true; toLocoMove.duration = 0.2f;
            toLocoMove.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");

            var toCalm = loco.AddTransition(calm);
            toCalm.hasExitTime = false; toCalm.hasFixedDuration = true; toCalm.duration = 0.35f;
            toCalm.AddCondition(AnimatorConditionMode.IfNot, 0, "InCombat");
            toCalm.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
        }

        // 이미 만들어진 컨트롤러에 비전투 대기를 한 번만 추가 (프리팹은 다시 만들지 않음)
        static void UpgradeCalmIdleOnce()
        {
            if (EditorPrefs.GetBool(CalmIdleKey, false)) return;
            string calmPath = PackDir + "chg_Rifle Idle.fbx";
            if (!File.Exists(calmPath) || !File.Exists(ControllerPath)) return;
            EditorPrefs.SetBool(CalmIdleKey, true);
            UpgradeCalmIdle();
        }

        [MenuItem("CHG/Soldier_Rifle 비전투 대기 추가 (컨트롤러만)")]
        public static void UpgradeCalmIdle()
        {
            string calmPath = PackDir + "chg_Rifle Idle.fbx";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!ctrl) { Debug.LogError("[CHG] 컨트롤러가 없습니다: " + ControllerPath); return; }
            var sm = ctrl.layers[0].stateMachine;
            if (sm.states.Any(s => s.state.name == "CalmIdle")) { Debug.Log("[CHG] chg_Soldier_RifleAnimator 에 이미 비전투 대기(CalmIdle)가 있습니다."); return; }

            SetupAnimation(calmPath, true);
            var calmClip = LoadClip(calmPath);
            if (!calmClip) { Debug.LogError("[CHG] 비전투 대기 클립을 찾지 못했습니다: " + calmPath); return; }

            if (!ctrl.parameters.Any(p => p.name == "Speed")) ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            if (!ctrl.parameters.Any(p => p.name == "InCombat")) ctrl.AddParameter("InCombat", AnimatorControllerParameterType.Bool);

            var loco = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
            if (!loco) { Debug.LogError("[CHG] Locomotion 상태를 찾지 못했습니다."); return; }
            AddCalmIdle(sm, loco, calmClip);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log("[CHG][검증] chg_Soldier_RifleAnimator 에 비전투 대기(CalmIdle, chg_Rifle Idle) 추가 — 전투 시작 시 조준 대기로 전환");
        }

        // ---------------------------------------------------------------- 킥 (패링 가능 강공격)
        // 'Turn And Kick'에서 킥 부분만 잘라 쓰고, 회전 기준을 킥 시작 순간의 몸 방향으로 맞춰 정면으로 차게 함
        static void SetupKickClip(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
                mi = AssetImporter.GetAtPath(path) as ModelImporter;
            }
            var defaults = mi.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0) return;
            var c = defaults[0];
            float start = c.firstFrame + KickStartOffset;
            c.firstFrame = start;
            c.lastFrame = Mathf.Min(start + KickLength, defaults[0].lastFrame);
            c.loopTime = false;
            c.loopPose = false;
            c.lockRootRotation = true;
            c.keepOriginalOrientation = false;   // 몸 방향 기준 → 잘라낸 첫 프레임의 몸 방향이 정면
            c.lockRootHeightY = true;
            c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;        // 이동은 빼냄 (제자리)
            c.keepOriginalPositionXZ = false;    // 몸 중심 기준 → 잘라낸 첫 프레임에서 몸이 옆으로 비켜 있지 않고 콜라이더 위치에 옴
            mi.clipAnimations = new[] { c };
            mi.SaveAndReimport();
        }

        static void AddKickState(AnimatorController ctrl, AnimatorStateMachine sm, AnimatorState loco, AnimationClip kickClip)
        {
            if (!ctrl.parameters.Any(p => p.name == "Kick")) ctrl.AddParameter("Kick", AnimatorControllerParameterType.Trigger);
            if (!ctrl.parameters.Any(p => p.name == "KickSpeed"))
            {
                ctrl.AddParameter("KickSpeed", AnimatorControllerParameterType.Float);
                var ps = ctrl.parameters;
                foreach (var p in ps) if (p.name == "KickSpeed") p.defaultFloat = 1f;
                ctrl.parameters = ps;
            }
            var kick = State(sm, "Kick", kickClip);
            kick.speedParameterActive = true;
            kick.speedParameter = "KickSpeed";
            AnyTo(sm, kick, "Kick", 0.1f);
            Exit(kick, loco, 0.2f);
        }

        // 프리팹의 AI 값 (킥 사용, 공격 판정 프리팹 연결)
        static void ApplyKickValues(SerializedObject aso)
        {
            var hitbox = AssetDatabase.LoadAssetAtPath<EnemyAttackHitbox>(HitboxPrefabPath);
            if (hitbox) aso.FindProperty("attackHitboxPrefab").objectReferenceValue = hitbox;
            else Debug.LogWarning("[CHG] 공격 판정 프리팹을 찾지 못했습니다: " + HitboxPrefabPath);
            aso.FindProperty("rangedAttack.kickEnabled").boolValue = true;
            Set(aso, "rangedAttack.kickRange", 2.5f);
            aso.FindProperty("rangedAttack.kickDamage").intValue = 25;
            Set(aso, "rangedAttack.kickCooldown", 4f);
            Set(aso, "rangedAttack.kickPrepTime", 0.7f);
            Set(aso, "rangedAttack.kickPrepAnimSpeed", 0.15f);
            Set(aso, "rangedAttack.kickHitClipTime", 0.3f);
            Set(aso, "rangedAttack.kickActiveTime", 0.13f);
            Set(aso, "rangedAttack.kickClipLength", 0.8f);
            Set(aso, "rangedAttack.kickLungeDistance", 0.3f);
            aso.FindProperty("rangedAttack.kickParryable").boolValue = true;
        }

        // 킥 시작 때 몸이 콜라이더 옆으로 비켜 서던 문제: 클립 위치 기준만 다시 적용 (프리팹·Inspector 값은 건드리지 않음)
        static void FixKickClipOnce()
        {
            if (EditorPrefs.GetBool(KickClipFixKey, false)) return;
            if (!File.Exists(KickClipPath)) return;
            EditorPrefs.SetBool(KickClipFixKey, true);
            SetupKickClip(KickClipPath);
            Debug.Log("[CHG][검증] 킥 클립 위치 기준 수정 (몸 중심 기준) — 킥이 콜라이더 위치에서 시작");
        }

        static void UpgradeKickOnce()
        {
            if (EditorPrefs.GetBool(KickKey, false)) return;
            if (!File.Exists(KickClipPath) || !File.Exists(ControllerPath) || !File.Exists(PrefabPath)) return;
            EditorPrefs.SetBool(KickKey, true);
            UpgradeKick();
        }

        // 이미 만들어진 컨트롤러·프리팹에 킥만 추가 (프리팹에 직접 붙인 총·모자·탈은 그대로)
        [MenuItem("CHG/Soldier_Rifle 킥 추가 (컨트롤러·값만)")]
        public static void UpgradeKick()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!ctrl) { Debug.LogError("[CHG] 컨트롤러가 없습니다: " + ControllerPath); return; }
            var sm = ctrl.layers[0].stateMachine;

            SetupKickClip(KickClipPath);
            var kickClip = LoadClip(KickClipPath);
            if (!kickClip) { Debug.LogError("[CHG] 킥 클립을 찾지 못했습니다: " + KickClipPath); return; }

            if (!sm.states.Any(s => s.state.name == "Kick"))
            {
                var loco = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
                if (!loco) { Debug.LogError("[CHG] Locomotion 상태를 찾지 못했습니다."); return; }
                AddKickState(ctrl, sm, loco, kickClip);
                EditorUtility.SetDirty(ctrl);
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var ai = root.GetComponent<EnemyAI>();
                if (!ai) { Debug.LogError("[CHG] 프리팹에 CHG_EnemyAI가 없습니다: " + PrefabPath); return; }
                var aso = new SerializedObject(ai);
                ApplyKickValues(aso);
                aso.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            AssetDatabase.SaveAssets();
            Debug.Log("[CHG][검증] chg_Soldier_Rifle 킥 추가: 2.5m 안이면 킥, 피해 25, 쿨타임 4초, 준비 자세 0.7초 (패링 가능)");
        }

        // ---------------------------------------------------------------- 총알
        static EnemyBullet BuildBullet()
        {
            var existing = AssetDatabase.LoadAssetAtPath<EnemyBullet>(BulletPath);
            if (existing) return existing;   // 이미 있으면 그대로 사용 (Inspector 값 유지)

            var headMat = MakeMaterial("chg_mat_EnemyBullet", "Universal Render Pipeline/Unlit", false);
            var trailMat = MakeMaterial("chg_mat_EnemyBulletTrail", "Universal Render Pipeline/Particles/Unlit", true);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("chg_EnemyBullet");
                SceneManager_Move(go, scene);

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                Object.DestroyImmediate(head.GetComponent<Collider>());
                head.transform.SetParent(go.transform, false);
                head.transform.localScale = Vector3.one * 0.1f;
                var hr = head.GetComponent<MeshRenderer>();
                hr.sharedMaterial = headMat;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hr.receiveShadows = false;

                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.25f;
                trail.minVertexDistance = 0.05f;
                trail.widthMultiplier = 1f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.07f), new Keyframe(1f, 0f));
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.15f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = grad;
                trail.sharedMaterial = trailMat;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;

                var bullet = go.AddComponent<EnemyBullet>();
                var so = new SerializedObject(bullet);
                so.FindProperty("headRenderer").objectReferenceValue = hr;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(go, BulletPath);
                Debug.Log("[CHG][검증] chg_EnemyBullet 프리팹 생성 (" + BulletPath + ")");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            return AssetDatabase.LoadAssetAtPath<EnemyBullet>(BulletPath);
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        static Material MakeMaterial(string name, string shaderName, bool additive)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;

            var sh = Shader.Find(shaderName);
            if (!sh) sh = Shader.Find("Sprites/Default");
            m = new Material(sh);
            if (additive)
            {
                // 더하기(빛나는) 반투명
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetColor("_BaseColor", Color.white);
            }
            else
            {
                // 밝게 (Bloom이 켜져 있으면 번져 보임)
                m.SetColor("_BaseColor", new Color(2.0f, 1.2f, 0.5f, 1f));
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------------------------------------------------------------- 프리팹
        static void BuildPrefab(AnimatorController ctrl, EnemyBullet bullet)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!modelAsset) { Debug.LogError("[CHG] 원거리 병사 모델 없음: " + ModelPath); return; }

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
                root.name = "chg_Soldier_Rifle";
                root.tag = "Enemy";

                var anim = root.GetComponent<Animator>();
                if (!anim) anim = root.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;

                // 몸 (근접 병사와 같은 값)
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.9f, 0f);
                capsule.height = 1.8f;
                capsule.radius = 0.3f;
                capsule.direction = 1;

                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 80f;
                rb.useGravity = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                // 총구 위치 (총을 붙이면 이 오브젝트를 총구 끝으로 옮기거나 총 아래로 넣으세요)
                var firePoint = new GameObject("chg_FirePoint").transform;
                firePoint.SetParent(root.transform, false);
                firePoint.localPosition = new Vector3(0f, 1.45f, 0.6f);

                // HP · 피격 · 체력바
                var health = root.AddComponent<EnemyHealth>();
                var hso = new SerializedObject(health);
                Set(hso, "maxHp", 40f);
                Set(hso, "hideDelay", 2.4f);
                Set(hso, "respawnDelay", 5f);
                hso.ApplyModifiedPropertiesWithoutUndo();

                var ai = root.AddComponent<EnemyAI>();
                var aso = new SerializedObject(ai);
                Set(aso, "wanderRadius", 0f);
                Set(aso, "wanderSpeed", 1.0f);
                Set(aso, "detectRange", 14f);
                Set(aso, "loseRange", 20f);
                Set(aso, "loseDelay", 3f);
                Set(aso, "chaseSpeed", 3.2f);
                Set(aso, "attackRange", 1.8f);
                aso.FindProperty("heavyAttack.enabled").boolValue = false;
                aso.FindProperty("rangedAttack.enabled").boolValue = true;
                aso.FindProperty("rangedAttack.bulletPrefab").objectReferenceValue = bullet;
                aso.FindProperty("rangedAttack.firePoint").objectReferenceValue = firePoint;
                aso.FindProperty("rangedAttack.damage").intValue = 18;
                Set(aso, "rangedAttack.bulletSpeed", 15f);
                Set(aso, "rangedAttack.bulletRange", 25f);
                Set(aso, "rangedAttack.bulletRadius", 0.12f);
                Set(aso, "rangedAttack.fireInterval", 5f);
                Set(aso, "rangedAttack.aimTime", 0.8f);
                Set(aso, "rangedAttack.fireAnimTime", 0.27f);
                Set(aso, "rangedAttack.reloadTime", 3.3f);
                Set(aso, "rangedAttack.firstShotDelay", 1f);
                Set(aso, "rangedAttack.preferredMaxDistance", 14f);
                Set(aso, "rangedAttack.retreatDistance", 5f);
                Set(aso, "rangedAttack.maxFireDistance", 18f);
                Set(aso, "rangedAttack.retreatSpeed", 1.1f);
                Set(aso, "rangedAttack.aimHeight", 1.2f);
                Set(aso, "hitStopTime", 0.1f);
                Set(aso, "parriedStunTime", 1.4f);
                if (File.Exists(KickClipPath)) ApplyKickValues(aso);
                aso.ApplyModifiedPropertiesWithoutUndo();

                var receiver = root.AddComponent<Damageable>();
                receiver.maxHp = 1000f;
                receiver.hp = 1000f;
                receiver.markerHeight = 2.05f;

                var relay = root.AddComponent<EnemyDamageRelay>();
                var rso = new SerializedObject(relay);
                rso.FindProperty("meleeReceiver").objectReferenceValue = receiver;
                rso.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<EnemyHpBar>();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[CHG][검증] chg_Soldier_Rifle 프리팹 생성 (" + PrefabPath + "): 원거리 AI, HP 40, 피해 18, 사격 간격 5초, 총알 15m/s");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void Set(SerializedObject so, string path, float value)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogWarning("[CHG] 필드를 찾지 못했습니다: " + path); return; }
            p.floatValue = value;
        }
    }
}
#endif
