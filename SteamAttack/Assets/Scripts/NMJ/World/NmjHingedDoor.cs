using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 경첩 문 (근정전 꽃살문 등). 이 오브젝트의 원점 = 경첩, 로컬 Y축으로 돈다.
    /// - 플레이어(태그 Player)가 openRadius 안에 들어오면 열리고, closeRadius 밖으로 나가면 닫힌다.
    /// - 문짝 모델은 블렌더에서 원점을 경첩 쪽 모서리에 맞춰 두었다 (오른짝은 좌우 거울, 열 각도는 음수).
    /// - 문에 붙은 콜라이더도 같이 돌아서, 닫혀 있을 때는 막고 열리면 지나갈 수 있다.
    /// </summary>
    public class NmjHingedDoor : MonoBehaviour
    {
        [Tooltip("열렸을 때 각도(도). 왼짝 +, 오른짝(거울) − → 둘 다 안쪽으로 열린다")]
        [SerializeField] private float openAngle = 95f;
        [SerializeField] private float openRadius = 4.5f;
        [SerializeField] private float closeRadius = 7f;
        [Tooltip("다 열리는 데 걸리는 시간(초)")]
        [SerializeField] private float openTime = 0.9f;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private AudioClip openSound;

        private Quaternion closedRot;
        private Vector3 closedCenter;   // 닫혀 있을 때 문짝 가운데 (월드) — 플레이어 거리 기준
        private float t;          // 0 닫힘 ~ 1 열림
        private bool wantOpen;
        private Transform player;
        private float nextSearch;

        public void Configure(float angle) => openAngle = angle;

        private void Awake()
        {
            closedRot = transform.localRotation;
            var r = GetComponent<Renderer>();
            closedCenter = r != null ? r.bounds.center : transform.position;
        }

        private void Update()
        {
            if (player == null && Time.time >= nextSearch)
            {
                nextSearch = Time.time + 1f;
                var p = GameObject.FindGameObjectWithTag(playerTag);
                if (p != null) player = p.transform;
            }
            if (player != null)
            {
                // 닫힌 문짝 가운데까지 수평 거리 (경첩 기준이면 칸마다 어긋난다)
                Vector3 a = player.position, b = closedCenter; a.y = b.y = 0f;
                float d = Vector3.Distance(a, b);
                bool before = wantOpen;
                if (d < openRadius) wantOpen = true;
                else if (d > closeRadius) wantOpen = false;
                if (wantOpen && !before && openSound != null) AudioSource.PlayClipAtPoint(openSound, transform.position, 0.8f);
            }
            float target = wantOpen ? 1f : 0f;
            if (!Mathf.Approximately(t, target))
            {
                t = Mathf.MoveTowards(t, target, Time.deltaTime / Mathf.Max(0.05f, openTime));
                float e = t * t * (3f - 2f * t);
                transform.localRotation = closedRot * Quaternion.Euler(0f, openAngle * e, 0f);
            }
        }
    }
}
