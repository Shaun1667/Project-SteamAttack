#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 메뉴: CHG > Build Prototype Scene (캐릭터: Arts/Models/CHG/chg_player.fbx, 동작: Animations/CHG/chg_anim_*.fbx)
    ///  - chg_PlayerAnimator.controller 생성 (Locomotion 블렌드트리 + 구르기/약공격/강공격/사격)
    ///  - chg_Prototype.unity 생성: 플레이어(환도/테스트 둔기/테스트 총), 3인칭 카메라, 허수아비 3개
    /// </summary>
    [InitializeOnLoad]
    public static class PrototypeSetup
    {
        // 씬 자동 생성은 꺼 두었음 (팀원 컴퓨터에서 실행되지 않도록). 메뉴 CHG > Build Prototype Scene 으로만 실행.
        // 아래는 프리팹 저장을 한 번만 자동 실행 — 이 씬을 만들어 본 적 있는 컴퓨터(작성자)에서만 동작
        static PrototypeSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorPrefs.GetInt("chg_ProtoBuiltVersion", 0) <= 0) return;      // 팀원 컴퓨터: 아무것도 안 함
                if (!File.Exists(ScenePath)) return;
                if (!EditorPrefs.GetBool("chg_PrefabsSaved_v1", false))
                {
                    EditorPrefs.SetBool("chg_PrefabsSaved_v1", true);
                    SavePrefabsAndTest();
                }
                if (!EditorPrefs.GetBool("chg_Migrated_v19", false) && File.Exists(PrefabDir + "NGH_Player.prefab"))
                {
                    EditorPrefs.SetBool("chg_Migrated_v19", true);
                    MigrateHpUpdate();
                }
                if (!EditorPrefs.GetBool("chg_Migrated_v20", false) && File.Exists(PrefabDir + "NGH_Player.prefab"))
                {
                    EditorPrefs.SetBool("chg_Migrated_v20", true);
                    MigrateDeathStandup();
                }
                if (!EditorPrefs.GetBool("chg_Migrated_v21", false) && File.Exists(PrefabDir + "NGH_Player.prefab"))
                {
                    EditorPrefs.SetBool("chg_Migrated_v21", true);
                    ApplyStandupTuning();
                }
                if (!EditorPrefs.GetBool("chg_Migrated_v23", false) && File.Exists(PrefabDir + "NGH_Player.prefab"))
                {
                    // 피드백 반영: 공격 판정 범위 1.3배, 납도 상태에서 공격하면 발도 동작 먼저
                    EditorPrefs.SetBool("chg_Migrated_v23", true);
                    string pp23 = PrefabDir + "NGH_Player.prefab";
                    var r23 = PrefabUtility.LoadPrefabContents(pp23);
                    var log = new System.Text.StringBuilder("[CHG][검증] 공격 판정 1.3배:");
                    foreach (var w in r23.GetComponentsInChildren<Weapon>(true))
                    {
                        if (!w.isMelee) continue;
                        float r0 = w.reach, h0 = w.hitRadius;
                        w.reach = r0 * 1.3f; w.hitRadius = h0 * 1.3f;
                        log.Append($" {w.weaponName} 거리 {r0:0.##}→{w.reach:0.##}, 반경 {h0:0.##}→{w.hitRadius:0.##};");
                    }
                    var pc23 = r23.GetComponent<PlayerController>();
                    if (pc23) pc23.instantDrawOnAttack = false;
                    PrefabUtility.SaveAsPrefabAsset(r23, pp23);
                    PrefabUtility.UnloadPrefabContents(r23);
                    var chk23 = AssetDatabase.LoadAssetAtPath<GameObject>(pp23).GetComponent<PlayerController>();
                    Debug.Log(log.ToString() + " / 납도 공격 시 발도 먼저: " + (chk23 && !chk23.instantDrawOnAttack ? "OK" : "실패"));
                }
                if (!EditorPrefs.GetBool("chg_Migrated_v22", false) && File.Exists(PrefabDir + "NGH_Player.prefab"))
                {
                    EditorPrefs.SetBool("chg_Migrated_v22", true);
                    string pp = PrefabDir + "NGH_Player.prefab";
                    var root = PrefabUtility.LoadPrefabContents(pp);
                    var pc = root.GetComponent<PlayerController>();
                    if (pc) pc.lightCombo2.hitOncePerAction = false;
                    PrefabUtility.SaveAsPrefabAsset(root, pp);
                    PrefabUtility.UnloadPrefabContents(root);
                    var chk = AssetDatabase.LoadAssetAtPath<GameObject>(pp).GetComponent<PlayerController>();
                    Debug.Log("[CHG][검증] 약공격 2타 판정 2번: " + (!chk.lightCombo2.hitOncePerAction ? "OK" : "실패"));
                }
            };
        }

        const string AnimDir = "Assets/Animations/CHG";
        // Blender에서 다시 만든 파일: 칼이 오른손 뼈에 붙어 있는 캐릭터 + 동작별 애니메이션
        const string CharPath = "Assets/Arts/Models/CHG/chg_player.fbx";
        const string AnimPrefix = "chg_anim_";
        const int BuildVersion = 18;
        const float WalkTimeScale = 1.6f;   // 걷기 동작 재생 속도 (Locomotion 블렌드트리)
        const string TexDir = "Assets/Arts/Textures/CHG/";
        const string MatDir = "Assets/Materials/CHG/";
        const string ControllerPath = AnimDir + "/chg_PlayerAnimator.controller";
        const string ScenePath = "Assets/Scenes/CHG/chg_Prototype.unity";
        static readonly string[] LoopClips = { "standby", "walk", "run", "fastrun", "leftwalk", "rightwalk" };


        [MenuItem("CHG/Build Prototype Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int prevVersion = EditorPrefs.GetInt("chg_ProtoBuiltVersion", 0);

            // ---------- 기존 씬에서 Inspector로 조절한 값 보존 (다시 만들어도 덮어쓰지 않도록)
            string savedPlayer = null, savedCamera = null, savedGround = null;
            var savedWeapons = new Dictionary<string, string>();
            float savedWalkScale = ReadWalkTimeScale();
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var oldPc = Object.FindFirstObjectByType<PlayerController>();
                if (oldPc) savedPlayer = EditorJsonUtility.ToJson(oldPc);
                var oldCam = Object.FindFirstObjectByType<ThirdPersonCamera>();
                if (oldCam) savedCamera = EditorJsonUtility.ToJson(oldCam);
                var oldGc = Object.FindFirstObjectByType<GroundClamp>();
                if (oldGc) savedGround = EditorJsonUtility.ToJson(oldGc);
                foreach (var w in Object.FindObjectsByType<Weapon>(FindObjectsInactive.Include))
                    savedWeapons[w.gameObject.name] = EditorJsonUtility.ToJson(w);
            }

            PrepareCharacter();
            SetLoops();
            var clips = LoadClips();
            var controller = BuildController(clips, savedWalkScale > 0f ? savedWalkScale : WalkTimeScale);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // ---------- 바닥
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "chg_Ground";
            ground.transform.localScale = Vector3.one * 5f;   // 50m x 50m
            var groundMat = MakeGroundMaterial();
            if (groundMat) ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            // ---------- 플레이어
            var player = new GameObject("NGH_Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.0f; cc.radius = 0.2f; cc.center = new Vector3(0, 0.5f, 0);
            cc.stepOffset = 0.2f; cc.skinWidth = 0.02f;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharPath));
            model.name = "chg_Model";
            model.transform.SetParent(player.transform, false);
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var groundClamp = model.AddComponent<GroundClamp>();   // 어떤 동작이든 땅 아래로 내려가지 않게
            if (savedGround != null) { EditorJsonUtility.FromJsonOverwrite(savedGround, groundClamp); groundClamp.groundReference = null; groundClamp.boneRoot = null; }

            // 모델 정면을 플레이어 정면(+Z)에 맞춤: 발 → 발끝 방향을 정면으로 봄
            var bones = model.GetComponentsInChildren<Transform>(true);
            var foot = bones.FirstOrDefault(t => t.name.EndsWith("LeftFoot"));
            var toe = bones.FirstOrDefault(t => t.name.EndsWith("LeftToeBase"));
            if (foot && toe)
            {
                Vector3 f = toe.position - foot.position; f.y = 0f;
                if (f.sqrMagnitude > 1e-6f)
                {
                    float yaw = Vector3.SignedAngle(f, Vector3.forward, Vector3.up);
                    model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
                    Debug.Log($"[CHG] 모델 정면 보정: {yaw:0.#}도");
                }
            }
            else Debug.LogWarning("[CHG] 발 뼈를 찾지 못해 정면 보정을 건너뜁니다.");

            var hand = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith("RightHand"));
            if (hand == null) Debug.LogWarning("[CHG] RightHand 뼈를 찾지 못했습니다.");

            // ---------- 무기 3종 (환도는 모델 안에서 이미 오른손 뼈에 붙어 있음)
            var holder = player.AddComponent<WeaponHolder>();
            var swordT = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "chg_sword");
            if (swordT == null) { Debug.LogError("[CHG] 모델 안에서 chg_sword를 찾지 못했습니다."); return; }
            var sword = swordT.gameObject;
            AddWeapon(sword, "환도", true, 1f, 3f, 1.17f, 0.78f);

            // 손잡이 쥐는 점: 칼 손잡이 끝에서 약 20% 지점 (T-포즈에서 칼끝은 +Z)
            var sb = GetBounds(swordT);
            Vector3 grip = new Vector3(sb.center.x, sb.center.y, sb.min.z + sb.size.z * 0.206f);

            var club = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            club.name = "chg_TestClub";
            Object.DestroyImmediate(club.GetComponent<Collider>());
            club.transform.localScale = new Vector3(0.035f, 0.22f, 0.035f);
            club.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            club.transform.position = grip + Vector3.forward * 0.16f;
            if (hand) club.transform.SetParent(hand, true);
            Tint(club, new Color(0.45f, 0.3f, 0.15f));
            AddWeapon(club, "테스트 둔기", true, 1f, 3f, 0.91f, 0.65f);

            var gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "chg_TestGun";
            Object.DestroyImmediate(gun.GetComponent<Collider>());
            gun.transform.localScale = new Vector3(0.04f, 0.07f, 0.22f);
            gun.transform.position = grip + Vector3.forward * 0.08f;
            if (hand) gun.transform.SetParent(hand, true);
            Tint(gun, new Color(0.15f, 0.15f, 0.18f));
            AddWeapon(gun, "테스트 원거리(락온 불가 확인용)", false, 0f, 0f, 0f, 0f);

            holder.slots = new List<Weapon>
            {
                sword.GetComponent<Weapon>(), club.GetComponent<Weapon>(), gun.GetComponent<Weapon>()
            };
            foreach (var w in holder.slots)
                if (w && savedWeapons.TryGetValue(w.gameObject.name, out var wj)) EditorJsonUtility.FromJsonOverwrite(wj, w);

            // ---------- 컨트롤러
            var pc = player.AddComponent<PlayerController>();
            if (savedPlayer != null) EditorJsonUtility.FromJsonOverwrite(savedPlayer, pc);   // 조절해 둔 값 복원
            if (prevVersion < 9)
            {
                // 요청받은 값 (한 번만 적용, 이후에는 Inspector 값이 우선)
                pc.backpedalSpeed = 1.5f;
                pc.rollDistance = 3f;
                pc.rollTime = 1.5f;
                pc.lightAttack.playSpeed = 2f;
                pc.heavyAttack.playSpeed = 2f;
                pc.sheathHideAt = 0.75f;   // 새 납도 동작: 칼이 칼집에 들어가는 지점
            }
            if (prevVersion < 17)
            {
                // 옆걸음 이동 1.1배 (0.7 → 0.77), 뒷걸음 애니메이션 0.9배 (0.8 → 0.72), 한 번만
                pc.strafeSpeed = 0.77f;
                pc.backpedalAnimSpeed = 0.72f;
            }
            if (prevVersion < 16)
            {
                // 뒷걸음: 애니메이션 0.8배 (1 → 0.8), 이동 속도 1.2배 (1.2 → 1.44), 한 번만
                pc.backpedalSpeed = 1.44f;
            }
            if (prevVersion < 15) pc.lightCombo2.lungeDistance = 0.15f;   // 약공격 2타 전진 거리 절반 (베기당 0.3 → 0.15), 한 번만
            if (prevVersion < 12)
            {
                // 파워슬래시 판정을 실제로 내려치는 순간(칼끝이 앞쪽 아래로 가장 빠르게 움직이는 프레임 52~58)에 맞춤
                pc.heavyAttack.hitWindows = new[] { new Vector2(0.345f, 0.39f) };
            }
            if (prevVersion < 11)
            {
                // 강공격을 파워슬래시로 교체 → 판정/후딜레이/전진 값 갱신, 약공격 후딜레이 살짝 줄임 (한 번만)
                pc.heavyAttack.endAt = 0.57f;
                pc.heavyAttack.hitWindows = new[] { new Vector2(0.345f, 0.39f) };
                pc.heavyAttack.lungeDistance = 0.5f;
                pc.lightAttack.endAt = Mathf.Max(0.28f, pc.lightAttack.endAt - 0.03f);
            }
            if (prevVersion < 10)
            {
                // 직접 확인하고 맞춘 구르기 값 (한 번만 적용, 이후에는 Inspector 값 유지)
                pc.rollTime = 0.9f;
                pc.rollAnimSpeed = 1.6f;
            }
            pc.animator = anim;
            pc.weapons = holder;
            pc.cameraTransform = null;
            if (clips.TryGetValue("swordslash", out var c1)) pc.lightAttack.clipLength = c1.length;
            if (clips.TryGetValue("powerslash", out var c2)) pc.heavyAttack.clipLength = c2.length;
            if (clips.TryGetValue("doubleslash", out var c9)) pc.lightCombo2.clipLength = c9.length;
            if (clips.TryGetValue("shot", out var c3)) pc.shotAttack.clipLength = c3.length;
            if (clips.TryGetValue("drawsword", out var c5)) pc.drawAction.clipLength = c5.length;
            if (clips.TryGetValue("sheathsword", out var c6)) pc.sheathAction.clipLength = c6.length;
            if (clips.TryGetValue("hit", out var c7)) pc.hitSmallAction.clipLength = c7.length;
            if (clips.TryGetValue("hit2", out var c8)) pc.hitLargeAction.clipLength = c8.length;
            ConfigurePlayerExtras(player, clips);
            // 구르기 Roll Time / Roll Anim Speed는 Inspector에서 직접 맞춘 값을 그대로 사용 (자동 계산 안 함)

            // ---------- 카메라
            var cam = Camera.main;
            if (cam)
            {
                pc.cameraTransform = cam.transform;
                var tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
                if (savedCamera != null) EditorJsonUtility.FromJsonOverwrite(savedCamera, tpc);
                tpc.target = player.transform;
                tpc.player = pc;
                cam.transform.position = new Vector3(0.25f, 1.3f, -2.4f);
                cam.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            }

            // ---------- ESC 조작키 안내
            var ui = new GameObject("NGH_ControlsGuide");
            var overlay = ui.AddComponent<ControlsOverlay>();
            overlay.guide = PrepareGuideTexture();

            // ---------- 허수아비
            MakeDummy("NGH_Dummy_1", new Vector3(0f, 0f, 4f));
            MakeDummy("NGH_Dummy_2", new Vector3(3f, 0f, 6f));
            MakeDummy("NGH_Dummy_3", new Vector3(-3.5f, 0f, 5f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorPrefs.SetInt("chg_ProtoBuiltVersion", BuildVersion);
            AssetDatabase.Refresh();
            Selection.activeGameObject = player;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(ScenePath));
            Debug.Log("[CHG] 프로토타입 씬 생성 완료: " + ScenePath + " — Play를 눌러 테스트하세요.");
        }

        // ================================================================ 캐릭터 임포트 준비 (재질/텍스처)

        static void PrepareCharacter()
        {
            SetNormalMap(TexDir + "chg_body_normal.png");
            SetNormalMap(TexDir + "chg_sword_normal.png");
            var bodyMat = MakeMaterial("chg_mat_body", "chg_body");
            var swordMat = MakeMaterial("chg_mat_sword", "chg_sword");

            var imp = AssetImporter.GetAtPath(CharPath) as ModelImporter;
            if (imp == null) { Debug.LogError("[CHG] 캐릭터 임포터를 찾지 못함: " + CharPath); return; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.importAnimation = false;
            // Blender FBX는 최상위 Armature에 축 보정 회전이 들어 있음.
            // 계층을 그대로 유지해야 애니메이션 재생 시 이 회전이 사라져 눕는 문제가 없음
            imp.preserveHierarchy = true;
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "chg_mat_body"), bodyMat);
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "chg_mat_sword"), swordMat);
            imp.SaveAndReimport();
        }

        // 바닥 격자: 텍스처 한 장 = 5m x 5m (1m 가는 선, 5m 굵은 선). 50m 바닥에 10번 반복
        static Material MakeGroundMaterial()
        {
            string texPath = TexDir + "chg_ground_grid.png";
            var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti != null && (ti.wrapMode != TextureWrapMode.Repeat || ti.anisoLevel < 8))
            {
                ti.textureType = TextureImporterType.Default;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;   // 비스듬히 봐도 선이 뭉개지지 않게
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Debug.LogWarning("[CHG] 바닥 격자 텍스처 없음: " + texPath); return null; }

            string path = MatDir + "chg_mat_ground.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex);
            m.SetTextureScale("_BaseMap", new Vector2(10f, 10f));
            m.SetTextureScale("_MainTex", new Vector2(10f, 10f));
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Texture2D PrepareGuideTexture()
        {
            string path = TexDir + "chg_controls_guide.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null && (ti.textureType != TextureImporterType.Default || ti.mipmapEnabled || ti.npotScale != TextureImporterNPOTScale.None))
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) Debug.LogWarning("[CHG] 조작키 안내 이미지를 찾지 못함: " + path);
            return tex;
        }

        static void SetNormalMap(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null || ti.textureType == TextureImporterType.NormalMap) return;
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }

        static Material MakeMaterial(string name, string texPrefix)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var desat = Shader.Find("CHG/CharacterDesaturate");   // HP에 따라 채도를 낮출 수 있는 캐릭터 셰이더
            if (m == null)
            {
                m = new Material(desat ? desat : Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (desat && m.shader != desat) m.shader = desat;
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + texPrefix + "_basecolor.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + texPrefix + "_normal.png");
            if (baseTex) { m.SetTexture("_BaseMap", baseTex); m.SetTexture("_MainTex", baseTex); }
            else Debug.LogWarning("[CHG] 텍스처 없음: " + texPrefix + "_basecolor.png");
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        // ================================================================ 무기

        static void AddWeapon(GameObject go, string name, bool melee, float light, float heavy, float reach, float radius)
        {
            var w = go.AddComponent<Weapon>();
            w.weaponName = name; w.isMelee = melee;
            w.lightDamage = light; w.heavyDamage = heavy; w.reach = reach; w.hitRadius = radius;
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!r || !sh) return;
            // 프리팹에서도 깨지지 않도록 재질을 파일로 저장해서 사용
            string path = MatDir + "chg_mat_" + go.name.Replace("chg_", "").Replace("NGH_", "") + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.color = c;
            EditorUtility.SetDirty(m);
            r.sharedMaterial = m;
        }

        // ================================================================ HP / 피격 범위 / 게임오버 (v19)

        // 플레이어에 HP 관련 구성 추가 — Build와 프리팹 마이그레이션에서 같이 사용
        static void ConfigurePlayerExtras(GameObject player, Dictionary<string, AnimationClip> clips)
        {
            var pc = player.GetComponent<PlayerController>();
            if (!pc) return;
            pc.maxHp = 3;
            pc.lightCombo2.hitOncePerAction = false;       // 2타는 두 번 베는 동작 → 베기마다 1 데미지 (판정 2번)
            bool hasDeath = clips != null && clips.ContainsKey("death");
            AnimationClip dc = null;
            if (clips != null && (clips.TryGetValue("death", out dc) || clips.TryGetValue("hit2", out dc))) pc.deathClipLength = dc.length;
            pc.deathHoldAt = hasDeath ? 1f : 0.45f;       // 대신 쓰는 피격(대)는 쓰러지는 지점에서 멈춤
            if (clips != null && clips.TryGetValue("standup", out var su)) pc.standupClipLength = su.length;

            // 무기 데미지: 좌클릭 1, 강공격 3
            foreach (var w in player.GetComponentsInChildren<Weapon>(true))
                if (w.isMelee) { w.lightDamage = 1f; w.heavyDamage = 3f; }

            // HP에 따라 채도
            var model = pc.animator ? pc.animator.gameObject : player;
            if (!model.GetComponent<HpTint>()) model.AddComponent<HpTint>();

            // 피격 범위
            var hb = player.transform.Find("chg_Hurtbox");
            if (!hb)
            {
                var go = new GameObject("chg_Hurtbox");
                go.transform.SetParent(player.transform, false);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; cap.center = new Vector3(0f, 0.5f, 0f); cap.radius = 0.25f; cap.height = 1.0f;
                go.AddComponent<PlayerHurtbox>();
                hb = go.transform;
            }
            hb.GetComponent<PlayerHurtbox>().player = pc;

            // Game Over 화면
            var go2 = player.GetComponent<GameOverScreen>();
            if (!go2) go2 = player.AddComponent<GameOverScreen>();
            go2.player = pc;
            go2.image = PrepareUiTexture(TexDir + "chg_gameover.png");
        }

        static Texture2D PrepareUiTexture(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null && (ti.mipmapEnabled || ti.npotScale != TextureImporterNPOTScale.None || ti.textureType != TextureImporterType.Default))
            {
                ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
            }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!t) Debug.LogWarning("[CHG] 이미지 없음: " + path);
            return t;
        }

        // 이미 만들어 둔 프리팹/컨트롤러/재질에 HP 업데이트를 적용 (씬을 다시 만들지 않음)
        [MenuItem("CHG/Apply HP Update To Prefabs")]
        public static void MigrateHpUpdate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var clips = LoadClips();

            // 캐릭터 재질 → 채도 셰이더
            MakeMaterial("chg_mat_body", "chg_body");
            MakeMaterial("chg_mat_sword", "chg_sword");

            // 애니메이터에 Death 상태 추가
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl && !ctrl.layers[0].stateMachine.states.Any(cs => cs.state.name == "Death"))
            {
                AddState(ctrl.layers[0].stateMachine, "Death", clips, clips.ContainsKey("death") ? "death" : "hit2");
                EditorUtility.SetDirty(ctrl); AssetDatabase.SaveAssets();
            }

            // 플레이어 프리팹
            string pp = PrefabDir + "NGH_Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(pp);
            ConfigurePlayerExtras(root, clips);
            PrefabUtility.SaveAsPrefabAsset(root, pp);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log("[CHG] HP 업데이트 적용: " + pp);

            // 허수아비 프리팹: HP 5, 5초 뒤 부활
            string dp = PrefabDir + "NGH_Dummy.prefab";
            if (File.Exists(dp))
            {
                var d = PrefabUtility.LoadPrefabContents(dp);
                var dmg = d.GetComponent<Damageable>();
                if (dmg) { dmg.maxHp = 5f; dmg.respawnDelay = 5f; }
                PrefabUtility.SaveAsPrefabAsset(d, dp);
                PrefabUtility.UnloadPrefabContents(d);
            }

            // 프로토타입 씬의 허수아비 중 프리팹이 아닌 것도 맞춤
            foreach (var path in new[] { ScenePath, TestScenePath })
            {
                if (!File.Exists(path)) continue;
                var sc = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var dmg in Object.FindObjectsByType<Damageable>(FindObjectsInactive.Include))
                { dmg.maxHp = 5f; dmg.respawnDelay = 5f; EditorUtility.SetDirty(dmg); }
                EditorSceneManager.SaveScene(sc);
            }
            AssetDatabase.SaveAssets();

            // 검증
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
            int fail = 0;
            var pc = player.GetComponent<PlayerController>();
            Check(pc && pc.maxHp == 3, "플레이어 최대 HP 3", ref fail);
            var sword = player.GetComponentsInChildren<Weapon>(true).FirstOrDefault(w => w.isMelee);
            Check(sword && Mathf.Approximately(sword.lightDamage, 1f) && Mathf.Approximately(sword.heavyDamage, 3f), "데미지: 좌클릭 1 / 강공격 3", ref fail);
            Check(pc && !pc.lightCombo2.hitOncePerAction, "2타는 베기마다 맞음 (판정 2번)", ref fail);
            Check(player.GetComponentInChildren<HpTint>(true), "HP 채도 효과(NGH_HpTint)", ref fail);
            var hurt = player.GetComponentInChildren<PlayerHurtbox>(true);
            Check(hurt && hurt.GetComponent<CapsuleCollider>() && hurt.GetComponent<CapsuleCollider>().isTrigger, "피격 범위(chg_Hurtbox, Trigger 캡슐)", ref fail);
            var gos = player.GetComponent<GameOverScreen>();
            Check(gos && gos.image, "Game Over 화면과 이미지", ref fail);
            var bodyMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "chg_mat_body.mat");
            Check(bodyMat && bodyMat.shader && bodyMat.shader.name == "CHG/CharacterDesaturate" && bodyMat.shader.isSupported, "캐릭터 재질이 채도 셰이더 사용 (셰이더 컴파일 정상)", ref fail);
            Check(ctrl && ctrl.layers[0].stateMachine.states.Any(cs => cs.state.name == "Death" && cs.state.motion), "애니메이터 Death 상태", ref fail);
            var dummy = AssetDatabase.LoadAssetAtPath<GameObject>(dp);
            var dd = dummy ? dummy.GetComponent<Damageable>() : null;
            Check(dd && Mathf.Approximately(dd.maxHp, 5f) && Mathf.Approximately(dd.respawnDelay, 5f), "허수아비 HP 5, 5초 뒤 부활", ref fail);
            Debug.Log(fail == 0 ? "[CHG][검증] HP 업데이트 전체 통과" : "[CHG][검증] HP 업데이트 실패 " + fail + "건");
        }

        // 쓰러짐(down) / 일어남(standup) 동작 적용 (v20)
        [MenuItem("CHG/Apply Death + StandUp To Prefabs")]
        public static void MigrateDeathStandup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SetLoops();
            var clips = LoadClips();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl)
            {
                var sm = ctrl.layers[0].stateMachine;
                var death = sm.states.Select(cs => cs.state).FirstOrDefault(st => st.name == "Death");
                if (death == null) death = AddState(sm, "Death", clips, "death");
                else if (clips.TryGetValue("death", out var dc)) death.motion = dc;
                if (!sm.states.Any(cs => cs.state.name == "StandUp")) AddState(sm, "StandUp", clips, "standup");
                EditorUtility.SetDirty(ctrl); AssetDatabase.SaveAssets();
            }
            string pp = PrefabDir + "NGH_Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(pp);
            ConfigurePlayerExtras(root, clips);
            var gos = root.GetComponent<GameOverScreen>();
            if (gos) gos.showDelay = 0.3f;
            PrefabUtility.SaveAsPrefabAsset(root, pp);
            PrefabUtility.UnloadPrefabContents(root);

            int fail = 0;
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
            var pc = player.GetComponent<PlayerController>();
            Check(clips.ContainsKey("death") && clips.ContainsKey("standup"), "down/standup 동작 파일 불러옴", ref fail);
            Check(ctrl && clips.ContainsKey("death") && ctrl.layers[0].stateMachine.states.Any(cs => cs.state.name == "Death" && cs.state.motion == clips["death"]), "Death 상태 = 쓰러짐(down) 동작", ref fail);
            Check(ctrl && ctrl.layers[0].stateMachine.states.Any(cs => cs.state.name == "StandUp" && cs.state.motion), "StandUp 상태 = 일어남(standup) 동작", ref fail);
            Check(pc && pc.deathClipLength > 1f && pc.standupClipLength > 1f && Mathf.Approximately(pc.deathHoldAt, 1f),
                  $"동작 길이 입력 (쓰러짐 {(pc ? pc.deathClipLength : 0):0.##}초, 일어남 {(pc ? pc.standupClipLength : 0):0.##}초)", ref fail);
            Debug.Log(fail == 0 ? "[CHG][검증] 쓰러짐/부활 업데이트 전체 통과" : "[CHG][검증] 쓰러짐/부활 업데이트 실패 " + fail + "건");
        }

        // 일어남: 앞쪽 누워 있는 시간 줄임(클립 시작 72프레임) + 1.5배속 (v21)
        static void ApplyStandupTuning()
        {
            var clips = LoadClips();
            string pp = PrefabDir + "NGH_Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(pp);
            var pc = root.GetComponent<PlayerController>();
            if (pc)
            {
                pc.standupPlaySpeed = 1.5f;
                if (clips.TryGetValue("standup", out var su)) pc.standupClipLength = su.length;
            }
            PrefabUtility.SaveAsPrefabAsset(root, pp);
            PrefabUtility.UnloadPrefabContents(root);
            var p2 = AssetDatabase.LoadAssetAtPath<GameObject>(pp).GetComponent<PlayerController>();
            Debug.Log($"[CHG][검증] 일어남 동작: 길이 {p2.standupClipLength:0.##}초, {p2.standupPlaySpeed}배속 → 실제 {p2.standupClipLength / p2.standupPlaySpeed:0.##}초 (+1초 무적)");
        }

        // ================================================================ 프리팹 저장 + 테스트 씬

        const string PrefabDir = "Assets/Prefabs/NGH/";
        const string TestScenePath = "Assets/Scenes/CHG/chg_PrefabTest.unity";

        [MenuItem("CHG/Save Prefabs + Test Scene")]
        public static void SavePrefabsAndTest()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) { Debug.LogError("[CHG] 먼저 CHG > Build Prototype Scene 을 실행하세요."); return; }
            var proto = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var pc = Object.FindFirstObjectByType<PlayerController>();
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            var guide = Object.FindFirstObjectByType<ControlsOverlay>();
            var dummy = Object.FindObjectsByType<Damageable>(FindObjectsInactive.Include)
                              .OrderBy(d => d.name).FirstOrDefault();
            if (!pc) { Debug.LogError("[CHG] 씬에서 NGH_Player를 찾지 못했습니다."); return; }

            // 씬 안에만 있는 재질(파일이 아닌 것)은 프리팹에서 깨지므로 파일로 저장
            foreach (var root in new[] { pc ? pc.gameObject : null, dummy ? dummy.gameObject : null })
                if (root) PersistMaterials(root);

            var playerPrefab = SavePrefab(pc.gameObject, "NGH_Player");
            GameObject camPrefab = null, guidePrefab = null, dummyPrefab = null;
            if (cam)
            {
                var t = cam.target; var p = cam.player;
                cam.target = null; cam.player = null;            // 씬 오브젝트 참조는 프리팹에 넣지 않음 (실행 시 자동으로 찾음)
                camPrefab = SavePrefab(cam.gameObject, "NGH_PlayerCamera");
                cam.target = t; cam.player = p;
            }
            if (guide) guidePrefab = SavePrefab(guide.gameObject, "NGH_ControlsGuide");
            if (dummy) dummyPrefab = SavePrefab(dummy.gameObject, "NGH_Dummy");
            EditorSceneManager.SaveScene(proto);

            // ---------- 테스트 씬: 프리팹만으로 구성
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "chg_Ground";
            ground.transform.localScale = Vector3.one * 5f;
            var gm = MakeGroundMaterial();
            if (gm) ground.GetComponent<Renderer>().sharedMaterial = gm;

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            if (camPrefab) PrefabUtility.InstantiatePrefab(camPrefab);
            if (guidePrefab) PrefabUtility.InstantiatePrefab(guidePrefab);
            if (dummyPrefab)
            {
                var ps = new[] { new Vector3(0f, 0.55f, 4f), new Vector3(3f, 0.55f, 6f), new Vector3(-3.5f, 0.55f, 5f) };
                for (int i = 0; i < ps.Length; i++)
                {
                    var d = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab);
                    d.name = "NGH_Dummy_" + (i + 1);
                    d.transform.position = ps[i];
                }
            }
            EditorSceneManager.SaveScene(scene, TestScenePath);
            ValidatePrefabs(player, camPrefab, guidePrefab, dummyPrefab);
            AssetDatabase.Refresh();
        }

        static void Check(bool ok, string what, ref int fail)
        {
            if (ok) Debug.Log("[CHG][검증] OK  " + what);
            else { fail++; Debug.LogError("[CHG][검증] 실패 " + what); }
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            string path = PrefabDir + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
            Debug.Log("[CHG] 프리팹 저장: " + path);
            return prefab;
        }

        static void PersistMaterials(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || EditorUtility.IsPersistent(m)) continue;
                    string path = MatDir + "chg_mat_" + r.gameObject.name.Replace("chg_", "").Replace("NGH_", "") + (i > 0 ? "_" + i : "") + ".mat";
                    var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (existing) { existing.CopyPropertiesFromMaterial(m); EditorUtility.SetDirty(existing); mats[i] = existing; }
                    else { AssetDatabase.CreateAsset(m, path); }
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            AssetDatabase.SaveAssets();
        }

        // 테스트 씬에 놓인 프리팹이 제대로 연결돼 있는지 Console에 결과를 남김
        static void ValidatePrefabs(GameObject player, GameObject camPrefab, GameObject guidePrefab, GameObject dummyPrefab)
        {
            int fail = 0;

            var pc = player ? player.GetComponent<PlayerController>() : null;
            Check(pc, "NGH_Player 프리팹에 NGH_PlayerController 있음", ref fail);
            Check(pc && pc.animator && pc.animator.runtimeAnimatorController, "애니메이터와 컨트롤러 연결됨", ref fail);
            Check(player && player.GetComponent<CharacterController>(), "CharacterController 있음", ref fail);
            var holder = pc ? pc.weapons : null;
            Check(holder && holder.slots.Count == 3 && holder.slots.All(w => w), "무기 3칸 모두 연결됨", ref fail);
            Check(player && player.GetComponentInChildren<GroundClamp>(true), "땅 뚫림 방지(NGH_GroundClamp) 있음", ref fail);
            bool matsOk = player && player.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).All(m => m && EditorUtility.IsPersistent(m));
            Check(matsOk, "플레이어의 모든 재질이 파일로 저장돼 있음 (분홍색으로 안 깨짐)", ref fail);
            Check(pc && pc.lightAttack.clipLength > 0f && pc.heavyAttack.clipLength > 0f, "공격 동작 길이 입력됨", ref fail);
            Check(camPrefab && camPrefab.GetComponent<ThirdPersonCamera>() && camPrefab.CompareTag("MainCamera"), "카메라 프리팹 (MainCamera 태그)", ref fail);
            Check(guidePrefab && guidePrefab.GetComponent<ControlsOverlay>().guide, "ESC 안내 프리팹과 이미지 연결됨", ref fail);
            Check(dummyPrefab && dummyPrefab.GetComponent<Damageable>() && dummyPrefab.GetComponent<Collider>(), "허수아비 프리팹 (피격/락온 대상)", ref fail);
            Debug.Log(fail == 0 ? "[CHG][검증] 전체 통과 — " + TestScenePath + " 에서 Play로 확인하세요."
                                : "[CHG][검증] 실패 " + fail + "건");
        }

        static void MakeDummy(string name, Vector3 pos)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            d.name = name;
            d.transform.localScale = new Vector3(0.45f, 0.55f, 0.45f);   // 높이 약 1.1m
            d.transform.position = pos + Vector3.up * 0.55f;
            Tint(d, new Color(0.75f, 0.6f, 0.4f));
            var dmg = d.AddComponent<Damageable>();
            dmg.markerHeight = 0.75f;
        }

        // ================================================================ 애니메이션

        static string StateName(string path) => Path.GetFileNameWithoutExtension(path).Replace(AnimPrefix, "");

        static IEnumerable<string> ClipFiles() =>
            Directory.GetFiles(AnimDir, AnimPrefix + "*.fbx").Select(p => p.Replace('\\', '/'));

        static void SetLoops()
        {
            foreach (var path in ClipFiles())
            {
                bool loop = LoopClips.Contains(StateName(path));
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { Debug.LogWarning("[CHG] 임포터를 찾지 못함: " + path); continue; }
                bool changed = imp.clipAnimations.Length == 0;
                if (imp.animationType != ModelImporterAnimationType.Generic) { imp.animationType = ModelImporterAnimationType.Generic; changed = true; }
                if (!imp.preserveHierarchy) { imp.preserveHierarchy = true; changed = true; }
                var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
                foreach (var c in clips)
                    if (c.loopTime != loop) { c.loopTime = loop; changed = true; }
                if (!changed) continue;
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }
        }

        static Dictionary<string, AnimationClip> LoadClips()
        {
            var d = new Dictionary<string, AnimationClip>();
            foreach (var path in ClipFiles())
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip) d[StateName(path)] = clip;
                else Debug.LogWarning("[CHG] 애니메이션 클립이 없습니다: " + path);
            }
            return d;
        }

        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, float walkTimeScale)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter
                { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var sm = ctrl.layers[0].stateMachine;

            // Locomotion: 대기(0) → 걷기(0.5, run을 느리게) → 달리기(1, fastrun)
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (clips.TryGetValue("standby", out var idle)) tree.AddChild(idle, 0f);
            // 걷기: walk 동작이 있으면 사용, 없으면 run을 느리게 재생
            bool hasWalk = clips.TryGetValue("walk", out var walk);
            if (!hasWalk) clips.TryGetValue("run", out walk);
            if (walk) tree.AddChild(walk, 0.5f);
            if (clips.TryGetValue("fastrun", out var fast)) tree.AddChild(fast, 1f);
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
                if (children[i].motion == walk) children[i].timeScale = hasWalk ? walkTimeScale : 0.55f;
            tree.children = children;
            loco.speedParameter = "AnimSpeed";
            loco.speedParameterActive = true;   // 음수면 역재생 → 임시 뒷걸음
            sm.defaultState = loco;

            AddState(sm, "Roll", clips, "roll");
            AddState(sm, "LightAttack", clips, "swordslash");
            AddState(sm, "HeavyAttack", clips, "powerslash");   // 강공격: 파워슬래시
            AddState(sm, "Combo2", clips, "doubleslash");       // 약공격 2타: 2단베기
            string[] strafeStates = { "StrafeLeft", "StrafeRight" }, strafeClips = { "leftwalk", "rightwalk" };
            for (int i = 0; i < 2; i++)
            {
                var s2 = AddState(sm, strafeStates[i], clips, strafeClips[i]);   // 락온 중 좌/우 걷기 (A, D, WA, WD)
                s2.speedParameter = "AnimSpeed";
                s2.speedParameterActive = true;
            }
            AddState(sm, "Shot", clips, "shot");
            AddState(sm, "Draw", clips, "drawsword");      // 발도 (Mixamo Draw Sword 1 리타겟)
            AddState(sm, "Sheath", clips, "sheathsword");  // 납도 (Mixamo Sheath Sword 리타겟)
            AddState(sm, "HitSmall", clips, "hit");         // 피격(소): 데미지 1
            AddState(sm, "HitLarge", clips, "hit2");        // 피격(대): 데미지 2 이상
            AddState(sm, "Death", clips, clips.ContainsKey("death") ? "death" : "hit2");   // 사망 (전용 동작이 없으면 피격(대)로 대신)
            AddState(sm, "StandUp", clips, "standup");     // 부활 (일어남)

            AssetDatabase.SaveAssets();
            return ctrl;
        }

        // 기존 컨트롤러의 걷기 재생 속도(Time Scale)를 읽어서 다시 만들 때 유지
        static float ReadWalkTimeScale()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null || ctrl.layers.Length == 0) return -1f;
            foreach (var cs in ctrl.layers[0].stateMachine.states)
            {
                var tree = cs.state.motion as BlendTree;
                if (tree == null) continue;
                foreach (var c in tree.children)
                    if (Mathf.Abs(c.threshold - 0.5f) < 0.01f) return c.timeScale;   // 0.5 = 걷기 자리
            }
            return -1f;
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string state, Dictionary<string, AnimationClip> clips, string clip)
        {
            var st = sm.AddState(state);
            if (clips.TryGetValue(clip, out var c)) st.motion = c;
            else Debug.LogWarning($"[CHG] '{clip}' 클립을 찾지 못했습니다 ({state}).");
            return st;
        }

        static Bounds GetBounds(Transform root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(root.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
#endif
