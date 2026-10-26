using System.Collections.Generic;
using NGH;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 개발자 기능 (디버깅메뉴 D-064). 에디터·개발 빌드에서만 생긴다 (최종 빌드 제외). 따로 창을 띄우지 않는다.
    ///   ` (~ 키) 또는 Insert = 개발용 표시(YPH DebugView: 범위 원·판정 상자·왼쪽 위 설명) 켜기·끄기
    ///   표시가 켜진 동안 Alt를 누르고 있으면 마우스가 나오고 게임이 멈춘다. 왼쪽 위 상자에 마우스를 대면 그 자리에 기능 판이 펼쳐져 눌러 쓴다
    ///
    ///   무적       체력만 줄지 않는다. 맞는 판정·피격(소/대) 모션·경직은 그대로
    ///   원펀맨     일반 적은 한 대에 쓰러진다. 보스는 맞힌 부위(다리·신핵)만 한 대에 부서진다
    ///   무한 증기  켜 둔 동안 증기 최대치 고정. 피격·체력 감소는 그대로(무적은 따로 켬). 시작 튜토리얼(충전 시연) 중에는 멈춤
    ///   검 이펙트  강공격 시안: 기존(NGH) → C2 붉은 칼금 → C3 붉은 김 → 자동(증기 가득 C3) (NmjSwordSlash)
    ///   이동       시작 지점 / 육조거리 / 보스방 입구(안개문 앞, 들어가기 전)
    ///   리젠       지금 구역의 적을 모두 치우고 다시 세운다
    ///              육조거리(보스방 밖) = 일반 적 / 보스방 = 보스
    ///              리젠으로 치운 보스는 처치 문구·엔딩이 나오지 않는다. 직접 쓰러뜨리면 정상 엔딩
    ///
    /// 팀원 코드는 고치지 않고 공개된 값·이벤트만 쓴다.
    ///  - 무적: 켜는 동안 최대 체력을 크게 잡아 체력이 0이 되지 않게 하고(PlayerController.TakeHit는 그대로 돎),
    ///    피격 동작이 끝나면 다시 가득 채운다. 끌 때 원래 최대 체력·체력으로 되돌린다.
    ///  - 원펀맨: 일반 적은 EnemyHealth.AnyDamaged를 받아 그 프레임 끝에 남은 체력만큼 더 준다.
    ///    보스 부위는 지금 플레이어 공격이 닿지 않아서(BHS 테스트 탄환만 닿음), 칼 휘두름·총알 끝점에 부위가 있으면 바로 부순다.
    ///  - 보스 리젠: 보스방의 현무(BHS_HyunMu)를 본으로 복제해 BHS가 놓은 자리에 다시 세운다 (없으면 꺼진 채 놓인 BossHealth 시험 보스를 보스방 표식에).
    /// </summary>
    [DisallowMultipleComponent]
    public class NmjDevWindow : MonoBehaviour
    {
        // 개발용 표시 켜기·끄기: ` (~ 키, 숫자 1 왼쪽) 또는 Insert
        private static readonly Key[] ToggleKeys = { Key.Backquote, Key.Insert };
        private const int InvincibleMaxHp = 9999;

        // 위치 (찾는 이름이 없을 때의 값은 Common/Main 기준)
        private const string BossArenaName = "NMJ_BossArena";
        private const string BossSpawnName = "NMJ_BossSpawn_Hyeonmu";
        private const string HyeonmuName = "BHS_HyunMu";
        private const string GateFrontName = "NMJ_FogGate_돌아온자리";
        private const string BossGateName = "NMJ_FogGate_향원정";   // 안개문 — 도착 자리(Target)가 보스방 입구

        // 순간이동 — 레벨 배치 v4 구간 (NmjLevelLayoutBuilder, 맵구상 2장). 향원정 → 광화문 → 육조거리, 모두 남쪽을 보고 선다
        private struct Warp { public string label; public Vector3 pos; }
        private static readonly Warp[] Warps =
        {
            new Warp { label = "2. 근정전 마당 · 첫 전투 포졸 2", pos = new Vector3(0f, 0.1f, 14f) },
            new Warp { label = "3. 비망록 · 궁수 · 첫 받아치기", pos = new Vector3(-3f, 0.1f, -4f) },
            new Warp { label = "4. 광화문 안쪽 · 얼음 문 전투", pos = new Vector3(0f, 0.1f, -24f) },
            new Warp { label = "5. 광화문 밖 · 육조거리", pos = new Vector3(0f, 0.1f, -56f) },
        };
        private static readonly Vector3 GateFrontFallback = new Vector3(0f, 0.1f, -85f);  // 육조거리 안개문(z -90) 바로 앞
        private const float BossArenaRadius = 45f;

        private static NmjDevWindow instance;

        /// <summary>Alt로 마우스 기능 판을 쓰는 중 (ESC 메뉴는 이때 열리지 않는다)</summary>
        public static bool IsOpen => instance != null && instance.open;
        /// <summary>열려 있거나 방금(같은 ESC로) 닫힘 → ESC 메뉴가 열리면 안 됨</summary>
        public static bool BlocksEscape => instance != null && (instance.open || Time.frameCount - instance.closedFrame <= 1);
        public static bool Invincible { get; private set; }
        public static bool OnePunch { get; private set; }
        public static bool InfiniteSteam { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; Invincible = false; OnePunch = false; InfiniteSteam = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (!Debug.isDebugBuild || instance != null) return;
            var go = new GameObject("NMJ_DevWindow");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NmjDevWindow>();
        }

        // 씬 상태
        private PlayerController player;
        private Vector3 startPos;
        private Quaternion startRot;
        private bool hasStart;
        private GameObject bossTemplate;          // 보스방의 현무(BHS_HyunMu) 또는 꺼진 채 놓인 BossHealth 보스 (복제 본)
        private Vector3 bossHomePos;
        private Quaternion bossHomeRot;
        private bool bossUseHome;               // 보스방 현무는 BHS가 놓은 자리 그대로, 시험용 보스는 보스방 표식 자리
        private readonly List<GameObject> spawnedBosses = new List<GameObject>();

        // 무적
        private int savedMaxHp, savedHp;
        private PlayerController invinciblePlayer;

        // 원펀맨
        private readonly List<EnemyHealth> killQueue = new List<EnemyHealth>();
        private SteamGun gun;
        private PlayerController.ActionState lastAction;
        private readonly HashSet<Object> brokenThisSwing = new HashSet<Object>();

        // 창
        private bool open;
        private float prevTimeScale = 1f;
        private SteamTank[] tanks;
        private int closedFrame = -10;

        // ───────── 시작 ─────────

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            EnemyHealth.AnyDamaged -= OnEnemyDamaged;
            if (instance == this) instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            ExitMouseMode();
            invinciblePlayer = null;
            spawnedBosses.Clear();
            killQueue.Clear();
            gun = null;
            tanks = null;
            FindPlayer();
            hasStart = player != null;
            if (hasStart) { startPos = player.transform.position; startRot = player.transform.rotation; }

            // 복제 본: 보스방의 현무(BHS_HyunMu)가 있으면 그것, 없으면 꺼진 채 놓인 BossHealth 보스
            bossTemplate = null;
            bossUseHome = false;
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name.StartsWith(HyeonmuName) && t.gameObject.scene.IsValid()) { bossTemplate = t.gameObject; bossUseHome = true; break; }
            if (bossTemplate == null)
                foreach (BossHealth b in FindObjectsByType<BossHealth>(FindObjectsInactive.Include))
                {
                    if (!b.gameObject.scene.IsValid()) continue;
                    if (bossTemplate == null || !b.gameObject.activeInHierarchy) bossTemplate = b.gameObject;
                }
            if (bossTemplate != null) { bossHomePos = bossTemplate.transform.position; bossHomeRot = bossTemplate.transform.rotation; }
            if (Invincible) ApplyInvincible(true);
        }

        private void FindPlayer()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (TogglePressed(kb)) ToggleDisplay();
            }
            UpdateMouseMode(kb);

            FindPlayer();
            if (Invincible) KeepInvincible();
            if (OnePunch) WatchBossParts();
        }

        private static bool TogglePressed(Keyboard kb)
        {
            foreach (Key k in ToggleKeys) if (kb[k].wasPressedThisFrame) return true;
            return false;
        }

        private void LateUpdate()
        {
            if (InfiniteSteam) KeepSteamFull();

            // 원펀맨: 맞은 일반 적을 프레임 끝에 쓰러뜨린다 (맞은 그 자리에서 바로 죽이면 Died가 두 번 날 수 있음)
            if (killQueue.Count == 0) return;
            foreach (EnemyHealth e in killQueue)
                if (e != null && e.IsAlive) e.TakeDamage(e.CurrentHp, e.transform.position);
            killQueue.Clear();
        }

        // ───────── 무적 ─────────

        private void SetInvincible(bool on)
        {
            Invincible = on;
            ApplyInvincible(on);
            RefreshLabels();
        }

        private void ApplyInvincible(bool on)
        {
            if (on)
            {
                if (player == null || invinciblePlayer == player) return;
                invinciblePlayer = player;
                savedMaxHp = player.maxHp;
                savedHp = player.Hp;
                player.maxHp = InvincibleMaxHp;
                SetPlayerHp(player, InvincibleMaxHp);
            }
            else if (invinciblePlayer != null)
            {
                PlayerController p = invinciblePlayer;
                invinciblePlayer = null;
                p.maxHp = savedMaxHp;
                SetPlayerHp(p, Mathf.Clamp(savedHp, 1, savedMaxHp));
            }
        }

        private void KeepInvincible()
        {
            if (player != null && invinciblePlayer != player) ApplyInvincible(true);
            if (invinciblePlayer == null) return;
            // 맞는 동안은 건드리지 않는다 (피격 모션을 끊지 않게). 끝나면 가득 채운다
            PlayerController p = invinciblePlayer;
            if (p.Hp < InvincibleMaxHp && !p.IsHitReacting && !p.IsDead && p.CurrentAction == PlayerController.ActionState.None)
                SetPlayerHp(p, InvincibleMaxHp);
        }

        /// <summary>PlayerController에 체력을 바로 넣는 공개 함수가 없어 시간 역행 기록(체력만 바꿈)으로 넣는다</summary>
        private static void SetPlayerHp(PlayerController p, int hp)
        {
            if (p.IsDead || p.CurrentAction != PlayerController.ActionState.None)
            {
                if (hp > p.Hp) p.Heal(hp - p.Hp);   // 동작 중이면 회복으로만 (모션을 건드리지 않음)
                return;
            }
            PlayerController.RewindState s = p.CaptureRewindState();
            s.hp = hp;
            p.RestoreRewindState(s);
        }

        // ───────── 무한 증기 ─────────

        /// <summary>켜 둔 동안 증기를 최대치로 고정한다. 쓰는 쪽(사격·강공격·시간 역행)은 그대로 돌고 매 프레임 끝에 다시 채운다.
        /// 피격·체력은 건드리지 않는다(무적은 따로). 시작 튜토리얼(충전 시연) 중에는 멈춘다</summary>
        private void KeepSteamFull()
        {
            if (NmjStartTutorial.IsRunning) return;
            if (tanks == null || tanks.Length == 0 || tanks[0] == null) tanks = NmjStartTutorial.FindTanks();   // 플레이어 몸·증기통 탱크 모두
            foreach (SteamTank t in tanks)
                if (t != null && t.CurrentPressure < t.MaxPressure) t.RestorePressure(t.MaxPressure);
        }

        // ───────── 원펀맨 ─────────

        private void SetOnePunch(bool on)
        {
            OnePunch = on;
            EnemyHealth.AnyDamaged -= OnEnemyDamaged;
            if (on) EnemyHealth.AnyDamaged += OnEnemyDamaged;
            if (gun != null) gun.OnFired -= OnGunFired;
            gun = null;
            RefreshLabels();
        }

        private void OnEnemyDamaged(EnemyHealth e, float amount)
        {
            if (e != null && !killQueue.Contains(e)) killQueue.Add(e);
        }

        private void WatchBossParts()
        {
            if (player == null) return;

            // 총: 발사마다 총알 끝점에 부위가 있으면 부순다
            if (gun == null)
            {
                gun = player.GetComponentInChildren<SteamGun>(true);
                if (gun == null) gun = FindAnyObjectByType<SteamGun>();
                if (gun != null) gun.OnFired += OnGunFired;
            }

            // 칼: 공격 동작 동안 칼끝 자리(PlayerController.DoHit와 같은 자리)에 부위가 있으면 부순다
            PlayerController.ActionState a = player.CurrentAction;
            if (a != lastAction) brokenThisSwing.Clear();
            lastAction = a;
            if (!IsMeleeAttack(a)) return;
            Weapon w = player.weapons ? player.weapons.Current : null;
            float reach = w ? w.reach : 0.9f, radius = w ? w.hitRadius : 0.6f;
            Vector3 center = player.transform.position + Vector3.up * 0.5f + player.transform.forward * reach;
            BreakBossPartsAt(center, radius + 0.4f);
        }

        private static bool IsMeleeAttack(PlayerController.ActionState a) =>
            a == PlayerController.ActionState.LightAttack || a == PlayerController.ActionState.HeavyAttack ||
            a == PlayerController.ActionState.LightCombo2 || a == PlayerController.ActionState.LightCombo3 ||
            a == PlayerController.ActionState.LightCombo4 || a == PlayerController.ActionState.LightCombo5;

        private void OnGunFired(Vector3 muzzle, Vector3 end, bool hitEnemy)
        {
            if (OnePunch) BreakBossPartsAt(end, 0.5f);
        }

        private void BreakBossPartsAt(Vector3 center, float radius)
        {
            Vector3 from = player != null ? player.transform.position : center;
            foreach (Collider col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide))
            {
                BossWallHealth leg = col.GetComponentInParent<BossWallHealth>();
                if (leg != null && leg.currentHp > 0f && brokenThisSwing.Add(leg))
                {
                    leg.TakeDamage(leg.currentHp, from);
                    continue;
                }
                BossHealth boss = col.GetComponentInParent<BossHealth>();
                if (boss != null && boss.IsCoreExposed && boss.IsAlive && col == boss.coreCollider && brokenThisSwing.Add(boss))
                    boss.TakeDamage(boss.CurrentHp, from);   // 신핵 → 정상 처치 (처치 문구·엔딩)
            }
        }

        // ───────── 이동 ─────────

        private void MoveToStart()
        {
            if (hasStart) Teleport(startPos, startRot);
        }

        private void MoveToBossGate()
        {
            Transform t = FindInScene(GateFrontName);
            Teleport(t != null ? t.position : GateFrontFallback, Quaternion.Euler(0f, 180f, 0f));   // 안개문을 본다
        }

        /// <summary>안개문을 지난 것과 같은 자리 (안개문 Target)</summary>
        private void MoveToBossRoom()
        {
            Transform gate = FindInScene(BossGateName);
            FogGateTeleport fg = gate != null ? gate.GetComponent<FogGateTeleport>() : null;
            if (fg == null || fg.Target == null) { Debug.LogWarning("[NMJ 개발자 창] 보스방 안개문(NMJ_FogGate_향원정)을 찾지 못했습니다"); return; }
            Teleport(fg.Target.position, fg.Target.rotation);
        }

        private void Teleport(Vector3 pos, Quaternion rot)
        {
            Close();
            FindPlayer();
            if (player == null) return;
            Transform body = player.transform;
            InkTransition.Play(InkStyle.Drip, () =>
            {
                if (body == null) return;
                CharacterController cc = body.GetComponent<CharacterController>();
                bool ccOn = cc != null && cc.enabled;
                if (ccOn) cc.enabled = false;
                body.SetPositionAndRotation(pos, rot);
                if (ccOn) cc.enabled = true;
                Physics.SyncTransforms();
            }, 0.6f, 0.6f, 0.1f);
        }

        // ───────── 리젠 ─────────

        private bool InBossArena()
        {
            if (player == null) return false;
            Transform arena = FindInScene(BossArenaName);
            if (arena == null) return false;
            Vector3 d = player.transform.position - arena.position;
            d.y = 0f;
            return d.magnitude <= BossArenaRadius;
        }

        private void Respawn()
        {
            Close();
            if (InBossArena()) RespawnBoss();
            else RespawnEnemies();
        }

        /// <summary>일반 적: 죽은 적·살아 있는 적 모두 체력을 채워 제자리에서 다시 세운다 (쓰러짐 보고 없이)</summary>
        private void RespawnEnemies()
        {
            killQueue.Clear();
            int n = 0;
            foreach (EnemyHealth e in FindObjectsByType<EnemyHealth>())
            {
                e.Revive();
                n++;
            }
            Debug.Log($"[NMJ 개발자 창] 일반 적 {n}명 다시 세움");
        }

        /// <summary>보스: 켜진 보스를 모두 치우고(쓰러뜨리지 않고 지움 → 처치 문구·엔딩 없음) 본을 복제해 다시 세운다</summary>
        private void RespawnBoss()
        {
            if (bossTemplate == null) { Debug.LogWarning("[NMJ 개발자 창] 씬에 보스(BHS_HyunMu 또는 BossHealth)가 없어 다시 세울 수 없습니다"); return; }

            // 앞서 리젠한 보스와, 켜져 있는 시험용 보스를 치운다 (쓰러뜨리지 않고 지움)
            foreach (GameObject g in spawnedBosses) if (g != null) Destroy(g);
            spawnedBosses.Clear();
            foreach (BossHealth b in FindObjectsByType<BossHealth>())
                if (b.gameObject != bossTemplate && !b.transform.IsChildOf(bossTemplate.transform)) Destroy(b.gameObject);

            // 본은 끄고 복제만 쓴다 (본은 다음 리젠에도 깨끗한 상태로 남는다)
            if (bossTemplate.activeSelf) bossTemplate.SetActive(false);

            Transform spawn = bossUseHome ? null : FindInScene(BossSpawnName);
            Vector3 pos = spawn != null ? spawn.position : bossHomePos;
            Quaternion rot = spawn != null ? spawn.rotation : bossHomeRot;
            GameObject clone = Instantiate(bossTemplate, pos, rot, bossTemplate.transform.parent);
            clone.name = bossTemplate.name + " (개발자 리젠)";
            clone.SetActive(true);
            spawnedBosses.Add(clone);

            foreach (BossDefeatBanner banner in FindObjectsByType<BossDefeatBanner>()) banner.ForgetBoss();
            foreach (HyeonmuBossBar bar in FindObjectsByType<HyeonmuBossBar>()) bar.ResetBar();
            Debug.Log("[NMJ 개발자 창] 보스 다시 세움 (리젠으로 치운 보스는 처치·엔딩 없음)");
        }

        private static Transform FindInScene(string name)
        {
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name == name && t.gameObject.scene.IsValid()) return t;
            return null;
        }

        // ───────── 개발용 표시 + 마우스 기능 판 ─────────
        // ` (또는 Insert) = 개발용 표시(YPH DebugView) 켜기·끄기. 켜진 동안 Alt를 누르고 있으면 마우스가 나오고(게임 멈춤),
        // 왼쪽 위 '개발용 표시' 상자에 마우스를 대면 그 자리에 기능 판이 펼쳐진다. 따로 창을 띄우지 않는다.

        private static bool DevDisplay
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            get => DebugView.Enabled;
            set => DebugView.SetEnabled(value);
