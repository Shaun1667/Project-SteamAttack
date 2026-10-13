#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 플레이어 모델을 Hero_Bound(휴머노이드)로 전환하는 에디터 도구.
    ///  1) Tools/NGH/Hero/1. Build Hero Clips & Animator
    ///     NGH_PlayerAnimator의 제너릭 클립(원래 플레이어 뼈대용)을 휴머노이드 클립으로 구워 NGH_HeroAnimator에 연결.
    ///     원래 클립을 플레이어 뼈대에 재생 → NGH_ChgHumanAvatar로 근육 값을 읽어 기록. 공격 모션을 다시 굽고 나면 이 메뉴만 다시 실행.
    ///  2) Tools/NGH/Hero/2. Swap NGH_Player Model To Hero_Bound (한 번만)
    ///     YPH 통합 프리팹에서 무기·칼집이 붙은 Hero_Bound를 복사해 넣고, 원래 모델(chg_Model)을 가리키던 참조를 옮긴 뒤 제거.
    /// </summary>
    public static class HeroConversion
    {
        const string ModelPath = "Assets/Arts/Models/CHG/chg_player.fbx";
        // 원래 뼈대 → 근육 값 변환용 아바타. YPH가 히어로 손에 칼 위치를 맞출 때 쓴 보정 아바타를 우선 사용 (손목 각도가 같아야 칼이 손에 맞음)
        const string SourceAvatarPath = "Assets/Animations/YPH/HeroPlayer/YPH_SourceHumanCalibration.asset";
        const string ChgAvatarPath = "Assets/Animations/NGH/NGH_ChgHumanAvatar.asset";
        const string HeroModelPath = "Assets/Arts/Models/YPH/HeroDynamics/Hero_Bound.fbx";
        const string SourceControllerPath = "Assets/Animations/NGH/NGH_PlayerAnimator.controller";
        const string HeroControllerPath = "Assets/Animations/NGH/NGH_HeroAnimator.controller";
        const string HeroClipDir = "Assets/Animations/NGH/Hero/";
        const string PlayerPrefabPath = "Assets/Prefabs/NGH/NGH_Player.prefab";
        const string YphPrefabPath = "Assets/Prefabs/YPH/HeroPlayer/YPH_HeroPlayerIntegration.prefab";
        const string YphHeroPath = "NGH_Player/Hero_Bound";
        const string OldModelName = "chg_Model";
        const float ModelYaw = 17.53f;   // 원래 모델은 앞을 보도록 플레이어 안에서 이만큼 돌려 배치돼 있었음
        const float Fps = 60f;

        // ================================================================ 1. 클립 · 애니메이터

        [MenuItem("Tools/NGH/Hero/1. Build Hero Clips & Animator")]
        public static void BuildHeroClips()
        {
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(SourceAvatarPath);
            if (!avatar || !avatar.isValid || !avatar.isHuman) avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ChgAvatarPath);
            var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(SourceControllerPath);
            if (!avatar || !avatar.isHuman) { Debug.LogError("[NGH] 원래 뼈대용 휴머노이드 아바타가 없습니다: " + SourceAvatarPath); return; }
            float facing = FacingCorrection(avatar);
            var yaw = Quaternion.Euler(0f, facing, 0f);
            Debug.Log($"[NGH] 변환 아바타 {avatar.name}, 몸 방향 보정 {facing:0.0}°");
            var swordFlip = LoadSwordFlip();
            if (swordFlip == null) Debug.LogWarning("[NGH] NGH_Player에서 히어로 칼을 찾지 못해 5타 칼 뒤집기를 건너뜀 (2번 메뉴 먼저)");
            if (!source) { Debug.LogError("[NGH] 원본 애니메이터가 없습니다: " + SourceControllerPath); return; }
            if (!AssetDatabase.IsValidFolder(HeroClipDir.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/Animations/NGH", "Hero");

            // 원본 애니메이터에서 쓰는 제너릭 클립 → 휴머노이드 클립
            var baked = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var clip in CollectClips(source))
            {
                if (baked.ContainsKey(clip)) continue;
                var flip = FlipSwordClips.Contains(clip.name) ? swordFlip : null;
                baked[clip] = clip.humanMotion ? clip : BakeHumanoid(clip, avatar, yaw, flip);
            }

            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(HeroControllerPath))
                AssetDatabase.CopyAsset(SourceControllerPath, HeroControllerPath);
            var hero = AssetDatabase.LoadAssetAtPath<AnimatorController>(HeroControllerPath);
            int replaced = 0;
            var srcStates = source.layers[0].stateMachine.states;
            foreach (var cs in hero.layers[0].stateMachine.states)
            {
                var src = srcStates.FirstOrDefault(s => s.state.name == cs.state.name).state;
                if (src == null) continue;
                replaced += Replace(cs.state, src.motion, baked);
            }
            EditorUtility.SetDirty(hero);
            AssetDatabase.SaveAssets();
            Debug.Log($"[NGH] 히어로 클립 {baked.Count}개 굽고 NGH_HeroAnimator에 {replaced}곳 연결");
        }

        static IEnumerable<AnimationClip> CollectClips(AnimatorController c)
        {
            foreach (var s in c.layers[0].stateMachine.states)
                foreach (var clip in ClipsOf(s.state.motion)) yield return clip;
        }

        static IEnumerable<AnimationClip> ClipsOf(Motion m)
        {
            if (m is AnimationClip clip) yield return clip;
            else if (m is BlendTree tree)
                foreach (var child in tree.children)
                    foreach (var c in ClipsOf(child.motion)) yield return c;
        }

        // 히어로 상태(복사본)의 클립을 원본 상태의 클립에 대응하는 휴머노이드 클립으로 바꿈 (블렌드 트리는 자식 순서대로)
        static int Replace(AnimatorState heroState, Motion sourceMotion, Dictionary<AnimationClip, AnimationClip> baked)
        {
            if (sourceMotion is AnimationClip clip && baked.TryGetValue(clip, out var b)) { heroState.motion = b; EditorUtility.SetDirty(heroState); return 1; }
            if (sourceMotion is BlendTree srcTree && heroState.motion is BlendTree heroTree) return ReplaceTree(heroTree, srcTree, baked);
            return 0;
        }

        static int ReplaceTree(BlendTree heroTree, BlendTree srcTree, Dictionary<AnimationClip, AnimationClip> baked)
        {
            int n = 0;
            var children = heroTree.children; var src = srcTree.children;
            for (int i = 0; i < children.Length && i < src.Length; i++)
            {
                if (src[i].motion is AnimationClip c && baked.TryGetValue(c, out var b)) { children[i].motion = b; n++; }
                else if (src[i].motion is BlendTree st && children[i].motion is BlendTree ht) n += ReplaceTree(ht, st, baked);
            }
            heroTree.children = children;
            EditorUtility.SetDirty(heroTree);
            return n;
        }

        // 손가락 근육은 원래 뼈대에 손가락이 없어 기록하지 않음 (클립 속성 이름도 HumanTrait 이름과 다름)
        static bool IsFingerMuscle(string name) =>
            name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little");

        /// <summary>
        /// 원래 모델을 게임에서처럼(ModelYaw만큼 돌려) 세웠을 때의 몸 방향과, 같은 근육 값을 히어로(원점, 회전 없음)에 줬을 때의
        /// 몸 방향 차이(도). 아바타마다 '정면' 기준이 달라 고정값 대신 실제로 세워 보고 계산함. 방향은 양쪽 허벅지 뼈로 판단.
        /// </summary>
        static float FacingCorrection(Avatar sourceAvatar)
        {
            var heroAvatar = AssetDatabase.LoadAllAssetsAtPath(HeroModelPath).OfType<Avatar>().FirstOrDefault();
            if (!heroAvatar || !heroAvatar.isHuman) { Debug.LogWarning("[NGH] 히어로 아바타를 찾지 못해 기본 보정값 사용"); return ModelYaw; }
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var holder = new GameObject("Player");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), holder.transform);
                model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
                var hero = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HeroModelPath));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hero, scene);

                var pose = new HumanPose();
                using (var src = new HumanPoseHandler(sourceAvatar, model.transform)) src.GetHumanPose(ref pose);
                using (var dst = new HumanPoseHandler(heroAvatar, hero.transform)) dst.SetHumanPose(ref pose);

                Vector3 Facing(GameObject root, string left, string right)
                {
                    var bones = root.GetComponentsInChildren<Transform>(true);
                    var l = bones.First(t => t.name == left); var r = bones.First(t => t.name == right);
                    return Vector3.Cross(Vector3.ProjectOnPlane(r.position - l.position, Vector3.up), Vector3.up);
                }
                Vector3 want = Facing(model, "LeftUpLeg", "RightUpLeg");
                Vector3 got = Facing(hero, "mixamorig:LeftUpLeg", "mixamorig:RightUpLeg");
                return Vector3.SignedAngle(got, want, Vector3.up);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        // 이 동작 동안 손에 든 칼만 칼날 축으로 180° 돌려 날과 칼등을 뒤집음 (손·팔은 그대로). 원본(제너릭) 클립 이름
        static readonly HashSet<string> FlipSwordClips = new HashSet<string> { "NGH_Anim_Combo5" };
        const float SwordFlipTime = 0.15f;   // 시작·끝에서 돌려 쥐는 시간(초)

        /// <summary>손에 든 칼 모델의 기본 자세와 뒤집는 축 (부모 = 무기 오브젝트 기준)</summary>
        sealed class SwordFlip
        {
            public string path; public Vector3 restPos, restScale, axis, pivot; public Quaternion restRot;
        }

        static SwordFlip LoadSwordFlip()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var hero = prefab ? prefab.transform.Find("Hero_Bound") : null;
            var visual = hero ? hero.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == SwordVisualName) : null;
            var mf = visual ? visual.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return null;
            var (grip, handle, _, _) = SwordGeometry(mf.sharedMesh);
            var f = new SwordFlip
            {
                path = AnimationUtility.CalculateTransformPath(visual, hero),
                restPos = visual.localPosition, restRot = visual.localRotation, restScale = visual.localScale,
            };
            f.axis = (f.restRot * Vector3.Scale(f.restScale, handle)).normalized;
            f.pivot = f.restPos + f.restRot * Vector3.Scale(f.restScale, grip);   // 쥐는 점을 지나는 손잡이 축으로 돌려 칼이 주먹에서 벗어나지 않게
            return f;
        }

        static AnimationClip BakeHumanoid(AnimationClip src, Avatar avatar, Quaternion yaw, SwordFlip flip)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model, scene);
                var handler = new HumanPoseHandler(avatar, model.transform);
                var pose = new HumanPose();

                int muscleCount = HumanTrait.MuscleCount;
                var muscles = new AnimationCurve[muscleCount];
                for (int i = 0; i < muscleCount; i++) if (!IsFingerMuscle(HumanTrait.MuscleName[i])) muscles[i] = new AnimationCurve();
                var rootT = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                var rootQ = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };

                int frames = Mathf.Max(1, Mathf.RoundToInt(src.length * Fps));
                Quaternion prevQ = Quaternion.identity;
                for (int f = 0; f <= frames; f++)
                {
                    float t = Mathf.Min(f / Fps, src.length);
                    src.SampleAnimation(model, t);
                    handler.GetHumanPose(ref pose);
                    // 게임 안에서 원래 모델이 돌려져 있던 만큼 몸통 방향·위치를 돌려, 히어로가 같은 방향을 보게 함
                    Vector3 p = yaw * pose.bodyPosition;
                    Quaternion q = yaw * pose.bodyRotation;
                    if (f > 0 && Quaternion.Dot(prevQ, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    prevQ = q;
                    rootT[0].AddKey(t, p.x); rootT[1].AddKey(t, p.y); rootT[2].AddKey(t, p.z);
                    rootQ[0].AddKey(t, q.x); rootQ[1].AddKey(t, q.y); rootQ[2].AddKey(t, q.z); rootQ[3].AddKey(t, q.w);
                    for (int i = 0; i < muscleCount; i++) muscles[i]?.AddKey(t, pose.muscles[i]);
                }
                handler.Dispose();

                var clip = new AnimationClip { name = HeroName(src.name), frameRate = Fps };
                void Set(string prop, AnimationCurve c) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), c);
                Set("RootT.x", rootT[0]); Set("RootT.y", rootT[1]); Set("RootT.z", rootT[2]);
                Set("RootQ.x", rootQ[0]); Set("RootQ.y", rootQ[1]); Set("RootQ.z", rootQ[2]); Set("RootQ.w", rootQ[3]);
                for (int i = 0; i < muscleCount; i++) if (muscles[i] != null) Set(HumanTrait.MuscleName[i], muscles[i]);
                // 원래 뼈대에는 손가락이 없어 오른손을 무기를 쥔 주먹으로 고정 (값: -1 = 다 굽힘, 1 = 다 폄)
                foreach (var (prop, value) in RightFist)
                    Set(prop, AnimationCurve.Constant(0f, Mathf.Max(src.length, 1f / Fps), value));

                if (flip != null)
                {
                    var pos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    var rot = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    for (int f = 0; f <= frames; f++)
                    {
                        float t = Mathf.Min(f / Fps, src.length);
                        float w = Mathf.SmoothStep(0f, 1f, t / SwordFlipTime) * Mathf.SmoothStep(0f, 1f, (src.length - t) / SwordFlipTime);
                        var q = Quaternion.AngleAxis(180f * w, flip.axis);
                        Vector3 p = flip.pivot + q * (flip.restPos - flip.pivot);
                        Quaternion r = q * flip.restRot;
                        pos[0].AddKey(t, p.x); pos[1].AddKey(t, p.y); pos[2].AddKey(t, p.z);
                        rot[0].AddKey(t, r.x); rot[1].AddKey(t, r.y); rot[2].AddKey(t, r.z); rot[3].AddKey(t, r.w);
                    }
                    void SetT(string prop, AnimationCurve c) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(flip.path, typeof(Transform), prop), c);
                    SetT("m_LocalPosition.x", pos[0]); SetT("m_LocalPosition.y", pos[1]); SetT("m_LocalPosition.z", pos[2]);
                    SetT("m_LocalRotation.x", rot[0]); SetT("m_LocalRotation.y", rot[1]); SetT("m_LocalRotation.z", rot[2]); SetT("m_LocalRotation.w", rot[3]);
                }

                // 실제 이동은 컨트롤러가 담당 → 몸통 움직임은 모두 자세에 포함 (Bake Into Pose)
                var srcSettings = AnimationUtility.GetAnimationClipSettings(src);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = srcSettings.loopTime;
                settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
                settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AnimationUtility.SetAnimationEvents(clip, AnimationUtility.GetAnimationEvents(src));

                string path = HeroClipDir + clip.name + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing) { EditorUtility.CopySerialized(clip, existing); clip = existing; }
                else AssetDatabase.CreateAsset(clip, path);
                EditorUtility.SetDirty(clip);
                if (!clip.humanMotion) Debug.LogWarning("[NGH] 휴머노이드 클립으로 인식되지 않음: " + path);
                return clip;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static readonly (string prop, float value)[] RightFist =
        {
            ("RightHand.Thumb.1 Stretched", -0.2f), ("RightHand.Thumb.Spread", -0.3f), ("RightHand.Thumb.2 Stretched", -0.5f), ("RightHand.Thumb.3 Stretched", -0.5f),
            ("RightHand.Index.1 Stretched", -0.75f), ("RightHand.Index.Spread", 0f), ("RightHand.Index.2 Stretched", -0.9f), ("RightHand.Index.3 Stretched", -0.8f),
            ("RightHand.Middle.1 Stretched", -0.8f), ("RightHand.Middle.Spread", 0f), ("RightHand.Middle.2 Stretched", -0.9f), ("RightHand.Middle.3 Stretched", -0.8f),
            ("RightHand.Ring.1 Stretched", -0.8f), ("RightHand.Ring.Spread", 0f), ("RightHand.Ring.2 Stretched", -0.9f), ("RightHand.Ring.3 Stretched", -0.8f),
            ("RightHand.Little.1 Stretched", -0.8f), ("RightHand.Little.Spread", 0f), ("RightHand.Little.2 Stretched", -0.9f), ("RightHand.Little.3 Stretched", -0.8f),
        };

        static string HeroName(string src) => src.StartsWith("NGH_Anim_") ? "NGH_Hero_" + src.Substring("NGH_Anim_".Length) : "NGH_Hero_" + src;

        // ================================================================ 2. 프리팹 모델 교체

        [MenuItem("Tools/NGH/Hero/2. Swap NGH_Player Model To Hero_Bound")]
        public static void SwapModel()
        {
            var heroController = AssetDatabase.LoadAssetAtPath<AnimatorController>(HeroControllerPath);
            if (!heroController) { Debug.LogError("[NGH] 먼저 1번 메뉴(Build Hero Clips & Animator)를 실행하세요."); return; }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            var yph = PrefabUtility.LoadPrefabContents(YphPrefabPath);
            var log = new System.Text.StringBuilder("[NGH] NGH_Player 모델 교체:");
            try
            {
                var oldModel = root.transform.Find(OldModelName);
                if (!oldModel) { Debug.LogError("[NGH] NGH_Player 안에 " + OldModelName + "가 없습니다. 이미 교체했나요?"); return; }
                var yphHero = yph.transform.Find(YphHeroPath);
                if (!yphHero) { Debug.LogError("[NGH] YPH 프리팹에서 " + YphHeroPath + "를 찾지 못했습니다."); return; }

                // 히어로 복사 (무기·칼집·옷 충돌체 포함, 위치는 YPH와 같게)
                var hero = (GameObject)Object.Instantiate(yphHero.gameObject, root.transform);
                hero.name = "Hero_Bound";
                hero.transform.localPosition = yphHero.localPosition;
                hero.transform.localRotation = yphHero.localRotation;
                hero.transform.localScale = yphHero.localScale;
                foreach (var r in hero.GetComponents<YPH.HeroPoseRetarget>()) Object.DestroyImmediate(r);   // 실시간 자세 전달은 필요 없음

                // 캐릭터 크기 (YPH가 히어로에 맞춘 값)
                var cc = root.GetComponent<CharacterController>(); var ycc = yph.transform.Find("NGH_Player")?.GetComponent<CharacterController>();
                if (cc && ycc) { cc.height = ycc.height; cc.center = ycc.center; cc.radius = ycc.radius; log.Append($" 컨트롤러 높이 {cc.height};"); }

                // 애니메이터
                var oldAnim = oldModel.GetComponent<Animator>();
                var anim = hero.GetComponent<Animator>();
                anim.runtimeAnimatorController = heroController;
                if (oldAnim) { anim.applyRootMotion = oldAnim.applyRootMotion; anim.cullingMode = oldAnim.cullingMode; anim.updateMode = oldAnim.updateMode; }

                // 원래 모델에만 있던 컴포넌트를 히어로로 (예: HpTint)
                foreach (var c in oldModel.GetComponents<MonoBehaviour>())
                {
                    if (!c || hero.GetComponent(c.GetType())) continue;
                    UnityEditorInternal.ComponentUtility.CopyComponent(c);
                    UnityEditorInternal.ComponentUtility.PasteComponentAsNew(hero);
                    log.Append($" {c.GetType().Name} 옮김;");
                }

                // 원래 오브젝트 → 히어로 오브젝트 대응
                var map = new Dictionary<GameObject, GameObject>();
                var heroByName = new Dictionary<string, GameObject>();
                foreach (var t in hero.GetComponentsInChildren<Transform>(true)) if (!heroByName.ContainsKey(t.name)) heroByName[t.name] = t.gameObject;
                foreach (var t in oldModel.GetComponentsInChildren<Transform>(true))
                {
                    GameObject to = null;
                    if (t == oldModel) to = hero;
                    else if (heroByName.TryGetValue(t.name, out var same)) to = same;                     // 무기 (chg_sword 등)
                    else if (heroByName.TryGetValue("mixamorig:" + t.name, out var bone)) to = bone;      // 뼈 (Hips → mixamorig:Hips)
                    map[t.gameObject] = to;
                }

                // 무기 수치는 NGH 값을 유지
                foreach (var w in oldModel.GetComponentsInChildren<Weapon>(true))
                {
                    var to = map.TryGetValue(w.gameObject, out var go) && go ? go.GetComponent<Weapon>() : null;
                    if (to) { CopyFields(w, to); log.Append($" 무기 {w.weaponName} 수치 유지;"); }
                    else log.Append($" [무기 {w.name} 대응 없음];");
                }

                // NGH_Player 안의 참조를 히어로 쪽으로
                var unresolved = new List<string>();
                int remapped = 0;
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                {
                    if (!comp || comp.transform.IsChildOf(oldModel) || comp.transform.IsChildOf(hero.transform)) continue;
                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || !it.objectReferenceValue) continue;
                        var v = it.objectReferenceValue;
                        var vgo = v is GameObject g ? g : v is Component vc ? vc.gameObject : null;
                        if (!vgo || !vgo.transform.IsChildOf(oldModel)) continue;
                        Object to = null;
                        if (map.TryGetValue(vgo, out var ngo) && ngo)
                        {
                            if (v is GameObject) to = ngo;
                            else if (v is Animator) to = anim;
                            else to = FindComponent(ngo, v);
                        }
                        if (to) { it.objectReferenceValue = to; changed = true; remapped++; }
                        else unresolved.Add($"{comp.GetType().Name}.{it.propertyPath} → {v.name} ({v.GetType().Name})");
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
                log.Append($" 참조 {remapped}개 옮김;");

                Object.DestroyImmediate(oldModel.gameObject);
                FixUp(hero, log);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log(log.ToString());
                if (unresolved.Count > 0) Debug.LogWarning("[NGH] 히어로에서 대응을 찾지 못한 참조 (확인 필요):\n" + string.Join("\n", unresolved));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(yph);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/NGH/Hero/3. Fix Hero Setup (Remove YPH Gun Proxy, Sheath Sword)")]
        public static void FixHeroSetup()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var hero = root.transform.Find("Hero_Bound");
                if (!hero) { Debug.LogError("[NGH] NGH_Player 안에 Hero_Bound가 없습니다. 2번 메뉴를 먼저 실행하세요."); return; }
                var log = new System.Text.StringBuilder("[NGH] 히어로 정리:");
                FixUp(hero.gameObject, log);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log(log.ToString());
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        const string GripPoseClip = HeroClipDir + "NGH_Hero_Idle.anim";
        const string SwordVisualName = "HeroSword_DrawVisual";

        /// <summary>
        /// 손에 든 칼 모델(HeroSword_DrawVisual)을 오른손 주먹에 맞춤. 칼은 손 뼈의 자식이라 손 기준 위치만 맞추면 모든 동작에서 맞음.
        ///  - 쥐는 점: 대기 자세(주먹)에서 검지~새끼 첫째·둘째 마디의 평균 / 칼날 방향: 새끼 → 검지 (칼이 엄지 쪽으로 나옴)
        ///  - 칼 모델: 가장 긴 축 = 칼날, 손잡이 쪽 끝에서 가장 넓은 곳 = 날밑, 날밑에서 손잡이 쪽으로 손잡이 길이의 30% = 쥐는 곳
        /// </summary>
        [MenuItem("Tools/NGH/Hero/4. Align Sword To Right Hand Grip")]
        public static void AlignSwordToGrip()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var hero = root.transform.Find("Hero_Bound");
                var anim = hero ? hero.GetComponent<Animator>() : null;
                var visual = hero ? hero.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == SwordVisualName) : null;
                var mf = visual ? visual.GetComponent<MeshFilter>() : null;
                var pose = AssetDatabase.LoadAssetAtPath<AnimationClip>(GripPoseClip);
                if (!anim || !mf || !mf.sharedMesh || !pose) { Debug.LogError($"[NGH] 칼 맞추기 준비 실패 (히어로 {(anim ? "O" : "X")}, 칼 모델 {(mf ? "O" : "X")}, 대기 클립 {(pose ? "O" : "X")})"); return; }

                // 계산용으로 주먹 자세를 재생 → 끝나면 뼈를 원래대로 되돌려 프리팹에 자세가 저장되지 않게 함
                var rest = hero.GetComponentsInChildren<Transform>(true).Where(t => t != visual)
                               .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
                pose.SampleAnimation(hero.gameObject, 0f);
                var fingers1 = new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal };
                var fingers2 = new[] { HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightLittleIntermediate };
                var fingers3 = new[] { HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleDistal };
                var b1 = fingers1.Select(anim.GetBoneTransform).ToArray(); var b2 = fingers2.Select(anim.GetBoneTransform).ToArray(); var b3 = fingers3.Select(anim.GetBoneTransform).ToArray();
                if (b1.Concat(b2).Concat(b3).Any(b => !b)) { Debug.LogError("[NGH] 히어로 오른손 손가락 뼈를 찾지 못했습니다."); return; }
                // 손가락은 손잡이를 감싸므로 각 손가락의 세 마디를 지나는 원의 중심을 손잡이가 지남
                var centers = Enumerable.Range(0, 4).Select(i => Circumcenter(b1[i].position, b2[i].position, b3[i].position)).ToArray();
                Vector3 grip = centers.Aggregate(Vector3.zero, (s, c) => s + c) / 4f;
                Vector3 bladeDir = (centers[0] - centers[3]).normalized;   // 새끼 → 검지: 칼이 엄지 쪽으로 나옴
                // 날은 손등(첫째 마디) 반대쪽 = 손가락 끝이 감겨 들어가는 쪽
                Vector3 knuckles = b1.Aggregate(Vector3.zero, (s, b) => s + b.position) / 4f;
                Vector3 edgeDir = Vector3.ProjectOnPlane(grip - knuckles, bladeDir).normalized;

                var (gripLocal, handleLocal, edgeLocal, handleLow) = SwordGeometry(mf.sharedMesh);

                Vector3 before = visual.TransformPoint(gripLocal);
                Vector3 axisBefore = visual.TransformDirection(handleLocal).normalized;
                // 손잡이 축 → 쥐는 축, 날 → 손가락이 감기는 쪽 (칼 모델 크기는 균일해서 회전만으로 방향이 맞음)
                visual.rotation = Quaternion.LookRotation(bladeDir, edgeDir) * Quaternion.Inverse(Quaternion.LookRotation(handleLocal, edgeLocal));
                visual.position += grip - visual.TransformPoint(gripLocal);
                Vector3 axisWorld = axisBefore;
                foreach (var (t, p, r, s) in rest) { t.localPosition = p; t.localRotation = r; t.localScale = s; }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[NGH] 칼 맞춤: 쥐는 점 이동 {(grip - before).magnitude * 100f:0.0}cm, 칼날 방향 {Vector3.Angle(axisWorld, bladeDir):0}° 회전 (손잡이 쪽 = 칼 모델 {(handleLow ? "-" : "+")}축)");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>
        /// 칼 모델 메시 분석 (메시 기준 좌표): 쥐는 점, 손잡이 축(칼끝 쪽 방향), 날 방향.
        ///  가장 긴 축 = 칼 / 손잡이 쪽 끝에서 가장 넓은 곳 = 날밑 / 날밑에서 손잡이 길이의 30% = 쥐는 점
        /// </summary>
        static (Vector3 grip, Vector3 handle, Vector3 edge, bool handleLow) SwordGeometry(Mesh mesh)
        {
            var verts = mesh.vertices;
            Vector3 center = verts.Aggregate(Vector3.zero, (s, v) => s + v) / verts.Length;
            Vector3 axis = PrincipalAxis(verts, center);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in verts) { float d = Vector3.Dot(v - center, axis); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
            float Width(float from, float to) { float w = 0f; foreach (var v in verts) { float d = Vector3.Dot(v - center, axis); if (d >= from && d <= to) w = Mathf.Max(w, Vector3.ProjectOnPlane(v - center, axis).magnitude); } return w; }
            float len = max - min, bin = len / 60f;
            float bestLow = min, bestLowW = 0f, bestHigh = max, bestHighW = 0f;
            for (float s = min; s < min + len * 0.4f; s += bin) { float w = Width(s, s + bin); if (w > bestLowW) { bestLowW = w; bestLow = s + bin * 0.5f; } }
            for (float s = max - len * 0.4f; s < max; s += bin) { float w = Width(s, s + bin); if (w > bestHighW) { bestHighW = w; bestHigh = s + bin * 0.5f; } }
            bool handleLow = bestLowW >= bestHighW;   // 날밑(넓은 곳)이 있는 쪽이 손잡이 쪽
            float guard = handleLow ? bestLow : bestHigh, pommel = handleLow ? min : max, tipEnd = handleLow ? max : min;

            // 구간별 단면 중심 (휜 칼날·손잡이를 따라가는 중심선)
            Vector3 SliceCenter(float at, float half)
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var v in verts) { float d = Vector3.Dot(v - center, axis); if (Mathf.Abs(d - at) <= half) { sum += v; n++; } }
                return n > 0 ? sum / n : center + axis * at;
            }
            float handleLen = Mathf.Abs(guard - pommel), bladeLen = Mathf.Abs(tipEnd - guard);
            Vector3 pommelC = SliceCenter(Mathf.Lerp(pommel, guard, 0.1f), handleLen * 0.08f);
            Vector3 guardC = SliceCenter(Mathf.Lerp(pommel, guard, 0.85f), handleLen * 0.08f);
            Vector3 handle = (guardC - pommelC).normalized;
            Vector3 grip = Vector3.Lerp(guardC, pommelC, 0.3f);
            Vector3 bladeBase = SliceCenter(Mathf.Lerp(guard, tipEnd, 0.08f), bladeLen * 0.03f);
            Vector3 bladeTip = SliceCenter(Mathf.Lerp(guard, tipEnd, 0.92f), bladeLen * 0.03f);
            Vector3 bladeMid = SliceCenter(Mathf.Lerp(guard, tipEnd, 0.5f), bladeLen * 0.03f);
            Vector3 chord = (bladeTip - bladeBase).normalized;
            // 휜 쪽의 반대가 날 (이 칼 모델로 확인: 볼록한 쪽으로 맞추면 칼끝이 아래로 휘어 날이 손등 쪽을 향했음)
            Vector3 edge = -Vector3.ProjectOnPlane(bladeMid - (bladeBase + chord * Vector3.Dot(bladeMid - bladeBase, chord)), handle).normalized;
            return (grip, handle, edge, handleLow);
        }

        static Vector3 Circumcenter(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, n = Vector3.Cross(ab, ac);
            if (n.sqrMagnitude < 1e-10f) return (a + b + c) / 3f;   // 손가락이 곧게 펴져 있으면 원을 못 그림
            return a + (Vector3.Cross(n, ab) * ac.sqrMagnitude + Vector3.Cross(ac, n) * ab.sqrMagnitude) / (2f * n.sqrMagnitude);
        }

        static Vector3 PrincipalAxis(Vector3[] pts, Vector3 c)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts) { var d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            Vector3 v = new Vector3(1f, 0.7f, 0.3f);
            for (int i = 0; i < 50; i++)
            {
                v = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z).normalized;
            }
            return v;
        }

        // YPH 플레이어 전용 장치 제거 + 칼집 속 칼 표시 (YPH에서는 HeroPoseRetarget가 하던 일)
        static void FixUp(GameObject hero, System.Text.StringBuilder log)
        {
            // YPH 증기총: YPH 플레이어의 증기 탱크·조준 시스템과 연결돼야 동작. NGH 사격은 chg_TestGun이 담당
            foreach (var proxy in hero.GetComponentsInChildren<Transform>(true).Where(b => b && b.name == "YPH_SteamGunProxy").ToArray())
            {
                Object.DestroyImmediate(proxy.gameObject);
                log.Append(" YPH_SteamGunProxy 제거;");
            }

            var bones = hero.GetComponentsInChildren<Transform>(true);
            var drawn = bones.FirstOrDefault(t => t.name == "chg_sword" && t.GetComponent<Weapon>());
            var socket = bones.FirstOrDefault(t => t.name == "Socket_SwordSheathed");
            var sheathed = socket ? socket.GetComponentsInChildren<Renderer>(true).FirstOrDefault() : null;
            if (drawn && sheathed)
            {
                var vis = hero.GetComponent<SheathSwordVisual>();
                if (!vis) vis = hero.AddComponent<SheathSwordVisual>();
                vis.drawnSword = drawn.gameObject;
                vis.sheathedSword = sheathed;
                log.Append($" 칼집 속 칼 표시 연결({sheathed.name});");
            }
            else log.Append($" [칼집 속 칼을 찾지 못함: 손 칼 {(drawn ? "O" : "X")}, 칼집 칼 {(sheathed ? "O" : "X")}];");

            // 저고리 Cloth 끔: 휴머노이드 애니메이션(팔을 몸에 붙인 자세)에서 Cloth의 기준 위치가 스키닝과 최대 10cm 어긋나
            // 허리 앞에 검게 늘어난 천 띠가 생김 (YPH 원본 모델에 같은 애니메이션을 입혀도 동일). 치마 Cloth는 정상이라 유지
            foreach (var cloth in hero.GetComponentsInChildren<Cloth>(true).Where(c => c.name == "OutterJeogori" && c.enabled))
            {
                cloth.enabled = false;
                log.Append(" 저고리 Cloth 끔;");
            }
        }

        // 같은 종류 컴포넌트 → 자식에서 찾기 → (렌더러면) 아무 렌더러. 에디터의 GetComponent는 '가짜 null'을 돌려줄 수 있어 bool로 검사
        static Component FindComponent(GameObject go, Object original)
        {
            var type = original.GetType();
            var c = go.GetComponent(type);
            if (c) return c;
            c = go.GetComponentInChildren(type, true);
            if (c) return c;
            if (original is Renderer) { var r = go.GetComponentInChildren<Renderer>(true); if (r) return r; }
            return null;
        }

        static void CopyFields(Component from, Component to)
        {
            var src = new SerializedObject(from); var dst = new SerializedObject(to);
            var it = src.GetIterator();
            if (!it.NextVisible(true)) return;
            do { if (it.name != "m_Script") dst.CopyFromSerializedProperty(it); } while (it.NextVisible(false));
            dst.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
