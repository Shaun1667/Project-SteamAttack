using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NGH.EditorTools
{
    /// <summary>
    /// 지붕·담장 위에 눈을 얹는다 (NGH/SnowCap 재질을 렌더러 재질 목록 끝에 추가 → 같은 메시를 한 번 더 그려 위를 향한 면만 눈으로 덮음).
    ///  - 원래 재질·모델은 그대로. 프리팹 인스턴스면 이 씬에서만 바뀌는 오버라이드가 된다
    ///  - 실내(근정전 안 바닥·어좌 등)와 사람이 걷는 바닥(월대 박석·계단)은 건너뜀 — 마당 눈은 치운 설정
    /// 메뉴: NGH/눈 얹기/선택한 오브젝트, NGH/눈 얹기/궁성 담장·건청궁 담장, NGH/눈 얹기/선택한 오브젝트에서 걷기
    /// </summary>
    public static class SnowCapApplier
    {
        const string MatPath = "Assets/Materials/NGH/M_SnowCap.mat";
        const string World = "현무_세계관 (1887 정월, 멈춘 도성)/";
        // 근정전(NMJ_Geunjeongjeon_VARCO)은 원래 모습 유지 — 넣지 않음
        static readonly string[] DefaultTargets =
        {
            World + "01_경복궁_궁성/VARCO_PR_PalaceWall",
            World + "06_건청궁_전등과_물불_발전기/VARCO_PR_InnerCourtWall",
        };
        // 이름에 들어 있으면 건너뜀: 실내, 걷는 바닥
        static readonly string[] SkipNames = { "내부", "어좌", "내벽", "박석", "하월대", "계단", "줄눈", "덮개판석", "마름돌", "전돌" };

        [MenuItem("NGH/눈 얹기/궁성 담장·건청궁 담장")]
        static void ApplyDefault()
        {
            var roots = new List<GameObject>();
            foreach (var p in DefaultTargets)
            {
                // 같은 경로가 여럿일 수 있어 이름으로 모두 찾음
                var name = p.Substring(p.LastIndexOf('/') + 1);
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                    if (t.name == name && t.gameObject.scene.IsValid()) roots.Add(t.gameObject);
            }
            Apply(roots);
        }

        [MenuItem("NGH/눈 얹기/선택한 오브젝트")]
        static void ApplySelected() => Apply(Selection.gameObjects);

        [MenuItem("NGH/눈 얹기/선택한 오브젝트에서 걷기")]
        static void RemoveSelected()
        {
            var mat = GetMaterial();
            int n = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    if (!mats.Contains(mat)) continue;
                    Undo.RecordObject(r, "눈 걷기");
                    r.sharedMaterials = mats.Where(m => m != mat).ToArray();
                    n++;
                }
            Debug.Log($"[SnowCap] 눈 걷음: 렌더러 {n}개");
        }

        public static void Apply(IEnumerable<GameObject> roots)
        {
            var mat = GetMaterial();
            var indoor = IndoorBounds();
            int added = 0, skipped = 0;
            foreach (var root in roots)
            {
                if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;
                    var mats = r.sharedMaterials;
                    if (mats.Contains(mat)) continue;
                    if (SkipNames.Any(s => r.name.Contains(s)) || IsIndoor(r.bounds, indoor)) { skipped++; continue; }
                    // 여러 서브메시 렌더러는 덧그림이 마지막 서브메시에만 적용되므로 생략
                    if (mf.sharedMesh.subMeshCount > 1) { skipped++; continue; }
                    Undo.RecordObject(r, "눈 얹기");
                    r.sharedMaterials = mats.Append(mat).ToArray();
                    added++;
                }
                EditorSceneManager.MarkSceneDirty(root.scene);
            }
            Debug.Log($"[SnowCap] 눈 얹음: 렌더러 {added}개 (실내·바닥 {skipped}개 생략)");
        }

        // 근정전 실내 눈막이 상자 (NMJ 프리팹의 트리거) — 지붕보다 낮게 실내에 들어 있는 것은 눈을 얹지 않음
        static List<Bounds> IndoorBounds()
        {
            var list = new List<Bounds>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
                if (c.isTrigger && c.name.Contains("눈막이")) list.Add(c.bounds);
            return list;
        }

        static bool IsIndoor(Bounds b, List<Bounds> indoor)
        {
            foreach (var i in indoor)
                if (b.max.y <= i.max.y + 0.05f && i.Contains(new Vector3(b.center.x, i.center.y, b.center.z))) return true;
            return false;
        }

        static Material GetMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m) return m;
            m = new Material(Shader.Find("NGH/SnowCap")) { name = "M_SnowCap" };
            AssetDatabase.CreateAsset(m, MatPath);
            return m;
        }
    }
}
