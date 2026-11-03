using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 납도할 때 칼이 몸(배)을 찌르지 않고 크게 돌아 칼집 방향과 딱 맞게 들어가도록 맞춤.
    ///  1) 크게 돌리기: 오른팔을 몸 왼쪽 바깥으로 크게 빼며(팔 IK) 칼끝을 몸 앞 → 몸 왼쪽 바깥·아래로 돌림
    ///     → 칼날이 배 앞을 가로지르지 않고 몸 옆으로 지나감
    ///  2) 꽂기: 몸 바깥에서 칼집 방향으로 돌려 넣으며 손은 칼집 입구(원래 손 자리)로 돌아옴
    ///  - alignStart → sheathHideAt 를 한 번에 이어 돌림 (중간 열쇠에서 멈칫하지 않게 각도에 비례해 같은 빠르기)
    ///  3) 칼이 바뀌는 순간(PlayerController.sheathHideAt)에는 칼집 속 칼과 자세가 똑같아 이음새 없이 바뀜
    ///  - 손에 든 칼(SheathSwordVisual.drawnSword 아래 모델)과 칼집 속 칼은 같은 메시
    ///  - 납도 중이 아닐 때는 칼 모델의 원래 손 안 자세로 되돌리고 팔은 건드리지 않음
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    [DefaultExecutionOrder(1100)]   // 팔 보정(ArmBodyGuard 1040, SleeveActionGuard 1060) 뒤에
    public class SheathInsertAlign : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;
        public SheathSwordVisual sheath;
        [Tooltip("손에 든 칼 모델 (비우면 drawnSword 아래 첫 렌더러)")]
        public Transform heldModel;

        [Header("구간 (납도 클립 진행 비율)")]
        [Tooltip("이 지점부터 팔을 크게 빼며 칼을 몸 바깥으로 돌리기 시작")]
        [Range(0f, 1f)] public float alignStart = 0.3f;

        [Header("크게 돌리기")]
        [Tooltip("돌리기 시작 직후 칼끝이 먼저 지나가는 쪽 (몸 앞·아래 — 오른쪽에서 왼쪽으로 갈 때 몸 앞을 돌아가게)")]
        public Vector3 frontTipDir = new Vector3(0.15f, -0.75f, 1f);
        [Tooltip("칼끝이 가장 크게 나갈 때 향하는 쪽 (몸 기준: 왼쪽·아래·약간 뒤)")]
        public Vector3 swingTipDir = new Vector3(-1f, -0.55f, -0.15f);
        [Tooltip("팔을 몸 바깥으로 빼는 양 (몸 기준 m: 왼쪽·위·앞)")]
        public Vector3 armReachOut = new Vector3(-0.1f, 0.05f, 0.14f);
        [Tooltip("팔꿈치가 향할 쪽 (몸 기준: 오른쪽·아래)")]
        public Vector3 elbowHint = new Vector3(1f, -0.6f, -0.3f);

        // 칼 메시(Sword) 로컬 좌표의 손잡이 끝 / 칼끝 (메시 바운드 x 끝)
        static readonly Vector3 HiltEndLocal = new Vector3(-0.989f, 0.29f, 0.765f);
        static readonly Vector3 TipEndLocal = new Vector3(0.009f, 0.29f, 0.765f);

        Transform _sheathed, _grip, _upper, _lower, _hand;
        Vector3 _localPos, _gripInModel;
        Quaternion _localRot, _startRotLocal;
        bool _ready, _haveStart;
        float _lastT;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            if (!sheath) sheath = GetComponentInChildren<SheathSwordVisual>(true);
            if (sheath && !heldModel && sheath.drawnSword)
            {
                var r = sheath.drawnSword.GetComponentInChildren<Renderer>(true);
                if (r) heldModel = r.transform;
            }
            if (!player || !player.animator || !sheath || !heldModel || !sheath.sheathedSword) return;
            _sheathed = sheath.sheathedSword.transform;
            _grip = sheath.drawnSword.transform;   // 손뼈에 붙은 칼 기준점 = 손잡이
            _upper = player.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lower = player.animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = player.animator.GetBoneTransform(HumanBodyBones.RightHand);
            _localPos = heldModel.localPosition;
            _localRot = heldModel.localRotation;
            _gripInModel = heldModel.InverseTransformPoint(_grip.position);
            _ready = _upper && _lower && _hand;
        }

        void LateUpdate()
        {
            if (!_ready) return;
            heldModel.localPosition = _localPos;
            heldModel.localRotation = _localRot;
            if (player.CurrentAction != PlayerController.ActionState.Sheath || !heldModel.gameObject.activeInHierarchy) { _haveStart = false; return; }

            var info = player.animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(player.sheathState)) { _haveStart = false; return; }
            ApplyAt(info.normalizedTime);
        }

        /// <summary>납도 클립 진행 비율 t 에서의 팔·칼 맞춤 (애니메이션이 자세를 잡은 뒤 호출). 에디터 미리보기에서도 씀</summary>
        public void ApplyAt(float t)
        {
            if (!_ready) Awake();
            if (!_ready) return;
            heldModel.localPosition = _localPos;
            heldModel.localRotation = _localRot;
            float hideAt = player.sheathHideAt;
            if (t < alignStart || t > hideAt) { _haveStart = false; return; }

            // 시작 방향은 맞추기 시작한 순간의 칼 방향으로 고정 (원래 동작이 그 뒤 칼을 몸 가운데로 휘두르고 앞에서 멈칫하는 것을 따라가지 않게)
            if (!_haveStart || t < _lastT) { _startRotLocal = Quaternion.Inverse(transform.rotation) * heldModel.rotation; _haveStart = true; }
            _lastT = t;
            Quaternion animRot = transform.rotation * _startRotLocal;

            // 칼 방향 열쇠: 시작 방향 → (오른쪽에서 시작하면) 몸 앞 → 몸 왼쪽 바깥 → 칼집 방향
            Quaternion rS = _sheathed.rotation;
            Vector3 sheathTipDir = (_sheathed.TransformPoint(TipEndLocal) - _sheathed.TransformPoint(HiltEndLocal)).normalized;
            Quaternion rSwing = Quaternion.FromToRotation(sheathTipDir, transform.TransformDirection(swingTipDir.normalized)) * rS;
            Vector3 startTipLocal = transform.InverseTransformDirection(animRot * Quaternion.Inverse(rS) * sheathTipDir);
            bool startsLeft = startTipLocal.x < -0.3f;   // 이미 몸 왼쪽이면 몸 앞을 돌 필요 없음
            Quaternion rFront = startsLeft
                ? Quaternion.Slerp(animRot, rSwing, 0.5f)
                : Quaternion.FromToRotation(sheathTipDir, transform.TransformDirection(frontTipDir.normalized)) * rS;

            // 처음부터 끝까지 한 번에 이어 돌림: 전체 진행 u 하나만 끝에서 부드럽게 하고,
            // 열쇠 사이는 돌아가는 각도에 비례해 나눠 같은 빠르기로 지나감 (열쇠마다 멈칫하지 않게)
            float x = Mathf.InverseLerp(alignStart, hideAt, t);
            float u = 1f - (1f - x) * (1f - x);   // 바로 빠르게 시작해 칼집에 들어가며 느려짐 (시작에서 멈칫하지 않게)
            float a1 = Mathf.Max(1f, Quaternion.Angle(animRot, rFront));
            float a2 = Mathf.Max(1f, Quaternion.Angle(rFront, rSwing));
            float a3 = Mathf.Max(1f, Quaternion.Angle(rSwing, rS));
            float sum = a1 + a2 + a3, k1 = a1 / sum, k2 = (a1 + a2) / sum;
            Quaternion rot = u <= k1 ? Quaternion.Slerp(animRot, rFront, u / k1)
                           : u <= k2 ? Quaternion.Slerp(rFront, rSwing, (u - k1) / (k2 - k1))
                           : Quaternion.Slerp(rSwing, rS, (u - k2) / (1f - k2));

            // 팔: 원래 손 자리에서 몸 바깥(왼쪽·앞)으로 뺐다가 칼집 입구로 돌아옴 (칼이 몸 왼쪽 바깥일 때 가장 큼)
            float reach = u <= k2 ? Mathf.Sin(u / k2 * Mathf.PI * 0.5f) : Mathf.Cos((u - k2) / (1f - k2) * Mathf.PI * 0.5f);
            if (reach > 0.001f) SolveArm(_hand.position + transform.TransformVector(armReachOut) * reach);

            // 위치: 손잡이(손) 기준, 바뀌기 직전에는 칼집 속 칼과 정확히 겹치게
            Vector3 lossy = heldModel.lossyScale;
            Vector3 pos = _grip.position - rot * Vector3.Scale(lossy, _gripInModel);
            float snap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hideAt - 0.05f, hideAt, t));
            pos = Vector3.Lerp(pos, _sheathed.position, snap);
            heldModel.SetPositionAndRotation(pos, rot);
        }

        // 오른팔 2관절 IK (위팔·아래팔 회전만 바꿈, 손 회전은 유지)
        void SolveArm(Vector3 goal)
        {
            Quaternion handRot = _hand.rotation;
            Vector3 a = _upper.position, b = _lower.position, c = _hand.position;
            float la = Vector3.Distance(a, b), lb = Vector3.Distance(b, c);
            Vector3 toGoal = goal - a;
            float d = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(la - lb) + 0.001f, (la + lb) * 0.985f);
            Vector3 dir = toGoal.normalized;
            Vector3 hint = transform.TransformDirection(elbowHint.normalized);
            Vector3 bend = Vector3.ProjectOnPlane(hint, dir).normalized;
            if (bend.sqrMagnitude < 1e-4f) bend = Vector3.ProjectOnPlane(b - a, dir).normalized;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float angA = Mathf.Acos(cosA);
            Vector3 elbow = a + (dir * Mathf.Cos(angA) + bend * Mathf.Sin(angA)) * la;
            Vector3 wrist = a + dir * d;
            _upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * _upper.rotation;
            _lower.rotation = Quaternion.FromToRotation(_hand.position - _lower.position, wrist - _lower.position) * _lower.rotation;
            _hand.rotation = handRot;
        }
    }
}
