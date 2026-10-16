using System.Collections;
using UnityEngine;

namespace YPH
{
    /// <summary>공격의 시작·취소·완료 통로를 통일하며 실제 판정 순서는 각 공격이 담당합니다.</summary>
    [RequireComponent(typeof(EnemyAI))]
    public abstract class EnemyAttack : MonoBehaviour
    {
        protected EnemyAI Brain { get; private set; }
        protected EnemyMotor Motor { get; private set; }
        protected EnemyAnimatorLink Animation { get; private set; }
        private Coroutine _routine;
        public bool IsRunning { get; private set; }

        protected virtual void Awake()
        {
            Brain = GetComponent<EnemyAI>();
            Motor = GetComponent<EnemyMotor>();
            Animation = GetComponent<EnemyAnimatorLink>();
        }

        /// <summary>현재 거리와 공격 대기 시간을 함께 검사합니다.</summary>
        public abstract bool CanBegin(float distanceToPlayer);
        /// <summary>EnemyAI가 공격 상태로 바꾼 뒤 호출합니다.</summary>
        public abstract void Begin();
        /// <summary>받아치기·사망 때 남은 코루틴을 취소합니다.</summary>
        public abstract void Cancel();

        protected void Run(IEnumerator sequence)
        {
            Cancel();
            IsRunning = true;
            _routine = StartCoroutine(sequence);
        }

        protected void StopSequence()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
            }
            _routine = null;
            IsRunning = false;
        }

        protected void Finish()
        {
            _routine = null;
            IsRunning = false;
            Brain.OnAttackFinished(this);
        }

        protected IEnumerator WaitActive(float duration, bool facePlayer = false)
        {
            float timer = 0f;
            while (timer < duration)
            {
                if (facePlayer)
                {
                    Brain.FacePlayer();
                }
                timer += Brain.ActiveDeltaTime;
                yield return null;
            }
        }

        protected virtual void OnDisable()
        {
            Cancel();
            // 부품만 꺼져도 AI가 끝난 공격을 기다리며 멈춰 있지 않도록 알립니다.
            Brain?.OnAttackFinished(this);
        }
    }
}
