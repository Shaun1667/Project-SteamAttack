using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 플레이어가 다가가면 열리고 멀어지면 닫히는 여닫이문 (문짝 여러 장).
    ///  - 문짝마다 경첩(문짝 오브젝트의 기준점)을 축으로 정해진 각도만큼 세로축으로 돌림
    ///  - 문짝 오브젝트에는 아무것도 붙이지 않고 이 컴포넌트가 회전만 바꿈 (다른 사람 오브젝트를 고치지 않으려고)
    ///  - 열고 닫는 기준은 이 오브젝트 위치에서 플레이어까지의 거리. 닫힘 거리를 열림 거리보다 길게 두어 문 앞에서 덜컹거리지 않게 함
    /// 문간 가운데에 놓은 빈 오브젝트에 붙입니다.
    /// </summary>
    public class ProximityDoor : MonoBehaviour
    {
        [System.Serializable]
        public class Leaf
        {
            [Tooltip("돌릴 문짝 (기준점이 경첩에 있어야 함)")]
            public Transform door;
            [Tooltip("열렸을 때 세로축 회전 각도 (도). 부호로 여는 방향을 정함")]
            public float openAngle = 90f;
            [HideInInspector] public Quaternion closedRotation;
        }

        [Tooltip("함께 열리는 문짝들")]
        public Leaf[] leaves;
        [Tooltip("플레이어가 이 거리 안으로 오면 열림 (m)")]
        [Min(0.5f)] public float openDistance = 4.5f;
        [Tooltip("플레이어가 이 거리 밖으로 나가면 닫힘 (m). 열림 거리보다 길게")]
        [Min(0.5f)] public float closeDistance = 7f;
        [Tooltip("다 열리거나 닫히는 데 걸리는 시간 (초)")]
        [Min(0.05f)] public float duration = 0.6f;
        [Tooltip("한 번 열리면 다시 닫지 않음")]
        public bool stayOpen = false;

        Transform _player;
        float _t;          // 0 = 닫힘, 1 = 열림
        bool _open;

        void Awake()
        {
            if (leaves == null) return;
            foreach (var l in leaves) if (l.door) l.closedRotation = l.door.rotation;
        }

        void Update()
        {
            if (!_player)
            {
                var p = FindAnyObjectByType<PlayerController>();
                if (!p) return;
                _player = p.transform;
            }

            float d = Vector3.Distance(_player.position, transform.position);
            if (d < openDistance) _open = true;
            else if (d > closeDistance && !stayOpen) _open = false;

            float target = _open ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / duration);
            float k = Mathf.SmoothStep(0f, 1f, _t);
            foreach (var l in leaves)
                if (l.door) l.door.rotation = Quaternion.Euler(0f, l.openAngle * k, 0f) * l.closedRotation;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, openDistance);
            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, closeDistance);
        }
    }
}
