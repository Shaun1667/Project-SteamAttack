using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 치마 전용 물리. 치마 둘레에 세로로 심은 뼈 사슬(Tools/NGH/Hero/5. Build Skirt Physics Bones 로 생성)을 움직입니다.
    ///  - 각 마디는 기준 위치로 돌아가려는 스프링 + 감쇠 + 중력으로 움직임
    ///  - 캐릭터가 이동·회전하면 관성만큼 뒤에 남았다가 따라옴
    ///  - 골반의 좌우 기울기·비틀림은 일부만 따라가서 달릴 때 아랫단이 지렛대처럼 휘둘리지 않음
    ///  - 기준 위치가 다리(허벅지·정강이 캡슐)를 감싸도록 넓어짐: 무릎이 나오면 그 아래 치마도 무릎 폭을 유지(텐트처럼 늘어짐)
    ///    아랫단 높이는 그대로 두고 길이·둘레가 늘어나서 크게 벌린 다리도 덮음
    ///    발(발목~발끝)도 덮고, 사슬과 사슬 사이로 들어온 다리는 양쪽 사슬을 같이 넓힘
    ///    흔들림이나 마디 길이 제한 때문에 덜 벌어졌으면 마지막에 다리를 덮는 반지름까지 강제로 늘림
    ///  - 옆 사슬과의 간격을 유지해서 치마가 갈라지거나 뭉치지 않음
    /// 애니메이션과 GroundClamp(1000) 뒤에 실행합니다.
    /// </summary>
    [DefaultExecutionOrder(1050)]
    public class SkirtPhysics : MonoBehaviour
    {
        [Header("뼈 (빌더가 채움)")]
        [Tooltip("사슬 순서대로 나열한 마디. 사슬 c의 j번째 마디 = joints[c * jointsPerChain + j]. j = 0은 허리(엉덩이에 고정)")]
        public Transform[] joints = new Transform[0];
        public int chainCount;
        public int jointsPerChain;
        [Tooltip("치마가 덮을 다리 캡슐 (게임 판정과 상관없는 콜라이더)")]
        public CapsuleCollider[] colliders = new CapsuleCollider[0];

        [Header("움직임")]
        [Tooltip("원래 모양으로 돌아오는 빠르기(Hz). 높을수록 빳빳하고, 낮을수록 하늘하늘")]
        [Range(0.5f, 8f)] public float frequency = 2.5f;
        [Tooltip("허리 쪽 마디가 아랫단보다 몇 배 빳빳한지")]
        [Range(1f, 4f)] public float waistStiffness = 2f;
        [Tooltip("흔들림이 멈추는 빠르기 (1/초). 높을수록 출렁임이 빨리 가라앉음")]
        [Range(0f, 20f)] public float damping = 7f;
        [Tooltip("캐릭터가 움직일 때 치마가 뒤에 남는 정도. 0 = 몸에 붙어 같이 움직임, 1 = 제자리에 남았다가 끌려옴")]
        [Range(0f, 1f)] public float inertia = 0.3f;
        [Tooltip("골반의 좌우 기울기·비틀림을 치마 아랫단이 따라가는 비율. 허리선은 항상 골반에 붙어 있음. 앞뒤 숙임은 그대로 따라감\n0 = 몸 방향 기준으로 차분하게, 1 = 골반에 단단히 붙어 같이 휘둘림")]
        [Range(0f, 1f)] public float hipSwayFollow = 0.3f;
        [Tooltip("골반의 앞뒤 숙임을 치마 아랫단이 따라가는 비율. 낮을수록 몸을 숙여도 치마가 아래로 늘어짐")]
        [Range(0f, 1f)] public float hipPitchFollow = 0.5f;
        [Tooltip("중력 비율")]
        [Range(0f, 1f)] public float gravityScale = 0.25f;
        [Tooltip("각 마디가 기준 방향에서 벌어질 수 있는 최대 각도")]
        [Range(5f, 80f)] public float maxAngle = 40f;
        [Tooltip("옆 사슬과의 간격 유지 강도. 높을수록 치마가 갈라지거나 뭉치지 않음")]
        [Range(0f, 1f)] public float ringStiffness = 0.6f;

        [Header("다리 덮기")]
        [Tooltip("다리 표면에서 치마를 띄울 거리 (m)")]
        public float legMargin = 0.03f;
        [Tooltip("다리를 덮느라 치마 길이·둘레가 늘어날 수 있는 최대 배율")]
        [Range(1f, 3f)] public float maxStretch = 2.5f;
        [Tooltip("발(발목~발끝)도 치마가 덮을 대상에 넣음. 뒤로 차올린 발이 치마를 뚫지 않게 함")]
        public bool coverFeet = true;
        [Tooltip("발 두께 반지름 (m)")]
        public float footRadius = 0.05f;
        [Tooltip("이 거리 이상 순간이동하면 시뮬레이션을 초기화 (m)")]
        public float teleportDistance = 0.7f;

        const float StepRate = 120f;
        const int Iterations = 2;
        const int LegSamples = 8;   // 캡슐 하나당 축 위 샘플 수

        Vector3[] _pos, _prev, _target;
        Quaternion[] _restLocalRot;
        Vector3[] _restLocalPos;
        float[] _segLen, _ringLen, _stiffMul;
        Vector3[] _lastAnchor;
        Vector3[] _restOffset;          // 허리 마디에서 각 마디까지 (골반 회전 기준, m)
        Transform _hips, _root;
        Quaternion _hipsRestInRoot;     // 기준 자세에서 몸(Animator) 기준 골반 회전
        Vector3[] _legPt;               // 다리 샘플 (월드)
        float[] _legR;
        int _legCount;
        float[] _minRadius;             // 다리를 덮는 데 필요한 마디의 최소 반지름 (사슬 방향, 수평). 0 = 제한 없음
        Vector3[] _chainDir;            // 사슬 방향 (허리 마디 기준, 수평 단위벡터)
        Transform[,] _feet;             // [왼/오, 발목/발끝]
        bool _ready;

        int Idx(int c, int j) => c * jointsPerChain + j;

        void Awake()
        {
            int n = chainCount * jointsPerChain;
            if (n == 0 || joints.Length != n) { enabled = false; return; }
            _pos = new Vector3[n]; _prev = new Vector3[n]; _target = new Vector3[n];
            _restLocalRot = new Quaternion[n]; _restLocalPos = new Vector3[n];
            _segLen = new float[n]; _ringLen = new float[n]; _stiffMul = new float[n];
            _lastAnchor = new Vector3[chainCount];
            _restOffset = new Vector3[n];
            _minRadius = new float[n];
            _chainDir = new Vector3[chainCount];
            _hips = joints[0].parent;
            var animator = GetComponentInParent<Animator>();
            _root = animator ? animator.transform : transform.parent;
            if (animator && animator.isHuman)
            {
                _feet = new Transform[2, 2];
                _feet[0, 0] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _feet[0, 1] = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                _feet[1, 0] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                _feet[1, 1] = animator.GetBoneTransform(HumanBodyBones.RightToes);
            }
            _legPt = new Vector3[(colliders.Length + 2) * LegSamples];
            _legR = new float[_legPt.Length];
            _hipsRestInRoot = Quaternion.Inverse(_root.rotation) * _hips.rotation;
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
                    _ringLen[i] = Vector3.Distance(joints[i].position, joints[Idx((c + 1) % chainCount, j)].position);
                    _restOffset[i] = Quaternion.Inverse(_hips.rotation) * (joints[i].position - joints[Idx(c, 0)].position);
                    float t = jointsPerChain > 1 ? j / (float)(jointsPerChain - 1) : 1f;
                    _stiffMul[i] = Mathf.Lerp(waistStiffness, 1f, t);
                }
        }

        void OnEnable() => _ready = false;

        void ResetToTargets()
        {
            for (int i = 0; i < _pos.Length; i++) _pos[i] = _prev[i] = _target[i];
            for (int c = 0; c < chainCount; c++) _lastAnchor[c] = _target[Idx(c, 0)];
            _ready = true;
        }

        void LateUpdate()
        {
            if (_pos == null) return;

            // 1) 애니메이션 기준 자세: 마디 회전·위치를 원래대로 돌려놓고 위치를 읽음
            for (int i = 0; i < joints.Length; i++) joints[i].SetLocalPositionAndRotation(_restLocalPos[i], _restLocalRot[i]);
            for (int i = 0; i < joints.Length; i++) _target[i] = joints[i].position;
            StabilizeTargets();
            WrapLegs();

            float dt = Time.deltaTime;
            bool teleported = false;
            if (_ready)
                for (int c = 0; c < chainCount && !teleported; c++)
                    teleported = (_target[Idx(c, 0)] - _lastAnchor[c]).sqrMagnitude > teleportDistance * teleportDistance;
            if (!_ready || teleported || dt > 0.1f) ResetToTargets();
            else if (dt > 0f) Simulate(dt);

            ApplyToBones();
        }

        // 허리 아래 마디의 기준 위치: 골반의 앞뒤 숙임은 hipPitchFollow, 좌우 기울기·비틀림은 hipSwayFollow 만큼만 따라감
        void StabilizeTargets()
        {
            if ((hipSwayFollow >= 1f && hipPitchFollow >= 1f) || !_hips || !_root) return;
            Quaternion stable = _root.rotation * _hipsRestInRoot;
            Vector3 e = (Quaternion.Inverse(stable) * _hips.rotation).eulerAngles;
            e = new Vector3(Mathf.DeltaAngle(0f, e.x) * hipPitchFollow, Mathf.DeltaAngle(0f, e.y) * hipSwayFollow, Mathf.DeltaAngle(0f, e.z) * hipSwayFollow);
            Quaternion frame = stable * Quaternion.Euler(e);
            for (int c = 0; c < chainCount; c++)
            {
                Vector3 waist = _target[Idx(c, 0)];
                for (int j = 1; j < jointsPerChain; j++)
                    _target[Idx(c, j)] = waist + frame * _restOffset[Idx(c, j)];
            }
        }

        // 기준 위치를 다리 바깥으로 넓힘. 사슬 방향으로 본 다리(허벅지·무릎·정강이)의 바깥 끝보다 legMargin 만큼 바깥에 오게 하고,
        // 위쪽에서 다리가 튀어나온 만큼은 그 아래 마디도 유지 (무릎에서 아래로 떨어지는 텐트 모양). 높이는 그대로
        void WrapLegs()
        {
            System.Array.Clear(_minRadius, 0, _minRadius.Length);
            _legCount = 0;
            for (int k = 0; k < colliders.Length; k++)
            {
                var col = colliders[k];
                if (!col || !col.enabled) continue;
                GetCapsule(col, out Vector3 a, out Vector3 b, out float r);
                AddLegSamples(a, b, r);
            }
            if (coverFeet && _feet != null)
                for (int side = 0; side < 2; side++)
                {
                    Transform ankle = _feet[side, 0], toe = _feet[side, 1];
                    if (!ankle) continue;
                    Vector3 tip = toe ? toe.position : ankle.position + ankle.forward * 0.15f;
                    AddLegSamples(ankle.position, tip, footRadius);
                }
            if (_legCount == 0) return;

            Vector3 up = _root.up;
            Vector3 center = _hips.position;
            // 사슬 방향은 허리 마디 기준 (몸을 숙여 아래 마디가 골반 뒤로 가도 앞쪽 사슬은 앞쪽)
            for (int c = 0; c < chainCount; c++)
                _chainDir[c] = Vector3.ProjectOnPlane(_target[Idx(c, 0)] - center, up).normalized;

            for (int c = 0; c < chainCount; c++)
            {
                Vector3 dir = _chainDir[c];
                if (dir.sqrMagnitude < 0.5f) continue;
                Vector3 prevDir = _chainDir[(c + chainCount - 1) % chainCount], nextDir = _chainDir[(c + 1) % chainCount];
                float waistH = Vector3.Dot(_target[Idx(c, 0)] - center, up);
                float carry = 0f;   // 위 마디에서 이어 내려오는 필요한 반지름
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    Vector3 rel = _target[i] - center;
                    float h = Vector3.Dot(rel, up);
                    float rest = Vector3.Dot(rel - up * h, dir);
                    float need = carry;
                    for (int s = 0; s < _legCount; s++)
                    {
                        Vector3 lp = _legPt[s] - center;
                        float lh = Vector3.Dot(lp, up);
                        if (lh < h - 0.05f) continue;                 // 이 마디보다 아래쪽 다리는 아래 마디가 담당
                        if (lh > waistH) continue;                    // 허리보다 높이 든 다리(구르기 등)는 치마로 덮을 수 없음
                        Vector3 lv = lp - up * lh;
                        float lr = _legR[s];
                        // 양옆 사슬 사이에 들어온 다리: 두 사슬을 잇는 천(직선)이 다리 바깥을 지나도록 두 사슬 모두 넓힘
                        float wedge = Mathf.Max(Wedge(prevDir, dir, lv, up), Wedge(dir, nextDir, lv, up));
                        if (wedge > 0f) need = Mathf.Max(need, (lv.magnitude + lr) / wedge);
                        // 이 사슬 방향에 걸치는 다리 (사슬 바깥쪽 먼 곳)
                        float along = Vector3.Dot(lv, dir);
                        float lat = (lv - dir * along).magnitude;
                        if (lat >= lr || along <= 0f) continue;
                        need = Mathf.Max(need, along + Mathf.Sqrt(lr * lr - lat * lat));
                    }
                    carry = need;
                    if (need > rest)
                    {
                        _target[i] += dir * (need - rest);
                        _minRadius[i] = need;
                    }
                }
            }
        }

        void AddLegSamples(Vector3 a, Vector3 b, float r)
        {
            for (int s = 0; s < LegSamples && _legCount < _legPt.Length; s++)
            {
                _legPt[_legCount] = Vector3.Lerp(a, b, s / (LegSamples - 1f));
                _legR[_legCount] = r + legMargin;
                _legCount++;
            }
        }

        // v가 a와 b 방향 사이(부채꼴 안)에 있으면 cos(반각)을, 아니면 0을 돌려줌
        static float Wedge(Vector3 a, Vector3 b, Vector3 v, Vector3 up)
        {
            float span = Vector3.Dot(Vector3.Cross(a, b), up);
            float s = span >= 0f ? 1f : -1f;
            if (s * Vector3.Dot(Vector3.Cross(a, v), up) < 0f || s * Vector3.Dot(Vector3.Cross(v, b), up) < 0f) return 0f;
            return Mathf.Max(0.5f, Mathf.Sqrt(Mathf.Max(0f, (1f + Vector3.Dot(a, b)) * 0.5f)));
        }

        // 흔들림·마디 길이 제한 뒤에도 다리를 덮도록, 다리가 필요로 하는 반지름보다 안쪽에 있는 마디를 바깥으로 꺼냄 (치마가 늘어남)
        void EnforceMinRadius()
        {
            Vector3 center = _hips.position;
            for (int c = 0; c < chainCount; c++)
            {
                Vector3 dir = _chainDir[c];
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    if (_minRadius[i] <= 0f) continue;
                    float r = Vector3.Dot(_pos[i] - center, dir);
                    if (r < _minRadius[i]) _pos[i] += dir * (_minRadius[i] - r);
                }
            }
        }

        void Simulate(float dt)
        {
            // 2) 관성: 허리가 움직인 만큼 (1 - inertia) 비율로 치마도 같이 옮김 (속도에는 영향 없음)
            for (int c = 0; c < chainCount; c++)
            {
                Vector3 move = (_target[Idx(c, 0)] - _lastAnchor[c]) * (1f - inertia);
                _lastAnchor[c] = _target[Idx(c, 0)];
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    _pos[i] += move; _prev[i] += move;
                }
            }

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * StepRate), 1, 8);
            float h = dt / steps;
            float omega = 2f * Mathf.PI * frequency;
            float drag = Mathf.Clamp01(1f - damping * h);
            Vector3 gravity = Physics.gravity * gravityScale;

            for (int s = 0; s < steps; s++)
            {
                // 3) 적분 (Verlet): 기준 위치로 당기는 스프링 + 중력
                for (int c = 0; c < chainCount; c++)
                {
                    int i0 = Idx(c, 0);
                    _pos[i0] = _prev[i0] = _target[i0];
                    for (int j = 1; j < jointsPerChain; j++)
                    {
                        int i = Idx(c, j);
                        float w = omega * _stiffMul[i];
                        Vector3 acc = (_target[i] - _pos[i]) * (w * w) + gravity;
                        Vector3 vel = (_pos[i] - _prev[i]) * drag;
                        _prev[i] = _pos[i];
                        _pos[i] += vel + acc * (h * h);
                    }
                }

                for (int it = 0; it < Iterations; it++)
                {
                    RingConstraint();
                    ChainConstraint();
                    KeepOutsideLegs();
                }
                EnforceMinRadius();
            }
        }

        // 옆 사슬과의 간격 유지 (기준 위치가 다리 때문에 넓어졌으면 그 간격까지 늘어남)
        void RingConstraint()
        {
            float k = ringStiffness * 0.5f;
            for (int j = 1; j < jointsPerChain; j++)
                for (int c = 0; c < chainCount; c++)
                {
                    int a = Idx(c, j), b = Idx((c + 1) % chainCount, j);
                    Vector3 d = _pos[b] - _pos[a];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    float rest = Mathf.Clamp(Vector3.Distance(_target[a], _target[b]), _ringLen[a], _ringLen[a] * maxStretch);
                    float clamped = Mathf.Clamp(len, rest * 0.8f, rest * 1.2f);
                    if (Mathf.Approximately(clamped, len)) continue;
                    Vector3 corr = d * ((len - clamped) / len * k);
                    _pos[a] += corr; _pos[b] -= corr;
                }
        }

        // 마디 길이 + 기준 방향에서 최대 각도 제한 (허리부터 아래로). 길이는 기준 위치가 늘어난 만큼 늘어남
        void ChainConstraint()
        {
            float maxRad = maxAngle * Mathf.Deg2Rad;
            for (int c = 0; c < chainCount; c++)
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j), p = Idx(c, j - 1);
                    Vector3 restDir = _target[i] - _target[p];
                    float restLen = Mathf.Clamp(restDir.magnitude, _segLen[i], _segLen[i] * maxStretch);
                    Vector3 dir = _pos[i] - _pos[p];
                    if (dir.sqrMagnitude < 1e-8f) dir = restDir;
                    dir = Vector3.RotateTowards(restDir.normalized, dir.normalized, maxRad, 0f);
                    _pos[i] = _pos[p] + dir * restLen;
                }
        }

        // 흔들리다가 다리 안으로 들어간 마디는 치마 중심에서 바깥쪽(수평)으로 꺼냄
        void KeepOutsideLegs()
        {
            if (_legCount == 0) return;
            Vector3 up = _root.up;
            Vector3 center = _hips.position;
            for (int c = 0; c < chainCount; c++)
            {
                // 바깥 방향 = 이 사슬의 허리 마디 방향 (수평)
                Vector3 outward = Vector3.ProjectOnPlane(_target[Idx(c, 0)] - center, up);
                if (outward.sqrMagnitude < 1e-8f) continue;
                outward.Normalize();
                for (int j = 1; j < jointsPerChain; j++)
                {
                    int i = Idx(c, j);
                    for (int s = 0; s < _legCount; s++)
                    {
                        Vector3 d = _pos[i] - _legPt[s];
                        float lr = _legR[s] * 0.8f;
                        if (d.sqrMagnitude >= lr * lr) continue;
                        // 다리 샘플 원을 수평 바깥쪽으로 빠져나갈 때까지
                        float along = Vector3.Dot(d, outward);
                        float rest2 = d.sqrMagnitude - along * along;
                        float exit = Mathf.Sqrt(Mathf.Max(0f, lr * lr - rest2));
                        _pos[i] += outward * (exit - along);
                    }
                }
            }
        }

        // 마디 회전으로 방향을 맞추고, 늘어난 만큼 아래 마디 위치도 옮김
        void ApplyToBones()
        {
            for (int c = 0; c < chainCount; c++)
                for (int j = 0; j < jointsPerChain - 1; j++)
                {
                    Transform bone = joints[Idx(c, j)], child = joints[Idx(c, j + 1)];
                    Vector3 target = _pos[Idx(c, j + 1)];
                    Vector3 from = child.position - bone.position;
                    Vector3 to = target - bone.position;
                    if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) continue;
                    bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
                    child.position = target;
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
            Gizmos.color = new Color(1f, 0.6f, 0.2f);
            for (int c = 0; c < chainCount; c++)
                for (int j = 0; j < jointsPerChain - 1; j++)
                {
                    var a = joints[Idx(c, j)]; var b = joints[Idx(c, j + 1)];
                    if (a && b) Gizmos.DrawLine(a.position, b.position);
                }
        }
    }
}
