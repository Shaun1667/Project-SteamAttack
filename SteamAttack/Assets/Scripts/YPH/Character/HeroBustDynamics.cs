using UnityEngine;

namespace YPH
{
    /// <summary>가슴과 의상이 공유하는 보조 본의 형태와 작은 관성 움직임을 제어합니다.</summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(1050)]
    public sealed class HeroBustDynamics : MonoBehaviour
    {
        [Tooltip("가슴에 부착한 배율 1 기준점")]
        [SerializeField] private Transform anchor;
        [Tooltip("좌우 바스트 보조 본")]
        [SerializeField] private Transform[] bones = new Transform[0];
        [Tooltip("기본 외형에서의 보조 본 로컬 위치")]
        [SerializeField] private Vector3[] restPositions = new Vector3[0];
        [Tooltip("크기 변화. 0은 원본, -1~1은 본의 축별 배율을 약 12~20% 축소·확대")]
        [Range(-1, 1)] [SerializeField] private float size;
        [Tooltip("앞쪽 돌출과 위쪽 형태 조절. 0은 원본")]
        [Range(-1, 1)] [SerializeField] private float projection;
        [Tooltip("움직임에 따른 관성 흔들림 사용")]
        [SerializeField] private bool simulateMotion = true;
        [Tooltip("흔들림의 최대 이동 거리(m)")]
        [Range(0, .008f)] [SerializeField] private float maxDisplacement = .004f;
        [Tooltip("원래 위치로 복귀하는 진동수(Hz)")]
        [Range(2, 8)] [SerializeField] private float frequency = 4.5f;
        [Tooltip("잔진동 감쇠 비율")]
        [Range(.3f, 1)] [SerializeField] private float damping = .8f;

        private Vector3 lastPosition, lastVelocity, displacement, velocity;
        private Quaternion lastRotation;
        private bool initialized;

        public Vector3 MotionOffset => displacement;

        public void SetShape(float newSize, float newProjection)
        {
            size = float.IsFinite(newSize) ? Mathf.Clamp(newSize, -1, 1) : 0;
            projection = float.IsFinite(newProjection) ? Mathf.Clamp(newProjection, -1, 1) : 0;
            ApplyShape();
        }

        public void ResetSimulation()
        {
            displacement = velocity = lastVelocity = Vector3.zero;
            initialized = anchor;
            if (anchor) { lastPosition = anchor.position; lastRotation = anchor.rotation; }
            ApplyShape();
        }

        public void Simulate(float deltaTime)
        {
            if (!anchor) return;
            if (!initialized || !float.IsFinite(deltaTime) || deltaTime > .1f ||
                Vector3.Distance(anchor.position, lastPosition) > .5f || Quaternion.Angle(anchor.rotation, lastRotation) > 100)
            { ResetSimulation(); return; }
            if (deltaTime <= 0) return;
            var currentVelocity = (anchor.position - lastPosition) / deltaTime;
            var acceleration = Vector3.ClampMagnitude((currentVelocity - lastVelocity) / deltaTime, 25);
            // shortcut: 가슴 주변 외부 충돌 대신 작은 이동 한도로 제한. 큰 변형이나 다른 의상에는 충돌 보정 필요.
            var force = anchor.InverseTransformDirection(-acceleration) * .3f;
            force.x *= .5f;
            float omega = 2 * Mathf.PI * Mathf.Clamp(frequency, 2, 8);
            int steps = Mathf.CeilToInt(deltaTime * 120);
            float dt = deltaTime / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity += (force - omega * omega * displacement - 2 * Mathf.Clamp(damping, .3f, 1) * omega * velocity) * dt;
                displacement += velocity * dt;
                float limit = Mathf.Clamp(maxDisplacement, 0, .008f);
                if (displacement.sqrMagnitude > limit * limit)
                {
                    displacement = Vector3.ClampMagnitude(displacement, limit);
                    if (Vector3.Dot(velocity, displacement) > 0) velocity = Vector3.ProjectOnPlane(velocity, displacement.normalized);
                }
            }
            lastPosition = anchor.position; lastRotation = anchor.rotation; lastVelocity = currentVelocity;
            ApplyShape();
        }

        private void ApplyShape()
        {
            if (!anchor || bones.Length != restPositions.Length) return;
            float s = Mathf.Clamp(size, -1, 1), p = Mathf.Clamp(projection, -1, 1);
            var offset = new Vector3(0, p * .003f, p * .008f) + displacement;
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i]) continue;
                bones[i].localPosition = restPositions[i] + offset;
                bones[i].localRotation = Quaternion.identity;
                bones[i].localScale = new Vector3(1 + s * .15f, 1 + s * .12f, 1 + s * .2f);
            }
        }

        private void LateUpdate()
        {
            if (Application.IsPlaying(gameObject) && simulateMotion) Simulate(Time.deltaTime);
            else ResetSimulation();
        }

        private void OnEnable() => ResetSimulation();

        private void OnDisable()
        {
            initialized = false;
            displacement = velocity = Vector3.zero;
            // 컴포넌트를 끄면 크기 보정도 해제하여 메시의 기준 자세로 돌아간다.
            for (int i = 0; i < bones.Length && i < restPositions.Length; i++)
                if (bones[i]) { bones[i].localPosition = restPositions[i]; bones[i].localRotation = Quaternion.identity; bones[i].localScale = Vector3.one; }
        }
    }
}

