using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>무기 최대 3개 장착, Tab으로 다음 무기 교체, 발도/납도 시 표시 전환.</summary>
    public class WeaponHolder : MonoBehaviour
    {
        public const int MaxSlots = 3;

        [Tooltip("장착 무기 (최대 3개, 빈 칸은 건너뜀)")]
        public List<Weapon> slots = new List<Weapon>();
        public int currentIndex;
        [Tooltip("교체 연출 시간(사라짐+나타남)")]
        public float swapTime = 0.2f;

        public bool IsSwapping { get; private set; }
        public Weapon Current =>
            (currentIndex >= 0 && currentIndex < slots.Count) ? slots[currentIndex] : null;

        bool _drawn;
        readonly Dictionary<Weapon, Vector3> _baseScale = new Dictionary<Weapon, Vector3>();

        void Awake()
        {
            if (slots.Count > MaxSlots) slots.RemoveRange(MaxSlots, slots.Count - MaxSlots);
            foreach (var w in slots) if (w) _baseScale[w] = w.transform.localScale;
            if (Current == null) currentIndex = NextIndex(currentIndex);
            Refresh();
        }

        /// <summary>발도(true)/납도(false). 납도 시 무기를 숨깁니다.</summary>
        public void SetDrawn(bool drawn)
        {
            _drawn = drawn;
            Refresh();
        }

        /// <summary>
        /// 시간 역행(CHG_TimeRewind) 복원용 — NGH(남귀훈) 추가.
        /// 진행 중인 교체 연출을 멈추고, 무기 칸과 발도/납도 표시를 즉시 맞춘다.
        /// </summary>
        public void RestoreState(int index, bool drawn)
        {
            StopAllCoroutines();
            IsSwapping = false;
            if (index >= 0 && index < slots.Count && slots[index]) currentIndex = index;
            _drawn = drawn;
            Refresh();
        }

        public void SwapNext()
        {
            if (IsSwapping) return;
            int next = NextIndex(currentIndex);
            if (next == currentIndex || next < 0) return;
            StartCoroutine(SwapRoutine(next));
        }

        int NextIndex(int from)
        {
            for (int i = 1; i <= slots.Count; i++)
            {
                int idx = (from + i) % slots.Count;
                if (slots[idx]) return idx;
            }
            return slots.Count > 0 && from >= 0 && from < slots.Count && slots[from] ? from : -1;
        }

        IEnumerator SwapRoutine(int next)
        {
            IsSwapping = true;
            float half = swapTime * 0.5f;
            var cur = Current;
            // 삭~ 사라지고
            if (_drawn && cur) yield return Scale(cur, 1f, 0f, half);
            currentIndex = next;
            Refresh();
            // 삭~ 나타남
            if (_drawn && Current) yield return Scale(Current, 0f, 1f, half);
            Debug.Log($"[CHG] 무기 교체 → {Current.weaponName}");
            IsSwapping = false;
        }

        IEnumerator Scale(Weapon w, float from, float to, float time)
        {
            Vector3 b;
            if (!_baseScale.TryGetValue(w, out b)) b = w.transform.localScale;
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                w.transform.localScale = b * Mathf.Lerp(from, to, t / time);
                yield return null;
            }
            w.transform.localScale = b * to;
        }

        void Refresh()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var w = slots[i];
                if (!w) continue;
                bool show = _drawn && i == currentIndex;
                w.gameObject.SetActive(show);
                if (show && _baseScale.TryGetValue(w, out var s)) w.transform.localScale = s;
            }
        }
    }
}
