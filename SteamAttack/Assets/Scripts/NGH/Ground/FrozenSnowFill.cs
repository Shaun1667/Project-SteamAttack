using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 허공에 멈춘 눈송이 채우기 (로딩 최적화).
    ///  - 예전: 파티클 미리 돌리기(Prewarm) 5초 + 트리거 모듈(근정전 안 눈 지우기)을 씬을 불러올 때마다 수만 개에 계산 → 로딩이 약 27초 늘어남
    ///  - 지금: 시작할 때 최대 개수만큼 한 번에 뿌리고, 막는 상자(근정전 실내 등) 안에 든 눈송이를 한 번만 지운 뒤 더 뿜지 않는다
    ///  - 눈송이는 속도 0 · 수명이 매우 길어(멈춘 눈) 다시 채울 필요가 없다. 해빙 엔딩(ThawEnding)은 중력만 바꾸므로 그대로 동작
    /// 멈춘 눈송이 파티클 시스템에 붙입니다. (이 스크립트가 있으면 파티클의 Prewarm·트리거·방출은 꺼 둔다)
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class FrozenSnowFill : MonoBehaviour
    {
        [Tooltip("이 상자 콜라이더들 안에 든 눈송이는 지움 (예: 근정전 실내_눈막이)")]
        public BoxCollider[] blockers;
        [Tooltip("뿌릴 눈송이 수 (0 = 파티클 최대 개수)")]
        [Min(0)] public int count;

        ParticleSystem _ps;

        void Start()
        {
            _ps = GetComponent<ParticleSystem>();
            var em = _ps.emission;
            em.enabled = false;
            _ps.Clear();
            _ps.Play();
            _ps.Emit(count > 0 ? count : _ps.main.maxParticles);
            Cull();
        }

        void Cull()
        {
            if (blockers == null || blockers.Length == 0) return;
            var buf = new ParticleSystem.Particle[_ps.particleCount];
            int n = _ps.GetParticles(buf);
            bool local = _ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            int keep = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = local ? transform.TransformPoint(buf[i].position) : buf[i].position;
                if (Inside(p)) continue;
                buf[keep++] = buf[i];
            }
            _ps.SetParticles(buf, keep);
        }

        bool Inside(Vector3 world)
        {
            foreach (BoxCollider b in blockers)
            {
                if (!b) continue;
                Vector3 l = b.transform.InverseTransformPoint(world) - b.center;
                Vector3 h = b.size * 0.5f;
                if (Mathf.Abs(l.x) <= h.x && Mathf.Abs(l.y) <= h.y && Mathf.Abs(l.z) <= h.z) return true;
            }
            return false;
        }
    }
}
