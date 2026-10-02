using SteamAttack.InventorySystem;
using SteamAttack.Items;
using SteamAttack.PlayerControl;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SteamAttack.EditorTools
{
    /// <summary>
    /// 지금 열려 있는 씬에 조작 가능한 플레이어 캡슐과 주울 수 있는 필드 아이템을 깐다.
    /// 메뉴: SteamAttack ▸ 플레이어 캡슐 + 떨어진 아이템 배치
    ///
    /// 캡슐은 Player 태그 + CharacterController 라서, 트리거로 된 ItemPickup 위를
    /// 지나가면 바로 인벤토리로 들어간다.
    /// </summary>
    public static class PlayerTestbedSetup
    {
        private const string DataRoot = "Assets/GameData/NMJ";
        private const string MaterialFolder = "Assets/Materials/NMJ";

        /// <summary>플레이어를 떨어뜨릴 위치(광화문과 근정전 사이 어도). 지면은 레이캐스트로 찾는다.</summary>
        private static readonly Vector3 SpawnHint = new Vector3(0f, 0f, 10f);

        [MenuItem("SteamAttack/플레이어 캡슐 + 떨어진 아이템 배치", false, 3)]
        public static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();

            if (Object.FindAnyObjectByType<PlayerInventory>() == null)
            {
                EditorUtility.DisplayDialog("SteamAttack",
                    "씬에 PlayerInventory 가 없다.\n먼저 'SteamAttack ▸ 현재 씬에 인벤토리·메인퀘스트 설치' 를 실행해라.", "확인");
                return;
            }

            Vector3 spawn = FindGround(SpawnHint);

            var player = BuildPlayer(spawn);
            HookCamera(player.transform);
            int dropped = DropItems(spawn);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = player;
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.FrameSelected();

            Debug.Log($"[SteamAttack] 플레이어를 {spawn} 에 배치하고 필드 아이템 {dropped}개를 떨어뜨렸다. " +
                      "재생 후 WASD 로 이동, 아이템 위를 지나가면 인벤토리에 들어간다. (I 키로 가방 확인)");
        }

        // ── 플레이어 ──────────────────────────────────────────

        private static GameObject BuildPlayer(Vector3 spawn)
        {
            var existing = GameObject.FindWithTag("Player");
            if (existing != null)
            {
                existing.transform.position = spawn;
                EnsurePlayerComponents(existing);
                return existing;
            }

            var player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = spawn;

            // 눈에 보이는 몸통. 충돌은 CharacterController 가 맡으므로 콜라이더는 떼어낸다.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            Paint(visual, new Color(0.85f, 0.78f, 0.62f), "Player_Body", 0f);

            // 어느 쪽을 보고 있는지 알아볼 수 있게 코를 하나 붙인다.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.35f, 0.42f);
            nose.transform.localScale = new Vector3(0.22f, 0.22f, 0.35f);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            Paint(nose, new Color(0.85f, 0.45f, 0.2f), "Player_Nose", 0.3f);

            EnsurePlayerComponents(player);
            return player;
        }

        private static void EnsurePlayerComponents(GameObject player)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller == null) controller = player.AddComponent<CharacterController>();

            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.5f;

            if (player.GetComponent<PlayerMover>() == null) player.AddComponent<PlayerMover>();
        }

        private static void HookCamera(Transform target)
        {
            var camera = Camera.main;
            if (camera == null) camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogWarning("[SteamAttack] 씬에 카메라가 없어서 추적 카메라를 붙이지 못했다.");
                return;
            }

            var follow = camera.GetComponent<PlayerFollowCamera>();
            if (follow == null) follow = camera.gameObject.AddComponent<PlayerFollowCamera>();
            follow.SetTarget(target);
            EditorUtility.SetDirty(camera.gameObject);
        }

        // ── 필드 아이템 ───────────────────────────────────────

        private static int DropItems(Vector3 center)
        {
            var root = GameObject.Find("FieldItems") ?? new GameObject("FieldItems");

            // 떨어뜨릴 목록: (아이템 아이디, 개수)
            var drops = new (string id, int count)[]
            {
                ("mat_firewater_core", 2),
                ("mat_firewater_core", 3),
                ("mat_firewater_core", 1),
                ("mat_armillary_ring", 1),
                ("con_ondol_tonic", 2),
                ("con_ondol_tonic", 1),
                ("mat_hyeonmu_scale", 1),
            };

            int made = 0;
            for (int i = 0; i < drops.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemData>($"{DataRoot}/Items/Item_{drops[i].id}.asset");
                if (item == null)
                {
                    Debug.LogWarning($"[SteamAttack] 아이템 에셋을 못 찾았다: {drops[i].id}");
                    continue;
                }

                string name = $"Drop_{i:00}_{item.ItemId}";
                if (root.transform.Find(name) != null) continue;

                float angle = i / (float)drops.Length * Mathf.PI * 2f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(4f, 9f);
                Vector3 position = FindGround(center + offset) + Vector3.up * 0.6f;

                var drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                drop.name = name;
                drop.transform.SetParent(root.transform, false);
                drop.transform.position = position;
                drop.transform.localScale = Vector3.one * 0.55f;

                var collider = drop.GetComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.radius = 1.4f; // 스케일 0.55 기준 약 0.77m — 스쳐 지나가도 먹히게

                Paint(drop, item.GradeColor, $"Pickup_{item.ItemId}", 0.6f);

                var pickup = drop.AddComponent<ItemPickup>();
                var so = new SerializedObject(pickup);
                so.FindProperty("item").objectReferenceValue = item;
                so.FindProperty("count").intValue = drops[i].count;
                so.FindProperty("playerTag").stringValue = "Player";
                so.FindProperty("requireFullSpace").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();

                drop.AddComponent<PickupBob>();
                made++;
            }

            return made;
        }

        // ── 공용 ──────────────────────────────────────────────

        /// <summary>위에서 아래로 쏴서 지면을 찾는다. 못 찾으면 y=1 로 둔다.</summary>
        private static Vector3 FindGround(Vector3 around)
        {
            var origin = new Vector3(around.x, 300f, around.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 1000f))
                return new Vector3(around.x, hit.point.y + 0.05f, around.z);

            return new Vector3(around.x, 1f, around.z);
        }

        private static void Paint(GameObject go, Color color, string materialName, float emission)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets/Materials", "NMJ");

            string path = $"{MaterialFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);

            if (emission > 0f && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
            }

            EditorUtility.SetDirty(material);
            renderer.sharedMaterial = material;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
