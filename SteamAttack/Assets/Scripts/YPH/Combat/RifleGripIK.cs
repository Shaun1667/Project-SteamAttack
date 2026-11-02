using NGH;
using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 총 프리팹의 양손 손목 목표에 팔을 맞추고 오른손 손가락을 손잡이에 맞춥니다.
    /// 양팔 도달 범위와 손목 제한에 맞춘 총 자세를 유지해 부모와 목표가 함께 움직이는 순환을 막습니다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1020)]
    public sealed class RifleGripIK : MonoBehaviour
    {
        private static readonly int RifleParryHash = Animator.StringToHash("Rifle.RifleParry");
        private static readonly HumanBodyBones[] RightFingerBones =
        {
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
        };

        [Header("오른손 손잡이")]
        [SerializeField, Tooltip("같은 총 아래의 오른손 손목 목표입니다. 손잡이를 감싸는 위치·회전을 정합니다. 비우면 기존 오른팔과 손가락을 유지합니다.")]
        private Transform _primaryGrip;
        [SerializeField, Tooltip("오른쪽 팔꿈치 방향 목표입니다. 총 Model 아래 또는 캐릭터 루트 아래의 목표를 연결하고, 비우면 원래 모션의 방향을 유지합니다.")]
        private Transform _primaryElbowHint;
        [SerializeField, Tooltip("오른손 손가락의 로컬 회전각(도)입니다. 엄지·검지·중지·약지·소지 순으로 각 3관절, 총 15개입니다. 비우면 기존 손가락 모션을 유지합니다.")]
        private Vector3[] _rightFingerLocalEulerAngles = System.Array.Empty<Vector3>();

        [Header("동작별 오른팔 보정")]
        [SerializeField, Tooltip("클립마다 달라지는 팔 자세에 맞춰 손목 꺾임을 제한합니다. 켜면 원래 팔꿈치 방향을 최대한 유지하고, 필요할 때만 총을 가까운 자연스러운 위치로 옮깁니다.")]
        private bool _adaptPrimaryPose = true;
        [SerializeField, Range(10f, 80f), Tooltip("팔뚝과 손등 사이에서 허용할 꺾임(도)입니다. 낮추면 손목을 더 펴며, 총 위치 보정이 더 커질 수 있습니다. 기본 40도입니다.")]
        private float _maxPrimaryWristAngle = 40f;
        [SerializeField, Range(0f, 0.2f), Tooltip("손목을 펴기 위해 총을 옮길 수 있는 최대 거리(m)입니다. 0이면 총은 원래 위치를 유지합니다. 기본 0.14m이며 사격 방향은 바꾸지 않습니다.")]
        private float _maxPrimaryGunShift = 0.14f;

        [Header("왼손 받침")]
        [SerializeField, Tooltip("같은 총 아래에 둔 왼손 손목 목표입니다. 위치와 회전으로 받치는 자세를 조절하며, 비우면 보정하지 않습니다.")]
        private Transform _supportGrip;
        [SerializeField, Tooltip("선택 사항인 팔꿈치 방향 목표입니다. 비우면 현재 애니메이션의 굽힘 방향을 유지합니다. 왼팔 뼈 아래에는 두지 마세요.")]
        private Transform _elbowHint;
        [SerializeField, Range(0f, 1f), Tooltip("양손 손목 회전 보정 비율입니다. 1은 목표 회전을 따르고, 0은 기존 손목 회전을 유지합니다.")]
        private float _rotationWeight = 1f;
        [SerializeField, Range(1f, 30f), Tooltip("보정을 켤 때 초당 늘릴 가중치입니다. 기본 12이며 높이면 빠르게 받칩니다. 회피·피격·장비 교체 때는 즉시 해제합니다.")]
        private float _blendSpeed = 12f;

        private Animator _animator;
        private PlayerController _player;
        private WeaponHolder _weapons;
        private Weapon _weapon;
        private TimeRewind _rewind;
        private RifleAimPose _aimPose;
        private Transform _upperArm;
        private Transform _forearm;
        private Transform _hand;
        private Transform _rightUpperArm;
        private Transform _rightForearm;
        private Transform _rightHand;
        private readonly Transform[] _rightFingers = new Transform[RightFingerBones.Length];
        private readonly Quaternion[] _rightFingerTargets = new Quaternion[RightFingerBones.Length];
        private readonly Quaternion[] _rightFingersBefore = new Quaternion[RightFingerBones.Length];
        private Quaternion _upperBefore;
        private Quaternion _forearmBefore;
        private Quaternion _handBefore;
        private Quaternion _rightUpperBefore;
        private Quaternion _rightForearmBefore;
        private Quaternion _rightHandBefore;
        private Vector3 _weaponPositionBefore;
        private Quaternion _weaponRotationBefore;
        private Vector3 _primaryPalmAxis = Vector3.up;
        private bool _isBound;
        private bool _hasCorrection;
        private bool _hasPrimaryCorrection;
        private bool _hasFingerCorrection;
        private bool _hasFingerPose;
        private float _currentWeight;
        private int _rifleLayerIndex;

        public Transform SupportGrip => _supportGrip;
        public Transform PrimaryGrip => _primaryGrip;
        public float CurrentWeight => _currentWeight;

        /// <summary>장착·재활성화 때만 부모의 연결과 팔 관절을 찾습니다.</summary>
        private void OnEnable()
        {
            _currentWeight = 0f;
            _animator = GetComponentInParent<Animator>();
            _player = GetComponentInParent<PlayerController>();
            _weapons = GetComponentInParent<WeaponHolder>();
            _weapon = GetComponent<Weapon>();
            _rewind = GetComponentInParent<TimeRewind>();
            _isBound = _supportGrip != null && _supportGrip.IsChildOf(transform)
                && _animator != null && _animator.isHuman && _animator.avatar != null
                && _animator.avatar.isValid && _player != null && _weapons != null && _weapon != null;
            if (!_isBound)
            {
                return;
            }
            _rifleLayerIndex = _animator.GetLayerIndex("Rifle");
            _upperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _forearm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _hand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _isBound = _upperArm != null && _forearm != null && _hand != null;
            BindRightHand();
            _aimPose = GetComponent<RifleAimPose>();
            if (_aimPose != null)
            {
                _aimPose.Bind(_animator, _player.transform, _player.GetComponent<RifleMotion>());
            }
        }

        /// <summary>오른손 설정은 활성화 때 한 번 읽어 매 프레임 관절 탐색과 배열 생성을 피합니다.</summary>
        private void BindRightHand()
        {
            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightForearm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            CacheFingerPose();
            for (int i = 0; i < RightFingerBones.Length; i++)
            {
                _rightFingers[i] = _animator.GetBoneTransform(RightFingerBones[i]);
            }
            if (_rightHand != null && _rightFingers[6] != null)
            {
                _primaryPalmAxis = _rightHand.InverseTransformDirection(
                    _rightFingers[6].position - _rightHand.position).normalized;
            }
        }

        /// <summary>Inspector에서 손가락 각도를 바꿔도 재생을 다시 시작하지 않고 확인할 수 있게 합니다.</summary>
        private void OnValidate()
        {
            CacheFingerPose();
        }

        /// <summary>지정된 15개 관절 각도만 회전으로 변환하고, 빈 설정은 원래 모션을 사용합니다.</summary>
        private void CacheFingerPose()
        {
            _hasFingerPose = _rightFingerLocalEulerAngles != null
                && _rightFingerLocalEulerAngles.Length == RightFingerBones.Length;
            if (!_hasFingerPose)
            {
                return;
            }
            for (int i = 0; i < RightFingerBones.Length; i++)
            {
                _rightFingerTargets[i] = Quaternion.Euler(_rightFingerLocalEulerAngles[i]);
            }
        }

        /// <summary>다음 애니메이션 평가 전에 이전 보정만 돌려줘 누적 비틀림을 막습니다.</summary>
        private void Update()
        {
            RestorePose();
        }

        /// <summary>자세 전달(900)과 바닥 높이 보정(1000)이 끝난 뒤, 소매 물리(1060)보다 먼저 팔을 맞춥니다.</summary>
        private void LateUpdate()
        {
            if (!CanApplyGrip())
            {
                _currentWeight = 0f;
                return;
            }
            _currentWeight = Mathf.MoveTowards(_currentWeight, 1f, _blendSpeed * Time.deltaTime);
            ApplyGrip(_currentWeight);
        }

        /// <summary>총 대기·이동·사격에만 적용하고 다른 행동과 역행은 원래 모션에 맡깁니다.</summary>
        private bool CanApplyGrip()
        {
            return _isBound && _animator.isActiveAndEnabled && _supportGrip != null
                && _weapons.Current == _weapon && !_weapon.isMelee && !_weapons.IsSwapping && _player.IsDrawn
                && !_player.IsDead && !_player.IsStandingUp && !_player.IsHitReacting
                && (_rewind == null || !_rewind.IsRewinding)
                && !IsParrying()
                && (_player.CurrentAction == PlayerController.ActionState.None
                    || _player.CurrentAction == PlayerController.ActionState.Shot);
        }

        /// <summary>총 받아치기는 행동 상태가 바뀌지 않으므로 전환 중인 상체 애니메이션도 확인합니다.</summary>
        private bool IsParrying()
        {
            return _rifleLayerIndex >= 0
                && (_animator.GetCurrentAnimatorStateInfo(_rifleLayerIndex).fullPathHash == RifleParryHash
                    || (_animator.IsInTransition(_rifleLayerIndex)
                        && _animator.GetNextAnimatorStateInfo(_rifleLayerIndex).fullPathHash == RifleParryHash));
        }

        /// <summary>목표를 미리 보거나 검증할 때도 같은 계산을 사용합니다. 0이면 원래 팔 자세로 복원합니다.</summary>
        public void ApplyGrip(float weight)
        {
            RestorePose();
            if (!_isBound || _supportGrip == null || weight <= 0f)
            {
                return;
            }
            weight = Mathf.Clamp01(weight);
            if (_aimPose != null && _aimPose.isActiveAndEnabled && _primaryGrip != null)
            {
                _aimPose.ApplyPose(weight, _primaryGrip);
            }
            ApplyPrimaryGrip(weight);
            _upperBefore = _upperArm.localRotation;
            _forearmBefore = _forearm.localRotation;
            _handBefore = _hand.localRotation;
            Vector3 position = Vector3.Lerp(_hand.position, _supportGrip.position, weight);
            Quaternion rotation = Quaternion.Slerp(_hand.rotation, _supportGrip.rotation, weight * _rotationWeight);
            Vector3 pole = (_elbowHint != null ? _elbowHint.position : _forearm.position) - _upperArm.position;
            _hasCorrection = SolveArm(_upperArm, _forearm, _hand, position, rotation, pole);
        }

        /// <summary>오른팔을 보정한 뒤 총은 계산한 월드 자세로 돌려 목표가 손을 따라 달아나는 것을 막습니다.</summary>
        private void ApplyPrimaryGrip(float weight)
        {
            if (_primaryGrip == null || !_primaryGrip.IsChildOf(transform)
                || _rightUpperArm == null || _rightForearm == null || _rightHand == null)
            {
                return;
            }
            _rightUpperBefore = _rightUpperArm.localRotation;
            _rightForearmBefore = _rightForearm.localRotation;
            _rightHandBefore = _rightHand.localRotation;
            _weaponPositionBefore = transform.localPosition;
            _weaponRotationBefore = transform.localRotation;
            Vector3 animatedElbow = _rightForearm.position;
            bool movedWeapon = false;
            bool alignedAim = _aimPose != null && _aimPose.CurrentWeight > 0f;
            Vector3 originalGrip = _primaryGrip.position;
            int passes = alignedAim ? 4 : 1;
            for (int i = 0; i < passes; i++)
            {
                movedWeapon |= _adaptPrimaryPose && AdaptWeaponPosition(weight, animatedElbow);
                if (alignedAim)
                {
                    // 반복 횟수와 관계없이 전체 이동을 제한하고 총구 방향과 양팔 도달을 함께 맞춥니다.
                    Vector3 shift = Vector3.ClampMagnitude(_primaryGrip.position - originalGrip, _maxPrimaryGunShift * weight);
                    transform.position += originalGrip + shift - _primaryGrip.position;
                    _aimPose.AlignWeaponToAim(_primaryGrip);
                }
            }
            Vector3 weaponPosition = transform.position;
            Quaternion weaponRotation = transform.rotation;
            Vector3 position = Vector3.Lerp(_rightHand.position, _primaryGrip.position, weight);
            Quaternion rotation = Quaternion.Slerp(_rightHand.rotation, _primaryGrip.rotation, weight * _rotationWeight);
            Vector3 pole = _adaptPrimaryPose
                ? GetWristLimitedPole(position, rotation * _primaryPalmAxis, animatedElbow, !movedWeapon)
                : (_primaryElbowHint != null ? _primaryElbowHint.position : animatedElbow) - _rightUpperArm.position;
            _hasPrimaryCorrection = SolveArm(_rightUpperArm, _rightForearm, _rightHand, position, rotation, pole);
            if (_hasPrimaryCorrection)
            {
                transform.SetPositionAndRotation(weaponPosition, weaponRotation);
                ApplyRightFingers(weight);
            }
            else
            {
                transform.SetLocalPositionAndRotation(_weaponPositionBefore, _weaponRotationBefore);
            }
        }

        /// <summary>필요한 만큼만 총을 옮겨 양팔이 닿게 하고, 손목과 원래 팔꿈치 자세를 함께 지킵니다.</summary>
        private bool AdaptWeaponPosition(float weight, Vector3 animatedElbow)
        {
            Vector3 shoulder = _rightUpperArm.position;
            float upperLength = Vector3.Distance(shoulder, animatedElbow);
            float lowerLength = Vector3.Distance(animatedElbow, _rightHand.position);
            if (upperLength < 0.00001f || lowerLength < 0.00001f || _maxPrimaryGunShift <= 0f)
            {
                return false;
            }
            Vector3 target = _primaryGrip.position;
            Vector3 palm = Quaternion.Slerp(_rightHand.rotation, _primaryGrip.rotation, _rotationWeight) * _primaryPalmAxis;
            float limit = Mathf.Max(5f, _maxPrimaryWristAngle - 5f);
            float leftUpper = Vector3.Distance(_upperArm.position, _forearm.position);
            float leftLower = Vector3.Distance(_forearm.position, _hand.position);
            float leftRadius = leftUpper + leftLower - Mathf.Min(leftUpper, leftLower) * 0.002f;
            Vector3 leftOffset = _supportGrip.position - _upperArm.position;
            Vector3 leftShift = Vector3.ClampMagnitude(leftOffset, leftRadius) - leftOffset;
            bool leftReachable = leftShift.sqrMagnitude < 0.00000001f;
            if (leftReachable && MinimumWristAngle(shoulder, target, palm, upperLength, lowerLength) <= limit)
            {
                return false;
            }
            if (!leftReachable && leftShift.magnitude <= _maxPrimaryGunShift
                && Vector3.Distance(shoulder, target + leftShift) < upperLength + lowerLength - 0.0002f
                && MinimumWristAngle(shoulder, target + leftShift, palm, upperLength, lowerLength) <= limit)
            {
                transform.position += leftShift * weight;
                return true;
            }
            Vector3 neutralTarget = GetReachableNeutralTarget(shoulder, target, palm, animatedElbow, upperLength, lowerLength);
            Vector3 shift = Vector3.ClampMagnitude(neutralTarget - target, _maxPrimaryGunShift);
            float low = 0f;
            float high = 1f;
            if (!leftReachable && shift.sqrMagnitude > 0.00000001f)
            {
                float projection = Vector3.Dot(leftOffset, shift);
                float discriminant = projection * projection
                    - shift.sqrMagnitude * (leftOffset.sqrMagnitude - leftRadius * leftRadius);
                if (discriminant >= 0f)
                {
                    low = Mathf.Clamp01((-projection - Mathf.Sqrt(discriminant)) / shift.sqrMagnitude);
                }
            }
            for (int i = 0; i < 8; i++)
            {
                float middle = (low + high) * 0.5f;
                if (MinimumWristAngle(shoulder, target + shift * middle, palm, upperLength, lowerLength) > limit)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            transform.position += shift * (high * weight);
            return shift.sqrMagnitude > 0.00000001f;
        }

        /// <summary>오른손을 펼 수 있는 위치 중 왼팔도 닿는 지점을 찾습니다. 양손의 도달 구를 함께 검사합니다.</summary>
        private Vector3 GetReachableNeutralTarget(Vector3 shoulder, Vector3 target, Vector3 palm,
            Vector3 animatedElbow, float upper, float lower)
        {
            Vector3 center = shoulder + palm * lower;
            Vector3 preferred = animatedElbow + palm * lower;
            float leftUpper = Vector3.Distance(_upperArm.position, _forearm.position);
            float leftLower = Vector3.Distance(_forearm.position, _hand.position);
            float leftRadius = leftUpper + leftLower - Mathf.Min(leftUpper, leftLower) * 0.002f;
            Vector3 leftCenter = _upperArm.position - (_supportGrip.position - target);
            if (Vector3.Distance(preferred, leftCenter) <= leftRadius)
            {
                return preferred;
            }
            Vector3 axis = leftCenter - center;
            float distance = axis.magnitude;
            if (distance < 0.00001f || distance > upper + leftRadius || upper > distance + leftRadius)
            {
                return center + axis.normalized * upper;
            }
            axis /= distance;
            float along = (upper * upper - leftRadius * leftRadius + distance * distance) / (2f * distance);
            Vector3 circleCenter = center + axis * along;
            float radius = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 side = Vector3.ProjectOnPlane(preferred - circleCenter, axis);
            if (side.sqrMagnitude < 0.00000001f)
            {
                side = Vector3.ProjectOnPlane(_animator.transform.right, axis);
            }
            return circleCenter + side.normalized * radius;
        }

        /// <summary>팔 길이와 손 방향으로 도달 가능한 최소 손목 꺾임을 계산합니다.</summary>
        private static float MinimumWristAngle(Vector3 shoulder, Vector3 target, Vector3 palm, float upper, float lower)
        {
            Vector3 axis = target - shoulder;
            float distance = Mathf.Clamp(axis.magnitude, Mathf.Abs(upper - lower) + 0.00001f, upper + lower - 0.00001f);
            float cosine = Mathf.Clamp((distance * distance + lower * lower - upper * upper) / (2f * distance * lower), -1f, 1f);
            float forearmAngle = Mathf.Acos(cosine) * Mathf.Rad2Deg;
            return Mathf.Abs(Vector3.Angle(axis, palm) - forearmAngle);
        }

        /// <summary>원래 팔꿈치에 가장 가까운 방향부터 검사해 손목 제한을 만족하는 만큼만 팔을 돌립니다.</summary>
        private Vector3 GetWristLimitedPole(Vector3 target, Vector3 palm, Vector3 animatedElbow, bool preferHint)
        {
            Vector3 shoulder = _rightUpperArm.position;
            float upper = Vector3.Distance(shoulder, animatedElbow);
            float lower = Vector3.Distance(animatedElbow, _rightHand.position);
            Vector3 axis = target - shoulder;
            float distance = axis.magnitude;
            if (upper < 0.00001f || lower < 0.00001f || distance < 0.00001f)
            {
                return animatedElbow - shoulder;
            }
            axis /= distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + 0.00001f, upper + lower - 0.00001f);
            float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 animatedBend = Vector3.ProjectOnPlane(animatedElbow - shoulder, axis).normalized;
            Vector3 neutralBend = -Vector3.ProjectOnPlane(palm, axis).normalized;
            if (animatedBend.sqrMagnitude < 0.000001f || neutralBend.sqrMagnitude < 0.000001f)
            {
                return animatedElbow - shoulder;
            }
            if (preferHint && _primaryElbowHint != null)
            {
                Vector3 hintBend = Vector3.ProjectOnPlane(_primaryElbowHint.position - shoulder, axis).normalized;
                if (hintBend.sqrMagnitude > 0.000001f
                    && Vector3.Angle(axis * (distance - along) - hintBend * height, palm) <= _maxPrimaryWristAngle)
                {
                    return hintBend;
                }
            }
            if (Vector3.Angle(axis * (distance - along) - animatedBend * height, palm) <= _maxPrimaryWristAngle)
            {
                return animatedBend;
            }
            float turn = Vector3.SignedAngle(animatedBend, neutralBend, axis);
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 8; i++)
            {
                float middle = (low + high) * 0.5f;
                Vector3 bend = Quaternion.AngleAxis(turn * middle, axis) * animatedBend;
                if (Vector3.Angle(axis * (distance - along) - bend * height, palm) > _maxPrimaryWristAngle)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            return Quaternion.AngleAxis(turn * high, axis) * animatedBend;
        }

        /// <summary>검지는 방아쇠 쪽, 나머지 손가락은 손잡이에 맞춘 지정 자세를 섞습니다.</summary>
        private void ApplyRightFingers(float weight)
        {
            if (!_hasFingerPose)
            {
                return;
            }
            for (int i = 0; i < _rightFingers.Length; i++)
            {
                if (_rightFingers[i] == null)
                {
                    continue;
                }
                _rightFingersBefore[i] = _rightFingers[i].localRotation;
                _rightFingers[i].localRotation = Quaternion.Slerp(_rightFingersBefore[i], _rightFingerTargets[i], weight);
            }
            _hasFingerCorrection = true;
        }

        /// <summary>팔 길이를 늘리지 않고 삼각형의 변 길이로 어깨와 팔꿈치 각도를 구합니다.</summary>
        private bool SolveArm(Transform upperArm, Transform forearm, Transform handBone,
            Vector3 target, Quaternion rotation, Vector3 pole)
        {
            Vector3 shoulder = upperArm.position;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float lowerLength = Vector3.Distance(forearm.position, handBone.position);
            if (upperLength < 0.00001f || lowerLength < 0.00001f)
            {
                return false;
            }
            Vector3 direction = target - shoulder;
            float targetDistance = direction.magnitude;
            if (direction.sqrMagnitude < 0.00000001f)
            {
                direction = handBone.position - shoulder;
            }
            if (direction.sqrMagnitude < 0.00000001f)
            {
                return false;
            }
            float margin = Mathf.Min(upperLength, lowerLength) * 0.001f;
            float distance = Mathf.Clamp(targetDistance,
                Mathf.Abs(upperLength - lowerLength) + margin, upperLength + lowerLength - margin);
            direction.Normalize();
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 0.00000001f)
            {
                bend = Vector3.ProjectOnPlane(_animator.transform.forward, direction);
            }
            if (bend.sqrMagnitude < 0.00000001f)
            {
                bend = Vector3.ProjectOnPlane(_animator.transform.up, direction);
            }
            bend.Normalize();
            float cosine = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                / (2f * upperLength * distance), -1f, 1f);
            Vector3 elbow = shoulder + upperLength * (direction * cosine + bend * Mathf.Sqrt(1f - cosine * cosine));
            upperArm.rotation = Quaternion.FromToRotation(forearm.position - shoulder, elbow - shoulder) * upperArm.rotation;
            Vector3 hand = shoulder + direction * distance;
            forearm.rotation = Quaternion.FromToRotation(handBone.position - forearm.position, hand - forearm.position) * forearm.rotation;
            handBone.rotation = rotation;
            return true;
        }

        /// <summary>손을 놓거나 껐을 때 마지막 보정 자세가 남지 않게 원래 애니메이션으로 돌립니다.</summary>
        private void RestorePose()
        {
            if (_hasCorrection && _upperArm != null && _forearm != null && _hand != null)
            {
                _upperArm.localRotation = _upperBefore;
                _forearm.localRotation = _forearmBefore;
                _hand.localRotation = _handBefore;
            }
            if (_hasPrimaryCorrection && _rightUpperArm != null && _rightForearm != null && _rightHand != null)
            {
                _rightUpperArm.localRotation = _rightUpperBefore;
                _rightForearm.localRotation = _rightForearmBefore;
                _rightHand.localRotation = _rightHandBefore;
                transform.SetLocalPositionAndRotation(_weaponPositionBefore, _weaponRotationBefore);
            }
            if (_hasFingerCorrection)
            {
                for (int i = 0; i < _rightFingers.Length; i++)
                {
                    if (_rightFingers[i] != null)
                    {
                        _rightFingers[i].localRotation = _rightFingersBefore[i];
                    }
                }
            }
            _hasCorrection = false;
            _hasPrimaryCorrection = false;
            _hasFingerCorrection = false;
            if (_aimPose != null)
            {
                _aimPose.RestorePose();
            }
        }

        /// <summary>무기를 숨기거나 컴포넌트를 끄면 양팔·손가락·총 연결을 원래 모션으로 돌려줍니다.</summary>
        private void OnDisable()
        {
            RestorePose();
            _currentWeight = 0f;
        }
    }
}
