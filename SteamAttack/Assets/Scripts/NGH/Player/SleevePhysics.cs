using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 저고리 소매(배래) 물리. 팔 아래로 늘어뜨린 뼈 사슬(Tools/NGH/Hero/7. Build Sleeve Physics Bones 로 생성)을 움직입니다.
    ///  - 각 마디는 팔에 붙은 원래 위치로 돌아가려는 스프링 + 감쇠 + 중력으로 움직임
    ///  - 팔을 휘두르면 관성만큼 뒤에 남았다가 따라와서 소매가 흔들림
    ///  - 같은 소매의 이웃 사슬과 간격을 유지해서 천이 찢어지거나 뭉치지 않음
    ///  - 몸통·허벅지 캡슐 안으로 들어가지 않음
    /// 애니메이션과 GroundClamp(1000), SkirtPhysics(1050) 뒤에 실행합니다.
    /// </summary>
    [DefaultExecutionOrder(1060)]
    public class SleevePhysics : MonoBehaviour
    {
        [Header("뼈 (빌더가 채움)")]
        [Tooltip("사슬 순서대로 나열한 마디. 사슬 c의 j번째 마디 = joints[c * jointsPerChain + j]. j = 0은 팔에 고정")]
        public Transform[] joints = new Transform[0];
        public int chainCount;
        public int jointsPerChain;
        [Tooltip("사슬 c와 c+1이 같은 소매라서 간격을 유지할지")]
        public bool[] linkNext = new bool[0];
        [Tooltip("소매가 뚫고 들어가지 않을 몸 캡슐 (게임 판정과 상관없는 콜라이더)")]
        public CapsuleCollider[] colliders = new CapsuleCollider[0];
        [Tooltip("콜라이더 없이 뼈 두 개 사이로 정의한 몸 캡슐 (허리~가슴 등)")]
        public BodySegment[] bodySegments = new BodySegment[0];

        [System.Serializable]
        public struct BodySegment
        {
            public Transform from, to;
            public float radius;
            [Tooltip("캡슐 중심을 from 뼈 기준 앞(+)·뒤(-)로 옮김 (m, 캐릭터 정면 방향)")]
            public float forwardOffset;
        }

        [Header("움직임")]
        [Tooltip("원래 모양으로 돌아오는 빠르기(Hz). 높을수록 빳빳하고, 낮을수록 하늘하늘")]
        [Range(0.5f, 8f)] public float frequency = 2.2f;
        [Tooltip("흔들림이 멈추는 빠르기 (1/초)")]
        [Range(0f, 20f)] public float damping = 5f;
        [Tooltip("팔이 움직일 때 소매가 뒤에 남는 정도. 0 = 팔에 붙어 같이 움직임, 1 = 제자리에 남았다가 끌려옴")]
        [Range(0f, 1f)] public float inertia = 0.6f;
        [Tooltip("중력 비율 (흔들릴 때 아래로 당기는 힘)")]
        [Range(0f, 2f)] public float gravityScale = 0.8f;
        [Tooltip("소매가 늘어지는 기준 방향을 실제 아래쪽으로 기울이는 비율. 0 = 팔에 붙은 원래 방향(팔을 내리면 몸 안쪽을 향함), 1 = 항상 바로 아래로")]
        [Range(0f, 1f)] public float hangFollow = 0.75f;        [Tooltip("각 마디가 원래 방향에서 벌어질 수 있는 최대 각도")]
        [Range(5f, 120f)] public float maxAngle = 70f;
        [Tooltip("이웃 사슬과의 간격 유지 강도")]
        [Range(0f, 1f)] public float linkStiffness = 0.5f;
        [Tooltip("몸 캡슐 표면에서 띄울 거리 (m)")]
        public float colliderMargin = 0.015f;
        [Tooltip("이 거리 이상 순간이동하면 시뮬레이션을 초기화 (m)")]
        public float teleportDistance = 0.7f;

        const float StepRate = 120f;
        const int Iterations = 2;

        Vector3[] _pos, _prev, _target;
        Quaternion[] _restLocalRot;
        Vector3[] _restLocalPos;
        float[] _segLen, _linkLen;
        Vector3[] _lastRoot;
        bool _ready;

        int Idx(int c, int j) => c * jointsPerChain + j;

        void Awake()
        {
            int n = chainCount * jointsPerChain;
            if (n == 0 || joints.Length != n) { enabled = false; return; }
            _pos = new Vector3[n]; _prev = new Vector3[n]; _target = new Vector3[n];
            _restLocalRot = new Quaternion[n]; _restLocalPos = new Vector3[n];
            _segLen = new float[n]; _linkLen = new float[n];
            _lastRoot = new Vector3[chainCount];
            for (int i = 0; i < n; i++)
            {
                _restLocalRot[i] = joints[i].localRotation;
                _restLocalPos[i] = joints[i].localPosition;
            }
            for (int c = 0; c < chainCount; c++)
                for (int j = 0; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    _segLen[i] = j > 0 ? Vector3.Distance(joints[i].position, joints[Idx(c, j - 1)].position) : 0f;
                    _linkLen[i] = Linked(c) ? Vector3.Distance(joints[i].position, joints[Idx(c + 1, j)].position) : 0f;
                }
        }

        bool Linked(int c) => c + 1 < chainCount && c < linkNext.Length && linkNext[c];

        void OnEnable() => _ready = false;

        void LateUpdate()
        {
            if (_pos == null) return;

            // 1) 애니메이션 기준 자세: 마디를 원래대로 돌려놓고(팔을 따라감) 위치를 읽음
            for (int i = 0; i < joints.Length; i++) joints[i].SetLocalPositionAndRotation(_restLocalPos[i], _restLocalRot[i]);
            for (int i = 0; i < joints.Length; i++) _target[i] = joints[i].position;
            HangTargets();

            float dt = Time.deltaTime;
            bool teleported = false;
            if (_ready)
                for (int c = 0; c < chainCount && !teleported; c++)
                    teleported = (_target[Idx(c, 0)] - _lastRoot[c]).sqrMagnitude > teleportDistance * teleportDistance;
            if (!_ready || teleported || dt > 0.1f)
            {
                for (int i = 0; i < _pos.Length; i++) _pos[i] = _prev[i] = _target[i];
                for (int c = 0; c < chainCount; c++) _lastRoot[c] = _target[Idx(c, 0)];
                _ready = true;
            }
            else if (dt > 0f) Simulate(dt);

            ApplyToBones();
        }

        // 사슬 모양은 그대로 두고, 뿌리를 중심으로 사슬 전체를 실제 아래쪽으로 hangFollow 만큼 돌림
        void HangTargets()
        {
            if (hangFollow <= 0f) return;
            Vector3 down = Physics.gravity.sqrMagnitude > 1e-6f ? Physics.gravity.normalized : Vector3.down;
            for (int c = 0; c < chainCount; c++)
            {
                int i0 = Idx(c, 0);
                Vector3 root = _target[i0];
                Vector3 dir = _target[Idx(c, jointsPerChain - 1)] - root;
                if (dir.sqrMagnitude < 1e-8f) continue;
                dir.Normalize();
                Vector3 hang = Vector3.Slerp(dir, down, hangFollow);
                Quaternion q = Quaternion.FromToRotation(dir, hang);
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    _target[i] = root + q * (_target[i] - root);
                }
            }
        }

        void Simulate(float dt)
        {
            // 2) 관성: 사슬 뿌리(팔)가 움직인 만큼 (1 - inertia) 비율로 같이 옮김
            for (int c = 0; c < chainCount; c++)
            {
                Vector3 move = (_target[Idx(c, 0)] - _lastRoot[c]) * (1f - inertia);
                _lastRoot[c] = _target[Idx(c, 0)];
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    _pos[i] += move; _prev[i] += move;
                }
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * StepRate), 1, 8);
            float h = dt / steps;
            float w = 2f * Mathf.PI * frequency;
            float drag = Mathf.Clamp01(1f - damping * h);
            Vector3 gravity = Physics.gravity * gravityScale;

            for (int s = 0; s < steps; s++)
            {
                // 3) 적분 (Verlet): 원래 위치로 당기는 스프링 + 중력
                for (int c = 0; c < chainCount; c++)
                {
                    int i0 = Idx(c, 0);
                    _pos[i0] = _prev[i0] = _target[i0];
                    for (int j = 1; j < jointsPerChain; j++)
                    {
                        int i = Idx(c, j);
                        Vector3 acc = (_target[i] - _pos[i]) * (w * w) + gravity;
                        Vector3 vel = (_pos[i] - _prev[i]) * drag;
                        _prev[i] = _pos[i];
                        _pos[i] += vel + acc * (h * h);
                    }
                }

                for (int it = 0; it < Iterations; it++)
                {
                    LinkConstraint();
                    ChainConstraint();
                    KeepOutsideBody();
                }
            }
        }

        // 같은 소매의 이웃 사슬과 간격 유지 (±20%)
        void LinkConstraint()
        {
            float k = linkStiffness * 0.5f;
            for (int c = 0; c < chainCount; c++)
            {
                if (!Linked(c)) continue;
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int a = Idx(c, j), b = Idx(c + 1, j);
                    Vector3 d = _pos[b] - _pos[a];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    float clamped = Mathf.Clamp(len, _linkLen[a] * 0.8f, _linkLen[a] * 1.2f);
                    if (Mathf.Approximately(clamped, len)) continue;
                    Vector3 corr = d * ((len - clamped) / len * k);
                    _pos[a] += corr; _pos[b] -= corr;
                }
            }
        }

        // 마디 길이 고정 + 원래 방향에서 최대 각도 제한 (뿌리부터 아래로)
        void ChainConstraint()
        {
            float maxRad = maxAngle * Mathf.Deg2Rad;
            for (int c = 0; c < chainCount; c++)
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j), p = Idx(c, j - 1);
                    Vector3 restDir = _target[i] - _target[p];
                    Vector3 dir = _pos[i] - _pos[p];
                    if (dir.sqrMagnitude < 1e-8f) dir = restDir;
                    dir = Vector3.RotateTowards(restDir.normalized, dir.normalized, maxRad, 0f);
                    _pos[i] = _pos[p] + dir * _segLen[i];
                }
        }

        // 몸 캡슐 안으로 들어간 마디를 표면 밖으로 꺼냄
        void KeepOutsideBody()
        {
            for (int k = 0; k < colliders.Length; k++)
            {
                var col = colliders[k];
                if (!col || !col.enabled) continue;
                GetCapsule(col, out Vector3 a, out Vector3 b, out float r);
                PushOut(a, b, r + colliderMargin);
            }
            Vector3 fwd = transform.root.forward;
            for (int k = 0; k < bodySegments.Length; k++)
            {
                var s = bodySegments[k];
                if (!s.from || !s.to) continue;
                Vector3 off = fwd * s.forwardOffset;
                PushOut(s.from.position + off, s.to.position + off, s.radius + colliderMargin);
            }
        }

        void PushOut(Vector3 a, Vector3 b, float r)
        {
            Vector3 ab = b - a;
            float abLen2 = Mathf.Max(ab.sqrMagnitude, 1e-8f);
            for (int c = 0; c < chainCount; c++)
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    Vector3 closest = a + ab * Mathf.Clamp01(Vector3.Dot(_pos[i] - a, ab) / abLen2);
                    Vector3 d = _pos[i] - closest;
                    float dist = d.magnitude;
                    if (dist >= r) continue;
                    _pos[i] = dist > 1e-5f ? closest + d * (r / dist) : _pos[i] + (_target[i] - closest).normalized * r;
                }
        }

        // 마디 회전으로 방향을 맞춤
        void ApplyToBones()
        {
            for (int c = 0; c < chainCount; c++)
                for (int j = 0; j < jointsPerChain - 1; j++)
                {
                    Transform bone = joints[Idx(c, j)], child = joints[Idx(c, j + 1)];
                    Vector3 from = child.position - bone.position;
                    Vector3 to = _pos[Idx(c, j + 1)] - bone.position;
                    if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) continue;
                    bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
                }
        }

        static void GetCapsule(CapsuleCollider col, out Vector3 a, out Vector3 b, out float r)
        {
            var t = col.transform;
            Vector3 s = t.lossyScale;
            Vector3 axis = col.direction == 0 ? Vector3.right : col.direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = Mathf.Abs(col.direction == 0 ? s.x : col.direction == 1 ? s.y : s.z);
            float radScale = col.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                           : col.direction == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))
                           : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            r = col.radius * radScale;
            float half = Mathf.Max(0f, col.height * axisScale * 0.5f - r);
            Vector3 center = t.TransformPoint(col.center);
            Vector3 worldAxis = t.TransformDirection(axis).normalized;
            a = center - worldAxis * half;
            b = center + worldAxis * half;
        }

        void OnDrawGizmosSelected()
        {
            if (joints == null || jointsPerChain < 2) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            for (int c = 0; c < chainCount; c++)
                for (int j = 0; j < jointsPerChain - 1; j++)
                {
                    var a = joints[Idx(c, j)]; var b = joints[Idx(c, j + 1)];
                    if (a && b) Gizmos.DrawLine(a.position, b.position);
                }
        }
    }
}
