using System.Collections.Generic;
using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YPH;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 개발자 창 (디버깅메뉴 D-064). ` (~ 키) 또는 Insert 키로 열고 닫는다. 에디터·개발 빌드에서만 생긴다 (최종 빌드 제외).
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
        // 열기·닫기: ` (~ 키, 숫자 1 왼쪽) 또는 Insert
        private static readonly Key[] ToggleKeys = { Key.Backquote, Key.Insert };
        private const int InvincibleMaxHp = 9999;

        // 위치 (찾는 이름이 없을 때의 값은 Common/Main 기준)
        private const string BossArenaName = "NMJ_BossArena";
        private const string BossSpawnName = "NMJ_BossSpawn_Hyeonmu";
        private const string HyeonmuName = "BHS_HyunMu";
        private const string GateFrontName = "NMJ_FogGate_돌아온자리";
        private const string BossGateName = "NMJ_FogGate_향원정";   // 안개문 — 도착 자리(Target)가 보스방 입구

        // 순간이동 — 레벨 배치 v2 구간 (NmjLevelLayoutBuilder, 맵구상 2장). 모두 북쪽(광화문·향원정)을 보고 선다
        private struct Warp { public string label; public Vector3 pos; }
        private static readonly Warp[] Warps =
        {
            new Warp { label = "2. 첫 전투 앞 · 포졸 2", pos = new Vector3(0f, 0.1f, -94f) },
            new Warp { label = "3. 비망록 · 누각 궁수", pos = new Vector3(-4f, 0.1f, -84f) },
            new Warp { label = "4. 첫 받아치기 · 포졸 1", pos = new Vector3(0f, 0.1f, -77f) },
            new Warp { label = "5. 광화문 앞 · 얼음 문 전투", pos = new Vector3(0f, 0.1f, -66f) },
            new Warp { label = "6. 궁 안 · 광화문 안쪽", pos = new Vector3(0f, 0.1f, -30f) },
            new Warp { label = "7. 근정전 옆 · 길잡이", pos = new Vector3(27.5f, 0.1f, 38f) },
        };
        private static readonly Vector3 GateFrontFallback = new Vector3(0f, 0.1f, 76.5f);   // 근정전 월대(z 74까지)와 안개문(z 79.4) 사이
        private const float BossArenaRadius = 45f;

        private static NmjDevWindow instance;

        /// <summary>개발자 창이 열려 있음 (ESC 메뉴는 이때 열리지 않는다)</summary>
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
        private Style style;
        private CanvasGroup group;
        private TMP_Text info;
        private readonly List<MenuButton> buttons = new List<MenuButton>();
        private MenuButton invincibleButton, onePunchButton, steamButton, swordFxButton;
        private SteamTank[] tanks;
        private int closedFrame = -10;

        // ───────── 시작 ─────────

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            BuildUi();
            Show(false);
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
            if (open) Close();
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
                if (TogglePressed(kb)) { if (open) Close(); else Open(); }
                else if (open && kb.escapeKey.wasPressedThisFrame) Close();
            }

            FindPlayer();
            if (Invincible) KeepInvincible();
            if (OnePunch) WatchBossParts();

            if (open)
            {
                UpdateButtons(buttons, style);
                info.text = $"구역: {(InBossArena() ? "보스방" : "보스방 밖 (육조거리)")}";
            }
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
            Teleport(t != null ? t.position : GateFrontFallback, Quaternion.identity);
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

        // ───────── 창 ─────────

        private void Open()
        {
            if (open || EndingCredits.IsPlaying || PauseMenu.IsOpen) return;
            open = true;
            EnsureEventSystem();
            ControlsOverlay.MenuOpen = true;   // 플레이어 입력·카메라 멈춤
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshLabels();
            Show(true);
            Select(invincibleButton);
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            closedFrame = Time.frameCount;
            ControlsOverlay.MenuOpen = false;
            Time.timeScale = prevTimeScale > 0f ? prevTimeScale : 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Select(null);
            Show(false);
        }

        private void Show(bool on)
        {
            group.alpha = on ? 1f : 0f;
            group.blocksRaycasts = on;
            group.interactable = on;
        }

        private static string SwordFxShort(NmjSwordSlash.Mode m) => m switch
        {
            NmjSwordSlash.Mode.C2 => "C2 붉은 칼금",
            NmjSwordSlash.Mode.C3 => "C3 붉은 김",
            NmjSwordSlash.Mode.Auto => "자동 C2/C3",
            _ => "기존",
        };

        private void RefreshLabels()
        {
            if (invincibleButton == null) return;
            invincibleButton.label.text = $"무적  <color=#{(Invincible ? "F0BA29>켜짐" : "888888>꺼짐")}</color>";
            onePunchButton.label.text = $"원펀맨  <color=#{(OnePunch ? "F0BA29>켜짐" : "888888>꺼짐")}</color>";
            steamButton.label.text = $"무한 증기  <color=#{(InfiniteSteam ? "F0BA29>켜짐" : "888888>꺼짐")}</color>" + (InfiniteSteam && NmjStartTutorial.IsRunning ? "  <size=60%>(충전 튜토리얼 중 멈춤)</size>" : "");
            swordFxButton.label.text = $"검 이펙트  <color=#F0BA29>{SwordFxShort(NmjSwordSlash.Current)}</color>";
        }

        private void BuildUi()
        {
            TMP_FontAsset font = null;
#if UNITY_EDITOR
            font = FindDefaultFont();
#endif
            if (font == null)
                foreach (TMP_Text t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
                    if (t.font != null && t.font.name.Contains("Chosun")) { font = t.font; break; }

            style = new Style { font = font, buttonHeight = 54f, fontSize = 28f };
            Canvas canvas = NewCanvas("NMJ_DevWindowCanvas", transform, 950, out group);   // 모든 HUD·ESC 메뉴(90)·퀘스트 판(100) 위, 먹 전환(1000) 아래

            Image dim = NewImage("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.zero);
            dim.raycastTarget = true;
            // 한 창에 모두: 기능 6 + 순간이동 9 = 15줄. 1080 높이 화면에 들어가게 줄 높이를 맞춘다
            const float rowH = 50f, top = 112f, bottom = 60f;
            int rows = 6 + 2 + Warps.Length + 1;   // 기능 6 + 시작·안개문 앞 2 + 구간 + 보스방
            RectTransform panel = Panel(canvas.transform, new Vector2(600f, top + bottom + rowH * rows + 34f), new Color(0.06f, 0.06f, 0.07f, 0.96f), Brass);

            TMP_Text title = NewText("Title", panel, font, "개발자 창", 32f, Brass);
            RectTransform tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(0f, 46f); tr.anchoredPosition = new Vector2(0f, -22f);

            info = NewText("Info", panel, font, "", 18f, White);
            RectTransform ir = info.rectTransform;
            ir.anchorMin = new Vector2(0f, 1f); ir.anchorMax = new Vector2(1f, 1f); ir.pivot = new Vector2(0.5f, 1f);
            ir.sizeDelta = new Vector2(0f, 26f); ir.anchoredPosition = new Vector2(0f, -70f);
            info.alpha = 0.6f;

            RectTransform list = NewRect("Buttons", panel);
            list.offsetMin = new Vector2(40f, bottom);
            list.offsetMax = new Vector2(-40f, -top);
            style.buttonHeight = rowH;
            style.fontSize = 25f;

            invincibleButton = AddButton(buttons, list, style, "무적", () => SetInvincible(!Invincible));
            onePunchButton = AddButton(buttons, list, style, "원펀맨", () => SetOnePunch(!OnePunch));
            steamButton = AddButton(buttons, list, style, "무한 증기", () => { InfiniteSteam = !InfiniteSteam; RefreshLabels(); });
            swordFxButton = AddButton(buttons, list, style, "검 이펙트", () =>
            {
                NmjSwordSlash.Mode m = NmjSwordSlash.Cycle();
                RefreshLabels();
                NmjSubtitle.Caption("검 이펙트 시안: " + NmjSwordSlash.Label(m), 2f);
            });
            AddButton(buttons, list, style, "리젠 — 지금 구역 적 다시", Respawn);
            int warpStart = buttons.Count;
            AddButton(buttons, list, style, "이동 1. 시작 지점 · 허수아비", MoveToStart);
            foreach (Warp w in Warps)
            {
                Vector3 to = w.pos;
                AddButton(buttons, list, style, "이동 " + w.label, () => Teleport(to, Quaternion.identity));
            }
            AddButton(buttons, list, style, "이동 8. 향원정 안개문 앞", MoveToBossGate);
            AddButton(buttons, list, style, "이동 9. 보스방 안", MoveToBossRoom);
            AddButton(buttons, list, style, "닫기", Close);
            LayoutColumn(buttons, rowH);
            foreach (MenuButton b in buttons) b.label.richText = true;
            // 순간이동 줄은 조금 작게 → 기능과 구분된다
            for (int i = warpStart; i < buttons.Count - 1; i++) buttons[i].label.fontSize = 22f;

            Hint(panel, font, "~ / Insert / ESC  닫기", White);
            RefreshLabels();
        }
    }
}
