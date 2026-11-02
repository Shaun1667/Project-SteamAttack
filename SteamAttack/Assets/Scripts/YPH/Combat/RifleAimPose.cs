using System.Collections.Generic;
using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 조준 중 상체·시선·총열을 같은 목표로 맞춥니다.
    /// RifleGripIK가 적용·복원 순서를 관리해 총의 오른손 부모 연결과 양손 잡기를 유지합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RifleAimPose : MonoBehaviour
    {
        [SerializeField, Tooltip("실제 총열 방향인 총구입니다. 같은 총 프리팹 아래의 Muzzle을 연결합니다.")]
        private Transform _muzzle;
        [SerializeField, Range(0f, 1f), Tooltip("원래 총열과 조준 방향의 좌우 차이 중 상체가 나눠 돌 비율입니다. 나머지는 양팔로 맞춥니다.")]
        private float _torsoYawShare = 0.65f;
        [SerializeField, Range(0f, 1f), Tooltip("상하 조준 각도 중 상체가 기울어질 비율입니다. 머리와 총열은 남은 각도까지 조준점을 향합니다.")]
        private float _torsoPitchShare = 0.45f;
        [SerializeField, Range(0f, 60f), Tooltip("상체만 좌우로 비틀 수 있는 최대 각도입니다. 기본 45도입니다.")]
        private float _maxTorsoYaw = 45f;
        [SerializeField, Range(0f, 45f), Tooltip("상체가 위아래로 기울 수 있는 최대 각도입니다. 기본 30도입니다.")]
        private float _maxTorsoPitch = 30f;

        private readonly Transform[] _spine = new Transform[3];
        private readonly Quaternion[] _spineBefore = new Quaternion[3];
        private RifleMotion _motion;
        private Transform _body;
        private Transform _hips;
        private Transform _neck;
        private Transform _head;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Vector3 _headForward = Vector3.forward;
        private Vector3 _headUp = Vector3.up;
        private Quaternion _neckBefore;
        private Quaternion _headBefore;
        private Vector3 _weaponBefore;
        private Quaternion _weaponRotationBefore;
        private Vector3 _weaponAimPoint;
        private bool _hasPose;

        public float CurrentWeight { get; private set; }
        public Vector3 AimPoint { get; private set; }

        /// <summary>장착 때만 관절과 Avatar 기준 축을 읽습니다. 재활성화 중인 자세를 기준 자세로 오인하지 않습니다.</summary>
        internal void Bind(Animator animator, Transform body, RifleMotion motion)
        {
            _body = body;
            _motion = motion;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
            _spine[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
            _spine[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (_head != null)
            {
                CacheHeadAxes(animator);
            }
        }

        /// <summary>공유 클립을 바꾸지 않고 현재 프레임의 조준 자세만 보정합니다. 양손 IK보다 먼저 호출합니다.</summary>
        internal void ApplyPose(float gripWeight, Transform primaryGrip)
        {
            if (_motion == null || _muzzle == null || !_muzzle.IsChildOf(transform)
                || !_motion.TryGetAimPose(out Vector3 point, out Vector3 direction, out float aimWeight))
            {
                return;
            }
            float weight = Mathf.Clamp01(gripWeight * aimWeight);
            if (weight <= 0f)
            {
                return;
            }
            _weaponBefore = transform.localPosition;
            _weaponRotationBefore = transform.localRotation;
            for (int i = 0; i < _spine.Length; i++)
            {
                if (_spine[i] != null)
                {
                    _spineBefore[i] = _spine[i].localRotation;
                }
            }
            if (_neck != null)
            {
                _neckBefore = _neck.localRotation;
            }
            if (_head != null)
            {
                _headBefore = _head.localRotation;
            }
            _hasPose = true;
            CurrentWeight = weight;
            ApplyTorso(direction.normalized, weight);

            Vector3 pivot = primaryGrip.position;
            Vector3 toPoint = point - pivot;
            float minimum = Mathf.Max(1f, Vector3.Distance(pivot, _muzzle.position) + 0.2f);
            // 총구보다 뒤나 너무 가까운 벽을 향해 총이 몸 뒤로 뒤집히지 않게 합니다.
            if (toPoint.magnitude < minimum || Vector3.Dot(toPoint, direction) <= 0f)
            {
                point = pivot + direction.normalized * Mathf.Max(2f, minimum);
            }
            AimPoint = point;
            AlignWeapon(pivot, point, weight);
            // 전환 중에는 섞인 총열 방향 자체를 목표로 삼아 마지막 프레임에서 자세가 갑자기 바뀌지 않게 합니다.
            _weaponAimPoint = _muzzle.position + _muzzle.forward * Vector3.Distance(_muzzle.position, point);
            ApplyLook(point, weight);
        }

        private void ApplyTorso(Vector3 direction, float weight)
        {
            if (_hips == null || _leftShoulder == null || _rightShoulder == null || _spine[0] == null)
            {
                return;
            }
            Transform chest = _spine[2] != null ? _spine[2] : (_spine[1] != null ? _spine[1] : _spine[0]);
            Vector3 forward = Vector3.Cross(_rightShoulder.position - _leftShoulder.position, chest.position - _hips.position).normalized;
            if (Vector3.Dot(forward, _body.forward) < 0f)
            {
                forward = -forward;
            }
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            float currentPitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float targetPitch = Mathf.Clamp(pitch * _torsoPitchShare, -_maxTorsoPitch, _maxTorsoPitch);
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(_muzzle.forward, Vector3.up),
                Vector3.ProjectOnPlane(direction, Vector3.up), Vector3.up);
            yaw = Mathf.Clamp(yaw * _torsoYawShare, -_maxTorsoYaw, _maxTorsoYaw);
            Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up)
                * Quaternion.AngleAxis(-(targetPitch - currentPitch), _body.right);
            float sum = 0f;
            for (int i = 0; i < _spine.Length; i++)
            {
                if (_spine[i] != null)
                {
                    sum += i + 1f;
                }
            }
            for (int i = 0; i < _spine.Length; i++)
            {
                if (_spine[i] != null)
                {
                    _spine[i].rotation = Quaternion.Slerp(Quaternion.identity, turn, weight * (i + 1f) / sum) * _spine[i].rotation;
                }
            }
        }

        /// <summary>손목 목표를 축으로 돌립니다. 총구의 옆 치우침까지 계산해 총열 선이 조준점을 지나게 합니다.</summary>
        private void AlignWeapon(Vector3 pivot, Vector3 point, float weight)
        {
            Vector3 offset = _muzzle.position - pivot;
            Vector3 side = Vector3.ProjectOnPlane(offset, _muzzle.forward);
            Vector3 target = point - pivot;
            float along = Mathf.Sqrt(Mathf.Max(0f, target.sqrMagnitude - side.sqrMagnitude));
            Quaternion turn = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(side + _muzzle.forward * along, target), weight);
            transform.SetPositionAndRotation(pivot + turn * (transform.position - pivot), turn * transform.rotation);
        }

        private void ApplyLook(Vector3 point, float weight)
        {
            if (_head == null || (point - _head.position).sqrMagnitude < 0.0001f)
            {
                return;
            }
            Quaternion target = Quaternion.LookRotation(point - _head.position, _body.up)
                * Quaternion.Inverse(Quaternion.LookRotation(_headForward, _headUp));
            Quaternion original = _head.rotation;
            Quaternion turn = target * Quaternion.Inverse(original);
            if (_neck != null)
            {
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight * 0.3f) * _neck.rotation;
            }
            _head.rotation = Quaternion.Slerp(original, target, weight);
        }

        /// <summary>양팔 도달 보정이 총을 옮긴 뒤 총열을 다시 같은 목표에 맞춥니다.</summary>
        internal void AlignWeaponToAim(Transform primaryGrip)
        {
            if (_hasPose && CurrentWeight > 0f)
            {
                AlignWeapon(primaryGrip.position, _weaponAimPoint, 1f);
            }
        }

        /// <summary>고개 축은 현재 애니메이션 대신 Avatar의 기준 자세로 계산합니다.</summary>
        private void CacheHeadAxes(Animator animator)
        {
            var chain = new List<Transform>();
            for (Transform bone = _head; bone != null && bone != animator.transform; bone = bone.parent)
            {
                chain.Add(bone);
            }
            SkeletonBone[] skeleton = animator.avatar.humanDescription.skeleton;
            Quaternion rest = Quaternion.identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Quaternion local = chain[i].localRotation;
                for (int j = 0; j < skeleton.Length; j++)
                {
                    if (skeleton[j].name == chain[i].name)
                    {
                        local = skeleton[j].rotation;
                        break;
                    }
                }
                rest *= local;
            }
            _headForward = Quaternion.Inverse(rest) * Vector3.forward;
            _headUp = Quaternion.Inverse(rest) * Vector3.up;
        }

        /// <summary>양팔·총의 IK를 먼저 복원한 뒤 호출해 원본 상체·시선·총 연결로 되돌립니다.</summary>
        internal void RestorePose()
        {
            if (_hasPose)
            {
                for (int i = 0; i < _spine.Length; i++)
                {
                    if (_spine[i] != null)
                    {
                        _spine[i].localRotation = _spineBefore[i];
                    }
                }
                if (_neck != null)
                {
                    _neck.localRotation = _neckBefore;
                }
                if (_head != null)
                {
                    _head.localRotation = _headBefore;
                }
                transform.SetLocalPositionAndRotation(_weaponBefore, _weaponRotationBefore);
            }
            _hasPose = false;
            CurrentWeight = 0f;
        }
    }
}
