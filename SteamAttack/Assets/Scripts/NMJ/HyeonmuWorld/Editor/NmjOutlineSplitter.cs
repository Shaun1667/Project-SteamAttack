using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace NMJ.EditorTools
{
    /// <summary>
    /// 수묵 외곽선을 "두 번째 재질"에서 "자식 오브젝트"로 옮긴다 (플레이할 때만, 씬 파일은 그대로).
    ///
    /// 월드 빌더(HyeonmuWorldBuilder)는 외곽선 재질(SteamAttack/InkOutline)을 물체의 마지막 재질로 덧붙여 두었다.
    /// 그리는 결과는 같지만, 유니티는 그릴 면(서브메시)보다 재질이 많다고 물체마다
    /// "Mesh 'Cube' has more materials (2) than submeshes (1)" 경고를 낸다 (Main 한 번에 5천 개가 넘음).
    ///
    /// 빌드할 때와 에디터에서 플레이를 시작해 씬을 불러올 때(IProcessSceneWithReport — 정적 배칭으로 메시가 합쳐지기 전),
    /// 남는 외곽선 재질을 떼어 같은 메시를 쓰는 자식 "InkOutline"에 붙인다. 씬 파일은 바뀌지 않는다.
    /// 보이는 모양·그리는 횟수는 같고 경고만 사라진다.
    /// </summary>
    public class NmjOutlineSplitter : IProcessSceneWithReport
    {
        private const string OutlineShader = "SteamAttack/InkOutline";

        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report) => Split(scene);

        private static readonly List<Material> keep = new List<Material>();
        private static readonly List<Material> extra = new List<Material>();

        public static int Split(Scene scene)
        {
            int moved = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                Mesh mesh = f != null ? f.sharedMesh : null;
                if (mesh == null) continue;
                Material[] mats = r.sharedMaterials;
                int sub = mesh.subMeshCount;
                if (mats.Length <= sub) continue;

                keep.Clear();
                extra.Clear();
                for (int i = 0; i < mats.Length; i++)
                {
                    if (i < sub) keep.Add(mats[i]);
                    else if (mats[i] != null && mats[i].shader != null && mats[i].shader.name == OutlineShader) extra.Add(mats[i]);
                }
                if (extra.Count == 0) continue;   // 외곽선이 아닌 남는 재질은 건드리지 않는다

                r.sharedMaterials = keep.ToArray();
                foreach (Material m in extra)
                {
                    var go = new GameObject("InkOutline");
                    go.layer = r.gameObject.layer;
                    go.transform.SetParent(r.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer o = go.AddComponent<MeshRenderer>();
                    // 서브메시가 여러 개면 원래처럼 마지막 서브메시에 외곽선이 그려지도록 같은 개수로 채운다
                    var arr = new Material[sub];
                    for (int i = 0; i < sub; i++) arr[i] = m;
                    o.sharedMaterials = arr;
                    o.shadowCastingMode = ShadowCastingMode.Off;
                    o.receiveShadows = false;
                    o.lightProbeUsage = LightProbeUsage.Off;
                    o.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    o.enabled = r.enabled;
                }
                moved++;
            }
            return moved;
        }
    }
}
