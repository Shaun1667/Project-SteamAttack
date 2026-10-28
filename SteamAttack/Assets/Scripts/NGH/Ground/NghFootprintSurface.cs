using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 발자국이 남는 바닥 영역 (눈 덮인 흙길 등). 이 오브젝트 아래 렌더러들을 감싸는 XZ 범위와 윗면 높이를 등록해 두면
    /// NghFootprints 가 그 안을 걸을 때만, 그 높이에 발자국을 찍는다.
    /// (길 바닥에 충돌체가 없어 레이캐스트로는 실제 보이는 면 높이를 알 수 없어서 영역으로 등록)
    /// </summary>
    public class NghFootprintSurface : MonoBehaviour
    {
        [Tooltip("발자국 색 (밟혀 눌린 눈 / 드러난 흙)")]
        public Color printColor = new Color(0.24f, 0.22f, 0.21f, 0.8f);
        [Tooltip("자동 계산된 범위 (월드 XZ) — 비어 있으면 자식 렌더러로 계산")]
        public Rect area;
        [Tooltip("자동 계산된 윗면 높이 (월드 Y)")]
        public float topY;
        [Tooltip("타원 구역 (월드 XZ 중심·반지름). 반지름이 0 이면 위의 사각 범위만 씀")]
        public Vector2 ellipseCenter, ellipseRadius;

        static readonly List<NghFootprintSurface> all = new List<NghFootprintSurface>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void OnEnable()
        {
            if (area.width <= 0f || area.height <= 0f) Recalculate();
            if (!all.Contains(this)) all.Add(this);
        }

        void OnDisable() => all.Remove(this);

        [ContextMenu("범위 다시 계산")]
        public void Recalculate()
        {
            bool has = false; Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            if (!has) return;
            area = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            topY = b.max.y;
        }

        /// <summary>월드 위치가 발자국 바닥 위인지. 그렇다면 그 바닥을 돌려줌</summary>
        public static NghFootprintSurface Find(Vector3 p)
        {
            foreach (var s in all)
                if (s && s.Contains(p)) return s;
            return null;
        }

        bool Contains(Vector3 p)
        {
            if (!area.Contains(new Vector2(p.x, p.z))) return false;
            if (ellipseRadius.x <= 0f || ellipseRadius.y <= 0f) return true;
            float ex = (p.x - ellipseCenter.x) / ellipseRadius.x, ez = (p.z - ellipseCenter.y) / ellipseRadius.y;
            // 타일은 가운데가 타원 안이면 깔리므로 가장자리는 타일 반 칸만큼 넉넉하게
            return ex * ex + ez * ez <= 1.08f;
        }
    }
}