#else
            get => false;
            set { }
#endif
        }

        private void ToggleDisplay()
        {
            DevDisplay = !DevDisplay;
            if (DevDisplay) NmjSubtitle.Caption("개발용 표시 켜짐 — Alt를 누른 채 왼쪽 위 상자에 마우스를 대면 개발자 기능", 3f);
            else ExitMouseMode();
        }

        /// <summary>Alt를 누르는 동안: 마우스 보이기, 플레이어 입력·카메라·시간 멈춤</summary>
        private void UpdateMouseMode(Keyboard kb)
        {
            bool want = DevDisplay && kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed)
                        && !EndingCredits.IsPlaying && !PauseMenu.IsOpen;
            if (want && !open) EnterMouseMode();
            else if (!want && open) ExitMouseMode();
        }

        private void EnterMouseMode()
        {
            open = true;
            panelShown = false;
            ControlsOverlay.MenuOpen = true;
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ExitMouseMode()
        {
            if (!open) return;
            open = false;
            panelShown = false;
            closedFrame = Time.frameCount;
            ControlsOverlay.MenuOpen = false;
            Time.timeScale = prevTimeScale > 0f ? prevTimeScale : 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>기능을 누른 뒤 판을 접는다 (Alt를 떼면 게임이 이어진다)</summary>
        private void Close() => panelShown = false;

        private void RefreshLabels() { }

        private static string SwordFxShort(NmjSwordSlash.Mode m) => m switch
        {
            NmjSwordSlash.Mode.C2 => "C2 붉은 칼금",
            NmjSwordSlash.Mode.C3 => "C3 붉은 김",
            NmjSwordSlash.Mode.Auto => "자동 C2/C3",
            NmjSwordSlash.Mode.Flair => "화려판",
            _ => "기존",
        };

        private static string OnOff(bool on) => on ? "<color=#F0BA29>켜짐</color>" : "<color=#888888>꺼짐</color>";

        private struct Item { public string label; public System.Action act; public bool small; }

        private List<Item> Items()
        {
            var list = new List<Item>
            {
                new Item { label = "무적  " + OnOff(Invincible), act = () => SetInvincible(!Invincible) },
                new Item { label = "원펀맨  " + OnOff(OnePunch), act = () => SetOnePunch(!OnePunch) },
                new Item { label = "무한 증기  " + OnOff(InfiniteSteam) + (InfiniteSteam && NmjStartTutorial.IsRunning ? " (충전 튜토리얼 중 멈춤)" : ""),
                           act = () => InfiniteSteam = !InfiniteSteam },
                new Item { label = "검 이펙트  <color=#F0BA29>" + SwordFxShort(NmjSwordSlash.Current) + "</color>", act = () =>
                {
                    NmjSwordSlash.Mode m = NmjSwordSlash.Cycle();
                    NmjSubtitle.Caption("검 이펙트 시안: " + NmjSwordSlash.Label(m), 2f);
                } },
                new Item { label = "리젠 — 지금 구역 적 다시", act = Respawn },
                new Item { label = "이동 1. 시작 지점 · 허수아비", act = MoveToStart, small = true },
            };
            foreach (Warp w in Warps)
            {
                Vector3 to = w.pos;
                list.Add(new Item { label = "이동 " + w.label, act = () => Teleport(to, Quaternion.Euler(0f, 180f, 0f)), small = true });
            }
            list.Add(new Item { label = "이동 6. 안개문 앞 · 육조거리", act = MoveToBossGate, small = true });
            list.Add(new Item { label = "이동 7. 보스방 안", act = MoveToBossRoom, small = true });
            return list;
        }

        // 판 그리기 (IMGUI — YPH 개발용 표시 상자와 같은 모양)
        private bool panelShown;
        private Rect panelRect;
        private GUIStyle boxStyle, itemStyle, smallStyle, headStyle;
        private Texture2D bgTex, hoverTex;

        private void EnsureStyles(float scale)
        {
            if (boxStyle == null || bgTex == null)
            {
                bgTex = Tex(new Color(0f, 0f, 0f, 0.82f));
                hoverTex = Tex(new Color(0.94f, 0.73f, 0.16f, 0.35f));
                boxStyle = new GUIStyle { normal = { background = bgTex } };
                itemStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, alignment = TextAnchor.MiddleLeft };
                itemStyle.normal.textColor = Color.white;
                itemStyle.hover.background = hoverTex;
                itemStyle.hover.textColor = Color.white;
                smallStyle = new GUIStyle(itemStyle);
                smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
                headStyle = new GUIStyle(itemStyle);
                headStyle.normal.textColor = new Color(0.94f, 0.73f, 0.16f);
            }
            itemStyle.fontSize = Mathf.RoundToInt(16 * scale);
            smallStyle.fontSize = Mathf.RoundToInt(14 * scale);
            headStyle.fontSize = Mathf.RoundToInt(15 * scale);
            int pad = Mathf.RoundToInt(8 * scale);
            itemStyle.padding = smallStyle.padding = headStyle.padding = new RectOffset(pad, pad, 0, 0);
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!open) return;
            GUI.depth = -100;   // YPH 개발용 표시 상자 위에 그린다
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            EnsureStyles(scale);

            // 마우스를 대는 곳: 왼쪽 위 '개발용 표시' 상자 자리
            Vector2 mouse = Event.current.mousePosition;
            Rect trigger = new Rect(0f, 0f, 300f * scale, 240f * scale);
            if (!panelShown && trigger.Contains(mouse)) panelShown = true;
            else if (panelShown && !panelRect.Contains(mouse) && !trigger.Contains(mouse)) panelShown = false;

            if (!panelShown)
            {
                Rect tag = new Rect(12f * scale, 12f * scale, 300f * scale, 26f * scale);
                GUI.Box(tag, GUIContent.none, boxStyle);
                GUI.Label(tag, "◀ 마우스를 대면 개발자 기능", headStyle);
                return;
            }

            List<Item> items = Items();
            float rowH = 30f * scale, smallH = 26f * scale, headH = 30f * scale;
            float h = headH * 2f;
            foreach (Item it in items) h += it.small ? smallH : rowH;
            panelRect = new Rect(12f * scale, 12f * scale, 380f * scale, h + 8f * scale);
            GUI.Box(panelRect, GUIContent.none, boxStyle);

            float y = panelRect.y + 4f * scale;
            GUI.Label(new Rect(panelRect.x, y, panelRect.width, headH), "개발자 기능  <size=80%>(Alt 떼면 계속, ` 표시 끄기)</size>", headStyle);
            y += headH;
            GUI.Label(new Rect(panelRect.x, y, panelRect.width, headH), "구역: " + (InBossArena() ? "보스방" : "보스방 밖"), smallStyle);
            y += headH;
            foreach (Item it in items)
            {
                float rh = it.small ? smallH : rowH;
                if (GUI.Button(new Rect(panelRect.x, y, panelRect.width, rh), it.label, it.small ? smallStyle : itemStyle)) it.act();
                y += rh;
            }
        }
    }
}
