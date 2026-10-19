using System;
using UnityEngine;

namespace YPH
{
    /// <summary>장신구 전용 본에 관성과 복원력을 적용합니다. 몸 애니메이션 뒤에 실행합니다.</summary>
    [DefaultExecutionOrder(1100)]
    public sealed class AccessorySpringBone : MonoBehaviour
    {
        [Serializable]
        private struct Joint
        {
            public Transform bone;
            public Vector3 localTip;
            public Quaternion restRotation;
        }

        [Tooltip("부모부터 자식 순서로 연결한 장신구 전용 본")]
        [SerializeField] private Joint[] joints = Array.Empty<Joint>();
        [Tooltip("장식이 통과하지 않을 머리 등의 구체. 게임 판정과 분리된 Trigger 사용")]
        [SerializeField] private SphereCollider[] colliders = Array.Empty<SphereCollider>();
        [Tooltip("복원 진동수(Hz). 높을수록 빨리 원래 방향으로 돌아옵니다.")]
        [SerializeField, Range(1f, 12f)] private float frequency = 4f;
        [Tooltip("진동 감쇠 비율. 높을수록 잔흔들림이 빨리 멈춥니다.")]
        [SerializeField, Range(.1f, 2f)] private float damping = .65f;
        [Tooltip("기준 방향에서 허용하는 본 회전 각도")]
        [SerializeField, Range(1f, 45f)] private float maxAngle = 28f;
        [Tooltip("중력의 적용 비율. 부착 위치는 움직이지 않습니다.")]
        [SerializeField, Range(0f, 1f)] private float gravityScale = .08f;
        [Tooltip("이 거리 이상 순간이동하면 시뮬레이션을 초기화합니다(m).")]
        [SerializeField, Min(.05f)] private float teleportDistance = .5f;

        private Vector3[] tips;
        private Vector3[] velocities;
        private Vector3 lastPosition;
        private Quaternion lastRotation;

        public void ResetSimulation()
        {
            if (tips == null || tips.Length != joints.Length)
            {
                tips = new Vector3[joints.Length];
                velocities = new Vector3[joints.Length];
            }
            foreach (var joint in joints)
                if (joint.bone) joint.bone.localRotation = joint.restRotation;
            for (int i = 0; i < joints.Length; i++)
            {
                tips[i] = joints[i].bone ? joints[i].bone.TransformPoint(joints[i].localTip) : Vector3.zero;
                velocities[i] = Vector3.zero;
            }
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }

        public void Simulate(float deltaTime)
        {
            if (deltaTime <= 0) return;
            if (tips == null || tips.Length != joints.Length || !float.IsFinite(deltaTime) || deltaTime > .1f ||
                Vector3.Distance(lastPosition, transform.position) > teleportDistance ||
                Quaternion.Angle(lastRotation, transform.rotation) > 100f)
            {
                ResetSimulation();
                return;
            }
            lastPosition = transform.position;
            lastRotation = transform.rotation;
            int steps = Mathf.CeilToInt(deltaTime * 120f);
            float dt = deltaTime / steps;
            float omega = 2f * Mathf.PI * frequency;

            for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (!joint.bone) continue;
                joint.bone.localRotation = joint.restRotation;
                Vector3 anchor = joint.bone.position;
                Vector3 desired = joint.bone.TransformPoint(joint.localTip);
                Vector3 restDirection = desired - anchor;
                float length = restDirection.magnitude;
                if (length < .0001f) continue;
                restDirection /= length;
                for (int step = 0; step < steps; step++)
                {
                    velocities[i] += ((desired - tips[i]) * (omega * omega) - velocities[i] * (2f * damping * omega)
                        + Physics.gravity * gravityScale) * dt;
                    tips[i] += velocities[i] * dt;
                    foreach (var sphere in colliders)
                    {
                        if (!sphere || !sphere.enabled) continue;
                        Vector3 offset = tips[i] - sphere.transform.TransformPoint(sphere.center);
                        var scale = sphere.transform.lossyScale;
                        float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) + .002f;
                        float distance = offset.magnitude;
                        if (distance < radius && distance > .00001f) tips[i] += offset * ((radius - distance) / distance);
                    }
                    Vector3 direction = tips[i] - anchor;
                    direction = direction.sqrMagnitude > 1e-10f ? direction.normalized : restDirection;
                    direction = Vector3.RotateTowards(restDirection, direction, maxAngle * Mathf.Deg2Rad, 0);
                    tips[i] = anchor + direction * length;
                    velocities[i] = Vector3.ProjectOnPlane(velocities[i], direction);
                }
                joint.bone.rotation = Quaternion.FromToRotation(restDirection, (tips[i] - anchor).normalized) * joint.bone.rotation;
            }
        }

        private void LateUpdate() => Simulate(Time.deltaTime);

        private void OnDisable()
        {
            foreach (var joint in joints)
                if (joint.bone) joint.bone.localRotation = joint.restRotation;
            tips = null;
            velocities = null;
        }
    }
}
