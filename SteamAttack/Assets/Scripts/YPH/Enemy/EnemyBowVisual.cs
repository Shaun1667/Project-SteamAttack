using UnityEngine;

namespace YPH
{
    /// <summary>원거리 공격 신호에 맞춰 활시위·날개와 손 화살을 움직입니다. 피해 판정에는 관여하지 않습니다.</summary>
    [DisallowMultipleComponent]
    public class EnemyBowVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("신호를 받을 원거리 공격입니다. 비우면 시작할 때 부모에서 찾으며, 없으면 쉬는 자세를 유지합니다.")]
        private EnemyRangedAttack _rangedAttack;
        [SerializeField, Tooltip("시위를 그릴 선입니다. 위 끝·가운데·아래 끝의 점 3개를 갱신하며, 비우면 선을 그리지 않습니다.")]
        private LineRenderer _stringLine;
        [SerializeField, Tooltip("활 위 끝 뼈(B-bowTip01)입니다. 비우면 시위 선을 숨깁니다.")]
        private Transform _tipTop;
        [SerializeField, Tooltip("활 아래 끝 뼈(B-bowTip02)입니다. 비우면 시위 선을 숨깁니다.")]
        private Transform _tipBottom;
        [SerializeField, Tooltip("시위 가운데 뼈(B-nockPoint)입니다. 당길 때 이 점이 움직이며, 비우면 시위 선을 숨깁니다.")]
        private Transform _nockPoint;
        [SerializeField, Tooltip("끝까지 당겼을 때 시위 가운데가 갈 위치(AnchorPoint)입니다. 비우면 당기지 않고 시위 선을 숨깁니다.")]
        private Transform _pullAnchor;
        [SerializeField, Tooltip("활 위 날개 뼈(B-bowLimb01)입니다. 비우면 위 날개를 휘지 않습니다.")]
        private Transform _limbTop;
        [SerializeField, Tooltip("활 아래 날개 뼈(B-bowLimb02)입니다. 비우면 아래 날개를 휘지 않습니다.")]
        private Transform _limbBottom;
        [SerializeField, Min(0f), Tooltip("당겼을 때 날개 로컬 Z에서 뺄 각도(도)입니다. 높이면 많이 휘며 기본 15도, 0이면 휘지 않습니다.")]
        private float _limbBendAngle = 15f;
        [SerializeField, Min(0f), Tooltip("조준 시작부터 당기기 시작까지의 시간(초)입니다. 높이면 손이 시위에 닿기를 오래 기다리며 기본 0.2초입니다.")]
        private float _pullDelay = 0.2f;
        [SerializeField, Min(0f), Tooltip("시위를 끝까지 당기는 시간(초)입니다. 높이면 느리게 당기며 기본 0.4초, 0이면 즉시 당깁니다.")]
        private float _pullDuration = 0.4f;
        [SerializeField, Min(0f), Tooltip("놓은 시위가 튕겨 돌아오는 시간(초)입니다. 높이면 느려지며 기본 0.15초, 0이면 즉시 돌아옵니다.")]
        private float _releaseDuration = 0.15f;
        [SerializeField, Tooltip("놓기 진행도에 따른 복귀량입니다. 1을 넘으면 쉬는 위치를 지나쳤다가 돌아오며, 비우면 선형으로 복귀합니다.")]
        private AnimationCurve _releaseCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.342f, 1.166f),
            new Keyframe(0.705f, 0.872f), new Keyframe(1f, 1f));
        [SerializeField, Min(0f), Tooltip("조준 취소 후 시위가 돌아오는 시간(초)입니다. 높이면 느려지며 기본 0.2초, 0이면 즉시 돌아옵니다.")]
        private float _cancelDuration = 0.2f;
        [SerializeField, Tooltip("오른손에 든 화살입니다. 발사 때 숨기고 잠시 뒤 보이며, 비우면 표시 처리를 생략합니다.")]
        private GameObject _arrowInHand;
        [SerializeField, Min(0f), Tooltip("발사 후 손 화살이 다시 보일 때까지의 시간(초)입니다. 높이면 늦게 나타나며 기본 1.25초, 0이면 바로 보입니다.")]
        private float _arrowReturnDelay = 1.25f;

        private enum State { Rest, Pulling, Releasing, Canceling }
        private State _state;
        private Vector3 _restNockPosition, _restTopAngles, _restBottomAngles;
        private float _timer, _drawAmount, _startDrawAmount, _arrowTimer;
        private bool _waitingForArrow;

        /// <summary>공격 부품과 시위·날개의 쉬는 자세를 한 번만 저장합니다.</summary>
        private void Awake()
        {
            if (_rangedAttack == null)
            {
                _rangedAttack = GetComponentInParent<EnemyRangedAttack>();
            }
            if (_nockPoint != null) _restNockPosition = _nockPoint.localPosition;
            if (_limbTop != null) _restTopAngles = _limbTop.localEulerAngles;
            if (_limbBottom != null) _restBottomAngles = _limbBottom.localEulerAngles;
            if (_stringLine != null)
            {
                _stringLine.positionCount = 3;
                _stringLine.useWorldSpace = true;
            }
        }

        /// <summary>신호를 구독하고 재활성화할 때 시위와 손 화살을 초기화합니다.</summary>
        private void OnEnable()
        {
            if (_rangedAttack != null)
            {
                _rangedAttack.AimStarted += OnAimStarted;
                _rangedAttack.Fired += OnFired;
                _rangedAttack.AimCanceled += OnAimCanceled;
            }
            _state = State.Rest;
            _timer = _drawAmount = _startDrawAmount = _arrowTimer = 0f;
            _waitingForArrow = false;
            SetArrowVisible(true);
            ApplyPose();
        }

        /// <summary>비활성화할 때 구독을 해제해 중복 반응을 막습니다.</summary>
        private void OnDisable()
        {
            if (_rangedAttack != null)
            {
                _rangedAttack.AimStarted -= OnAimStarted;
                _rangedAttack.Fired -= OnFired;
                _rangedAttack.AimCanceled -= OnAimCanceled;
            }
        }

        /// <summary>조준 시작 시 현재 당김에서 이어 당기고 손 화살을 보입니다.</summary>
        private void OnAimStarted()
        {
            _state = State.Pulling;
            _timer = 0f;
            _startDrawAmount = _drawAmount;
            _waitingForArrow = false;
            SetArrowVisible(true);
        }

        /// <summary>발사 순간부터 시위를 복귀시키고 손 화살의 재표시를 예약합니다.</summary>
        private void OnFired()
        {
            _state = State.Releasing;
            _timer = 0f;
            _startDrawAmount = _drawAmount;
            _arrowTimer = 0f;
            _waitingForArrow = true;
            SetArrowVisible(false);
        }

        /// <summary>조준 취소 시 현재 당김에서 쉬는 자세로 부드럽게 돌아갑니다.</summary>
        private void OnAimCanceled()
        {
            _state = State.Canceling;
            _timer = 0f;
            _startDrawAmount = _drawAmount;
        }

        /// <summary>할당 없이 타이머로 당김·복귀와 손 화살 재표시를 진행합니다.</summary>
        private void Update()
        {
            float delta = Time.deltaTime;
            if (_waitingForArrow)
            {
                _arrowTimer += delta;
                if (_arrowTimer >= _arrowReturnDelay)
                {
                    _waitingForArrow = false;
                    SetArrowVisible(true);
                }
            }
            _timer += delta;
            if (_state == State.Pulling)
            {
                if (_timer >= _pullDelay)
                {
                    float progress = Progress(_timer - _pullDelay, _pullDuration);
                    _drawAmount = Mathf.Lerp(_startDrawAmount, 1f, progress);
                }
            }
            else if (_state == State.Releasing || _state == State.Canceling)
            {
                float duration = _state == State.Releasing ? _releaseDuration : _cancelDuration;
                float progress = Progress(_timer, duration);
                float amount = _state == State.Releasing && _releaseCurve != null && _releaseCurve.length > 0
                    ? _releaseCurve.Evaluate(progress) : progress;
                _drawAmount = Mathf.LerpUnclamped(_startDrawAmount, 0f, amount);
                if (progress >= 1f)
                {
                    _drawAmount = 0f;
                    _state = State.Rest;
                }
            }
        }

        /// <summary>0초 설정도 안전하게 완료 진행도로 바꿉니다.</summary>
        private static float Progress(float elapsed, float duration)
        {
            return duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
        }

        /// <summary>Animator가 몸을 움직인 뒤 날개·시위 위치와 선의 세 점을 갱신합니다.</summary>
        private void LateUpdate()
        {
            ApplyPose();
            if (_stringLine == null) return;
            bool valid = _tipTop != null && _tipBottom != null && _nockPoint != null && _pullAnchor != null;
            _stringLine.enabled = valid;
            if (!valid) return;
            _stringLine.SetPosition(0, _tipTop.position);
            _stringLine.SetPosition(1, _nockPoint.position);
            _stringLine.SetPosition(2, _tipBottom.position);
        }

        /// <summary>당김 비율을 로컬 좌표로 적용해 몸과 활의 이동을 그대로 따라갑니다.</summary>
        private void ApplyPose()
        {
            if (_nockPoint != null)
            {
                Vector3 target = _restNockPosition;
                if (_pullAnchor != null)
                {
                    target = _nockPoint.parent != null
                        ? _nockPoint.parent.InverseTransformPoint(_pullAnchor.position) : _pullAnchor.position;
                }
                _nockPoint.localPosition = Vector3.LerpUnclamped(_restNockPosition, target, _drawAmount);
            }
            Vector3 bend = Vector3.forward * (_limbBendAngle * _drawAmount);
            if (_limbTop != null) _limbTop.localEulerAngles = _restTopAngles - bend;
            if (_limbBottom != null) _limbBottom.localEulerAngles = _restBottomAngles - bend;
        }

        /// <summary>손 화살이 연결된 경우에만 표시 상태를 바꿉니다.</summary>
        private void SetArrowVisible(bool visible)
        {
            if (_arrowInHand != null) _arrowInHand.SetActive(visible);
        }
    }
}
