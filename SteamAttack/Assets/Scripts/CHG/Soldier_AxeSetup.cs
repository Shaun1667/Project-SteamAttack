#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CHG
{
    /// <summary>
    /// 근거리 잡몹(도끼 병사) 세팅: Humanoid 설정, 재질·텍스처, 도끼 장착, Animator, 프리팹.
    /// 처음 한 번은 Unity가 스크립트를 불러올 때 자동 실행되고, 이후엔 메뉴 CHG/Build Soldier_Axe (도끼 병사) 로 다시 만들 수 있습니다.
    /// (다시 만들면 프리팹·컨트롤러를 새로 만들기 때문에 Inspector 에서 바꾼 값은 초기화됩니다)
    ///
    /// Animator 파라미터 (적 AI에서 사용)
    ///   Speed (float)  : -0.5 뒤로걷기 / 0 대기 / 0.5 걷기 / 1 달리기 (사이 값은 자연스럽게 섞임)
    ///   Attack (trigger)       : 일반 공격 (내려찍기, 패링 불가)
    ///   HeavyAttack (trigger)  : 패링 가능 강공격 (달려 점프 공격)
    ///   Hit (trigger)          : 강한 피격
    ///   Parried (trigger)      : 패링 당함 (자세 붕괴, 피격과 같은 동작)
    ///   Die (trigger)          : 사망 (끝 자세 유지)
    /// </summary>
    [InitializeOnLoad]
    public static class Soldier_AxeSetup
    {
        const string ModelPath = "Assets/Arts/Models/CHG/chg_Soldier.fbx";
        const string AxePath = "Assets/Arts/Models/CHG/chg_Axe3.fbx";
        const string TexDir = "Assets/Arts/Textures/CHG/";
        const string MatDir = "Assets/Materials/CHG/";
        const string AnimDir = "Assets/Animations/CHG/";
        const string ControllerPath = AnimDir + "chg_Soldier_AxeAnimator.controller";
        const string PrefabPath = "Assets/Prefabs/CHG/chg_Soldier_Axe.prefab";
        const string AutoKey = "chg_Soldier_v1";

        // 클립 이름 → (파일, 반복 여부)
        static readonly string[][] Clips =
        {
            new[] { "Idle", "chg_BreathingIdle", "loop" },
            new[] { "Walk", "chg_Walking", "loop" },
            new[] { "Run", "chg_RunForward", "loop" },
            new[] { "WalkBack", "chg_WalkBack", "loop" },
            new[] { "Attack", "chg_MeleeAttackDownward", "" },
            new[] { "HeavyAttack", "chg_MeleeRunJumpAttack", "" },
            new[] { "Hit", "chg_ReactLargeFromRight", "" },
            new[] { "Death", "chg_DeathForward", "" },
        };

        static Soldier_AxeSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(AutoKey, false)) return;
                if (!File.Exists(ModelPath) || !File.Exists(TexDir + "chg_soldier_basecolor.png")) return;
                if (File.Exists(PrefabPath)) { EditorPrefs.SetBool(AutoKey, true); return; }   // 이미 있으면 건드리지 않음
                EditorPrefs.SetBool(AutoKey, true);
                Build();
            };
        }

        [MenuItem("CHG/Build Soldier_Axe (도끼 병사)")]
        public static void BuildFromMenu()
        {
            if (File.Exists(PrefabPath) && !EditorUtility.DisplayDialog("chg_Soldier_Axe 다시 만들기",
                    "프리팹과 Animator를 새로 만들면 붙여 둔 AI·HP·체력바 등 컴포넌트와 Inspector에서 바꾼 값이 초기화됩니다.\n계속할까요?", "다시 만들기", "취소"))
            {
                return;
            }
            Build();
        }

        public static void Build()
        {
            // 1) 텍스처 설정
            SetupTexture(TexDir + "chg_soldier_normal.png", true);
            SetupTexture(TexDir + "chg_soldier_basecolor.png", false);
            SetupTexture(TexDir + "chg_axe3_normal.png", true);
            SetupTexture(TexDir + "chg_axe3_basecolor.png", false);

            // 2) 재질 (URP Lit) 만들고 FBX 재질을 이걸로 연결
            var soldierMat = MakeMaterial("chg_mat_soldier", "chg_soldier");
            var axeMat = MakeMaterial("chg_mat_axe3", "chg_axe3");
            RemapMaterials(ModelPath, soldierMat, true);
            RemapMaterials(AxePath, axeMat, false);

            // 3) 애니메이션: Humanoid, 반복 설정, 회전·높이는 자세에 고정 (이동은 코드에서)
            var clip = new Dictionary<string, AnimationClip>();
            foreach (var c in Clips)
            {
                string path = AnimDir + c[1] + ".fbx";
                SetupAnimation(path, c[2] == "loop");
                var a = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                if (!a) { Debug.LogError("[CHG] 병사 애니메이션을 찾지 못했습니다: " + path); return; }
                clip[c[0]] = a;
            }

            // 4) Animator Controller
            var ctrl = BuildController(clip);

            // 5) 프리팹 (도끼 장착)
            BuildPrefab(ctrl, axeMat);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- 텍스처 / 재질
        static void SetupTexture(string path, bool normal)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool changed = false;
            var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != want) { ti.textureType = want; changed = true; }
            if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; changed = true; }   // 잡몹이라 2048이면 충분
            if (changed) ti.SaveAndReimport();
        }

        static Material MakeMaterial(string name, string texPrefix)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            var bc = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + texPrefix + "_basecolor.png");
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + texPrefix + "_normal.png");
            if (bc) m.SetTexture("_BaseMap", bc);
            if (nm) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void RemapMaterials(string fbxPath, Material mat, bool humanoid)
        {
            var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (mi == null) return;
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>())
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), mat);
            if (humanoid)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            mi.SaveAndReimport();
        }

        static void SetupAnimation(string path, bool loop)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.SaveAndReimport();   // Humanoid 로 바꾼 뒤의 기본 클립 정보를 다시 읽기 위해 먼저 저장
            mi = AssetImporter.GetAtPath(path) as ModelImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;        // Root Transform Rotation: Bake Into Pose
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;         // Root Transform Position (Y): Bake Into Pose
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = loop;      // 제자리 반복 동작만 XZ 고정 (공격·사망은 코드/루트모션으로 이동 가능하게 둠)
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
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (var t in new[] { "Attack", "HeavyAttack", "Hit", "Parried", "Die" })
                ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;
            BlendTree tree;
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clip["WalkBack"], -0.5f);
            tree.AddChild(clip["Idle"], 0f);
            tree.AddChild(clip["Walk"], 0.5f);
            tree.AddChild(clip["Run"], 1f);
            loco.writeDefaultValues = false;
            sm.defaultState = loco;

            var attack = State(sm, "Attack", clip["Attack"]);
            var heavy = State(sm, "HeavyAttack", clip["HeavyAttack"]);
            var hit = State(sm, "Hit", clip["Hit"]);
            var parried = State(sm, "Parried", clip["Hit"]);
            var death = State(sm, "Death", clip["Death"]);

            AnyTo(sm, attack, "Attack", 0.1f);
            AnyTo(sm, heavy, "HeavyAttack", 0.1f);
            AnyTo(sm, hit, "Hit", 0.05f);
            AnyTo(sm, parried, "Parried", 0.05f);
            AnyTo(sm, death, "Die", 0.1f);

            foreach (var s in new[] { attack, heavy, hit, parried })
            {
                var t = s.AddTransition(loco);
                t.hasExitTime = true; t.exitTime = 1f; t.hasFixedDuration = true; t.duration = 0.2f;
            }
            // Death 는 끝 자세 유지 (나가는 연결 없음)

            EditorUtility.SetDirty(ctrl);
            Debug.Log("[CHG][검증] chg_Soldier_AxeAnimator 생성: 상태 " + sm.states.Length + "개 (" + ControllerPath + ")");
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

        // ---------------------------------------------------------------- 프리팹 + 도끼
        static void BuildPrefab(AnimatorController ctrl, Material axeMat)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var axeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AxePath);
            if (!modelAsset) { Debug.LogError("[CHG] 병사 모델 없음: " + ModelPath); return; }

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
                root.name = "chg_Soldier_Axe";
                var anim = root.GetComponent<Animator>();
                if (!anim) anim = root.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;

                var hand = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RightHand");
                if (hand && axeAsset)
                {
                    var axe = (GameObject)PrefabUtility.InstantiatePrefab(axeAsset, scene);
                    axe.name = "chg_Axe";
                    foreach (var r in axe.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = axeMat;
                    PlaceAxeInHand(root.transform, hand, axe.transform);
                    axe.transform.SetParent(hand, true);
                }
                else Debug.LogWarning("[CHG] RightHand 뼈 또는 도끼 모델을 찾지 못해 도끼를 붙이지 않았습니다.");

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[CHG][검증] chg_Soldier_Axe 프리팹 생성 (" + PrefabPath + "): 도끼 " + (hand && axeAsset ? "장착" : "없음") + ", Animator 연결");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static float WidthNear(Vector3[] vs, Vector3 axis, float len, float t)
        {
            var s = vs.Where(v => Mathf.Abs(Vector3.Dot(v, axis) - t) < len * 0.1f).ToArray();
            if (s.Length == 0) return 0f;
            var bb = new Bounds(s[0], Vector3.zero);
            foreach (var v in s) bb.Encapsulate(v);
            return bb.size.magnitude;
        }

        // 도끼 손잡이(가는 쪽 끝에서 25% 지점)를 손바닥에 두고, 도끼 머리가 캐릭터 앞쪽을 향하게 세움.
        // 정확한 쥐는 각도는 프리팹에서 chg_Axe 의 위치·회전으로 조절하세요.
        static void PlaceAxeInHand(Transform body, Transform hand, Transform axe)
        {
            var mf = axe.GetComponentInChildren<MeshFilter>();
            if (!mf || !mf.sharedMesh) return;
            var vs = mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v)).ToArray();
            // 가장 긴 방향 찾기
            var b = new Bounds(vs[0], Vector3.zero);
            foreach (var v in vs) b.Encapsulate(v);
            Vector3 axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right : (b.size.y >= b.size.z ? Vector3.up : Vector3.forward);
            float min = vs.Min(v => Vector3.Dot(v, axis)), max = vs.Max(v => Vector3.Dot(v, axis)), len = max - min;
            // 굵은 쪽 = 도끼 머리
            bool headAtMax = WidthNear(vs, axis, len, max - len * 0.1f) >= WidthNear(vs, axis, len, min + len * 0.1f);
            Vector3 handleToHead = headAtMax ? axis : -axis;
            float gripT = headAtMax ? min + len * 0.25f : max - len * 0.25f;
            var gripPts = vs.Where(v => Mathf.Abs(Vector3.Dot(v, axis) - gripT) < len * 0.05f).ToArray();
            Vector3 grip = gripPts.Length > 0 ? gripPts.Aggregate(Vector3.zero, (a, v) => a + v) / gripPts.Length : b.center;

            // 손바닥 위치: 손목에서 손끝 방향으로 약 6cm (이 모델은 손가락 뼈가 없어 아래팔→손 방향을 사용)
            Vector3 handDir = hand.parent ? (hand.position - hand.parent.position).normalized : -body.up;
            Vector3 palm = hand.position + handDir * 0.06f;
            // 손잡이는 손 방향과 수직, 머리는 캐릭터 앞쪽으로
            Vector3 want = Vector3.ProjectOnPlane(body.forward, handDir);
            if (want.sqrMagnitude < 1e-4f) want = body.forward;
            want.Normalize();

            var rot = Quaternion.FromToRotation(handleToHead, want);
            axe.rotation = rot * axe.rotation;
            Vector3 gripAfter = rot * (grip - axe.position) + axe.position;
            axe.position += palm - gripAfter;
        }
    }
}
#endif
