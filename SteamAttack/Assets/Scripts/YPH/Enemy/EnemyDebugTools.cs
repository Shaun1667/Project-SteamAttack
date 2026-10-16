#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using NGH;

namespace YPH
{
    /// <summary>씬에 하나만 자동 생성되어 개발용 키·설명·레이저를 관리합니다. 출시 빌드에는 타입 자체가 없습니다.</summary>
    public class EnemyDebugTools : MonoBehaviour
    {
        private const float DebugSteamAmount = 20f;
        private readonly List<TimeRewindTester> _disabledTesters = new List<TimeRewindTester>();
        private readonly Dictionary<EnemyRangedAttack, LineRenderer> _lasers = new Dictionary<EnemyRangedAttack, LineRenderer>();
        private readonly List<EnemyRangedAttack> _deadLasers = new List<EnemyRangedAttack>();
        private PlayerController _player;
        private SteamTank _steam;
        private Camera _camera;
        private Material _laserMaterial;
        private Transform _lockTarget;
        private EnemyAI _lockedEnemy;
        private bool _savedPlayerHitKeys, _ownsTestKeys;
        private float _nextLookup;
        private string _helpText = BuildHelpText(25);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                EnsureScene(SceneManager.GetSceneAt(i));
            }
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureScene(scene);
        private static void EnsureScene(Scene scene)
        {
            foreach (EnemyDebugTools existing in FindObjectsByType<EnemyDebugTools>())
            {
                if (existing.gameObject.scene == scene)
                {
                    existing.Bind();
                    return;
                }
            }
            GameObject host = new GameObject("EnemyDebugTools");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<EnemyDebugTools>();
        }
        private void OnEnable() => Bind();
        private void Bind()
        {
            DebugView.Changed -= OnDebugViewChanged;
            DebugView.Changed += OnDebugViewChanged;
            DebugView.Unregister(DrawLines);
            DebugView.Register(DrawLines);
        }
        private void OnDisable()
        {
            DebugView.Changed -= OnDebugViewChanged;
            DebugView.Unregister(DrawLines);
            RestoreTestKeys();
            foreach (LineRenderer laser in _lasers.Values)
            {
                if (laser != null)
                {
                    Destroy(laser.gameObject);
                }
            }
            _lasers.Clear();
            if (_laserMaterial != null)
            {
                Destroy(_laserMaterial);
            }
        }
        private void FindPlayerTools()
        {
            // 참조를 바꾸기 전에 실제로 차단했던 대상에만 원래 값을 돌려줍니다.
            RestoreTestKeys();
            _nextLookup = Time.time + 0.5f;
            if (_player == null || !_player.isActiveAndEnabled)
            {
                _player = FindAnyObjectByType<PlayerController>();
                if (_player != null)
                {
                    _helpText = BuildHelpText(_player.heavyHitThreshold);
                }
            }
            if (_steam == null || !_steam.isActiveAndEnabled)
            {
                _steam = FindAnyObjectByType<SteamTank>();
            }
            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }
        private void OnDebugViewChanged(bool on)
        {
            if (!on)
            {
                RestoreTestKeys();
                return;
            }
            if (gameObject.scene != SceneManager.GetActiveScene())
            {
                return;
            }
            FindPlayerTools();
            TakeTestKeys();
        }
        private void TakeTestKeys()
        {
            if (_ownsTestKeys)
            {
                return;
            }
            if (_player != null)
            {
                _savedPlayerHitKeys = _player.debugHitKeys;
                _player.debugHitKeys = false;
            }
            foreach (TimeRewindTester tester in FindObjectsByType<TimeRewindTester>())
            {
                if (tester.enabled)
                {
                    tester.enabled = false;
                    _disabledTesters.Add(tester);
                }
            }
            _ownsTestKeys = true;
        }
        private void RestoreTestKeys()
        {
            if (!_ownsTestKeys)
            {
                return;
            }
            if (_player != null)
            {
                _player.debugHitKeys = _savedPlayerHitKeys;
            }
            foreach (TimeRewindTester tester in _disabledTesters)
            {
                if (tester != null)
                {
                    tester.enabled = true;
                }
            }
            _disabledTesters.Clear();
            _ownsTestKeys = false;
        }
        private void Update()
        {
            if (gameObject.scene != SceneManager.GetActiveScene())
            {
                RestoreTestKeys();
                return;
            }
            DebugView.PollToggleKey();
            if (!DebugView.Enabled)
            {
                return;
            }
            // 늦게 생긴 테스트 입력도 0.5초마다 인계하고, 기존 플레이어 참조는 재사용합니다.
            if (Time.time >= _nextLookup)
            {
                FindPlayerTools();
            }
            TakeTestKeys();
            Keyboard keys = Keyboard.current;
            if (keys == null)
            {
                return;
            }
            if (Pressed(keys, 1) || Pressed(keys, 2))
            {
                if (_player != null)
                {
                    int damage = Pressed(keys, 1) ? 1 : _player.heavyHitThreshold;
                    _player.TakeHit(damage, _player.transform.position + _player.transform.forward);
                    Debug.Log("[YPH 테스트] 플레이어 피격 " + damage, _player);
                }
                else
                {
                    Debug.Log("[YPH 테스트] 플레이어를 찾지 못했습니다.");
                }
            }
            if (Pressed(keys, 3) || Pressed(keys, 4))
            {
                if (_steam != null)
                {
                    if (Pressed(keys, 3))
                    {
                        bool consumed = _steam.TryConsume(DebugSteamAmount);
                        Debug.Log("[YPH 테스트] 증기 소비 " + (consumed ? "성공" : "부족"), _steam);
                    }
                    else
                    {
                        _steam.Refill(DebugSteamAmount);
                    }
                }
                else
                {
                    Debug.Log("[YPH 테스트] 증기통을 찾지 못했습니다.");
                }
            }
            bool parry = Pressed(keys, 5), hp = Pressed(keys, 7);
            if (!(parry || hp))
            {
                return;
            }
            EnemyAI target = FindTarget(out _);
            if (target == null)
            {
                Debug.Log("[YPH 테스트] 살아 있는 적이 없습니다.");
                return;
            }
            if (parry)
            {
                target.ForceParried();
            }
            if (hp)
            {
                EnemyHpBar bar = target.GetComponent<EnemyHpBar>();
                if (bar != null)
                {
                    bar.Activate();
                }
                else
                {
                    Debug.Log("[YPH 테스트] EnemyHpBar가 없습니다.", target);
                }
            }
        }
        private static bool Pressed(Keyboard keys, int number)
        {
            switch (number)
            {
                case 1: return keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame;
                case 2: return keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame;
                case 3: return keys.digit3Key.wasPressedThisFrame || keys.numpad3Key.wasPressedThisFrame;
                case 4: return keys.digit4Key.wasPressedThisFrame || keys.numpad4Key.wasPressedThisFrame;
                case 5: return keys.digit5Key.wasPressedThisFrame || keys.numpad5Key.wasPressedThisFrame;
                case 7: return keys.digit7Key.wasPressedThisFrame || keys.numpad7Key.wasPressedThisFrame;
                default: return false;
            }
        }
        private EnemyAI FindTarget(out bool byLockOn)
        {
            Transform current = _player != null && _player.LockTarget != null ? _player.LockTarget.transform : null;
            if (current != _lockTarget)
            {
                _lockTarget = current;
                _lockedEnemy = current != null ? current.GetComponentInParent<EnemyAI>() : null;
            }
            byLockOn = _lockedEnemy != null && _lockedEnemy.isActiveAndEnabled && _lockedEnemy.Health.IsAlive;
            if (byLockOn)
            {
                return _lockedEnemy;
            }
            Vector3 origin = _player != null ? _player.transform.position : _camera != null ? _camera.transform.position : transform.position;
            EnemyAI best = null;
            float distance = float.MaxValue;
            foreach (EnemyAI enemy in EnemyAI.ActiveEnemies)
            {
                if (enemy == null || !enemy.enabled || !enemy.Health.IsAlive)
                {
                    continue;
                }
                float candidate = (enemy.transform.position - origin).sqrMagnitude;
                if (candidate < distance)
                {
                    distance = candidate;
                    best = enemy;
                }
            }
            return best;
        }
        /// <summary>플레이어를 찾았을 때 안내문을 만들어 그리기마다 문자열을 만들지 않습니다.</summary>
        private static string BuildHelpText(int threshold)
        {
            return "<b>개발용 표시 ON</b>  0: 끄기\n" +
                "<b>플레이어</b>\n1: 작은 경직(피해 1)  2: 큰 경직(피해 " + threshold + ")\n3: 증기 -20  4: 증기 +20\nCtrl: 붉은 동심원 받아치기(기본 증기 소모 없음)\n" +
                "<b>대상 적: 락온, 없으면 가장 가까운 적</b>\n5: 패링당함  7: 체력바\n" +
                "<b>원</b> 노랑: 인지  옅은 노랑: 놓침\n빨강: 근접 사거리/원거리 후퇴  자주: 강공격/킥\n초록: 원거리 유지/사격 거리\n" +
                "<b>빨간 상자</b> 실제 공격 판정\n<b>레이저</b> 빨강: 조준  노랑: 발사 방향";
        }
        private void OnGUI()
        {
            if (!DebugView.Enabled || gameObject.scene != SceneManager.GetActiveScene())
            {
                return;
            }
            Rect help = DebugView.DrawBox(_helpText, Vector2.zero);
            EnemyAI target = FindTarget(out bool locked);
            string info = "<b>대상 적</b>\n없음";
            if (target != null)
            {
                info = "<b>대상 적</b> (" + (locked ? "락온" : "가장 가까움") + ")\n" + target.name +
                    "\n종류: " + (target.UsesRanged ? "원거리" : "근접") + "\n상태: " + target.CurrentState +
                    "\nHP: " + Mathf.CeilToInt(target.Health.CurrentHp) + " / " + Mathf.CeilToInt(target.Health.MaxHp);
            }
            if (_player != null)
            {
                info += "\n\n플레이어 HP: " + _player.Hp + " / " + _player.maxHp;
            }
            if (_steam != null)
            {
                info += "\n증기: " + _steam.CurrentPressure.ToString("0") + " / " + _steam.MaxPressure.ToString("0");
            }
            DebugView.DrawBox(info, new Vector2(help.xMax, 0f));
        }
        private void DrawLines()
        {
            foreach (EnemyAI enemy in EnemyAI.ActiveEnemies)
            {
                if (enemy == null || enemy.gameObject.scene != gameObject.scene)
                {
                    continue;
                }
                enemy.DrawDebugLines();
                if (enemy.Melee != null && enemy.Melee.isActiveAndEnabled)
                {
                    enemy.Melee.DrawDebugLines();
                }
                if (enemy.UsesRanged)
                {
                    enemy.Ranged.DrawDebugLines();
                }
            }
        }
        private void LateUpdate()
        {
            _deadLasers.Clear();
            foreach (KeyValuePair<EnemyRangedAttack, LineRenderer> pair in _lasers)
            {
                if (pair.Key == null)
                {
                    if (pair.Value != null)
                    {
                        Destroy(pair.Value.gameObject);
                    }
                    _deadLasers.Add(pair.Key);
                }
                else if (pair.Value != null)
                {
                    pair.Value.enabled = false;
                }
            }
            foreach (EnemyRangedAttack dead in _deadLasers)
            {
                _lasers.Remove(dead);
            }
            if (!DebugView.Enabled)
            {
                return;
            }
            foreach (EnemyAI enemy in EnemyAI.ActiveEnemies)
            {
                if (enemy == null || enemy.gameObject.scene != gameObject.scene || !enemy.UsesRanged || !enemy.Ranged.ShowLaser)
                {
                    continue;
                }
                EnemyRangedAttack ranged = enemy.Ranged;
                if (!_lasers.TryGetValue(ranged, out LineRenderer laser))
                {
                    if (_laserMaterial == null)
                    {
                        Shader shader = Shader.Find("Sprites/Default");
                        if (shader == null)
                        {
                            shader = Shader.Find("Universal Render Pipeline/Unlit");
                        }
                        _laserMaterial = new Material(shader);
                    }
                    GameObject beam = new GameObject("EnemyAimLaser");
                    beam.transform.SetParent(transform);
                    laser = beam.AddComponent<LineRenderer>();
                    laser.sharedMaterial = _laserMaterial;
                    laser.useWorldSpace = true;
                    laser.positionCount = 2;
                    laser.widthMultiplier = 0.025f;
                    laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    laser.receiveShadows = false;
                    _lasers.Add(ranged, laser);
                }
                Vector3 direction = ranged.LaserDirection;
                direction = direction.sqrMagnitude < 0.0001f ? ranged.transform.forward : direction.normalized;
                float length = ranged.LaserRange;
                if (Physics.Raycast(ranged.FirePosition, direction, out RaycastHit hit, length, ranged.LaserMask, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(ranged.transform)) { length = hit.distance; }
                Color color = ranged.IsAiming ? new Color(1f, 0.1f, 0.1f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.9f);
                laser.startColor = laser.endColor = color;
                laser.SetPosition(0, ranged.FirePosition);
                laser.SetPosition(1, ranged.FirePosition + direction * length);
                laser.enabled = true;
            }
        }
#if UNITY_EDITOR
        internal static void DrawCircle(Vector3 center, float radius, Color color)
        {
            if (radius <= 0f)
            {
                return;
            }
            Gizmos.color = color;
            Vector3 previous = center + Vector3.right * radius;
            for (int i = 1; i <= 48; i++)
            {
                float angle = i * Mathf.PI * 2f / 48;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
        internal static void DrawHitbox(Transform owner, Vector3 offset, Vector3 size, Color color)
        {
            Gizmos.matrix = Matrix4x4.TRS(owner.TransformPoint(offset), owner.rotation, Vector3.one);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = color;
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
#endif
