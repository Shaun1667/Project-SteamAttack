using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NGH;
using YPH;

namespace CHG
{
    /// <summary>
    /// 플레이어 HUD (기획: 인게임 UI)
    /// - 좌하단: 플레이어 체력바 (PlayerController.Hp / maxHp). 깎인 부분은 밝은 잔상이 잠시 남았다가 줄어든다. 수치는 표시하지 않음.
    /// - 체력바 아래: 증기 막대 (SteamTank.NormalizedPressure). 부드럽게 따라감.
    /// - 우하단: 장착 무기 칸 (최대 2칸, NGH_WeaponHolder의 앞 2개). 지금 든 무기는 금테로 강조, Tab으로 바꾸면 살짝 튀어오름.
    /// - 무기 왼쪽: 회복약 칸 ('회복약' 글자만, 아직 회복약 기능이 없음)
    /// - 개발용 표시(0 키)를 켜면: 체력바 위에 'HP 현재 / 최대  증기 현재 / 최대', 'Tab' 무기 교체 안내
    ///   (레벨업으로 최대 체력이 늘어 체력바가 길어져도 겹치지 않도록 체력바 위에 표시)
    /// 플레이어 코드는 수정하지 않고 공개 값만 읽는다. 플레이어가 없으면 HUD를 숨긴다.
    /// 프리팹(PlayerHUD)은 메뉴 CHG/Build Player HUD 로 만들어지며, 씬에 하나 놓으면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHUD : MonoBehaviour
    {
        [Serializable]
        public class WeaponSlot
        {
            public GameObject root;
            public Image border;
            public Image inner;
            public TMP_Text label;
        }

        [Header("연결 (프리팹에 미리 연결됨)")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform hpFill;
        [SerializeField] private RectTransform hpTrail;
        [SerializeField] private WeaponSlot[] weaponSlots = new WeaponSlot[0];
        [Tooltip("증기 막대 채움 (비우면 증기 막대 없음)")]
        [SerializeField] private RectTransform steamFill;
        [Tooltip("비우면 씬에서 찾음")]
        [SerializeField] private SteamTank steamTank;
        [Tooltip("개발용 표시(0 키)일 때만 보이는 체력·증기 수치 (체력바 위)")]
        [SerializeField] private TMP_Text hpValueText;
        [Tooltip("개발용 표시(0 키)일 때만 보이는 'Tab' 안내")]
        [SerializeField] private GameObject tabHint;

        [Header("무기 칸")]
        [Tooltip("표시할 무기 칸 수 (기획: 2개)")]
        [SerializeField, Range(1, 3)] private int maxWeaponSlots = 2;

        [Header("체력바")]
        [Tooltip("맞은 뒤 잔상이 줄어들기 시작할 때까지 시간(초)")]
        [SerializeField, Min(0f)] private float trailDelay = 0.4f;
        [Tooltip("잔상이 줄어드는 속도 (초당 전체 대비 비율)")]
        [SerializeField, Min(0.01f)] private float trailSpeed = 0.6f;

        [Header("증기 막대")]
        [Tooltip("증기 막대가 실제 값을 따라가는 빠르기")]
        [SerializeField, Min(0.1f)] private float steamFollowSharpness = 14f;

        [Header("무기 칸 색")]
        [SerializeField] private Color selectedBorder = new Color(0.9f, 0.72f, 0.35f, 1f);
        [SerializeField] private Color normalBorder = new Color(0.55f, 0.52f, 0.48f, 0.55f);
        [SerializeField] private Color selectedInner = new Color(0.12f, 0.1f, 0.08f, 0.85f);
        [SerializeField] private Color normalInner = new Color(0.05f, 0.05f, 0.05f, 0.55f);
        [SerializeField] private Color selectedLabel = new Color(1f, 0.96f, 0.88f, 1f);
        [SerializeField] private Color normalLabel = new Color(0.7f, 0.68f, 0.64f, 0.7f);

        private PlayerController player;
        private float trailRatio = 1f;
        private float trailHold;
        private int lastIndex = -1;
        private float popTimer;
        private int shownHp = -1;
        private int shownMaxHp = -1;
        private int shownSteam = -1;
        private int shownMaxSteam = -1;
        private float steamRatio = -1f;

        private void Update()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
                if (player == null)
                {
                    if (group != null) group.alpha = 0f;
                    return;
                }
                trailRatio = HpRatio();
                lastIndex = -1;
            }
            if (group != null) group.alpha = 1f;

            UpdateHp();
            UpdateSteam();
            UpdateWeapons();
            UpdateDebugInfo();
        }

        // 개발용 표시(0 키)일 때만: 체력·증기 수치(체력바 위), Tab 안내
        private void UpdateDebugInfo()
        {
            bool debug = DebugView.Enabled;
            if (tabHint != null && tabHint.activeSelf != debug) tabHint.SetActive(debug);
            if (hpValueText == null) return;
            if (hpValueText.gameObject.activeSelf != debug)
            {
                hpValueText.gameObject.SetActive(debug);
                shownHp = -1;
            }
            if (!debug) return;

            int steam = steamTank != null ? Mathf.RoundToInt(steamTank.CurrentPressure) : -1;
            int maxSteam = steamTank != null ? Mathf.RoundToInt(steamTank.MaxPressure) : -1;
            if (player.Hp != shownHp || player.maxHp != shownMaxHp || steam != shownSteam || maxSteam != shownMaxSteam)
            {
                shownHp = player.Hp;
                shownMaxHp = player.maxHp;
                shownSteam = steam;
                shownMaxSteam = maxSteam;
                string text = "HP " + shownHp + " / " + shownMaxHp;
                if (steamTank != null) text += "    증기 " + shownSteam + " / " + shownMaxSteam;
                hpValueText.text = text;
            }
        }

        // ───────── 증기 막대 ─────────
        private void UpdateSteam()
        {
            if (steamFill == null) return;
            if (steamTank == null)
            {
                steamTank = FindAnyObjectByType<SteamTank>();
                if (steamTank == null)
                {
                    SetRatio(steamFill, 0f);
                    return;
                }
                steamRatio = -1f;
            }
            float target = steamTank.MaxPressure > 0f ? steamTank.NormalizedPressure : 0f;
            steamRatio = steamRatio < 0f ? target
                : Mathf.Lerp(steamRatio, target, 1f - Mathf.Exp(-steamFollowSharpness * Time.unscaledDeltaTime));
            SetRatio(steamFill, steamRatio);
        }

        private float HpRatio()
        {
            return player != null && player.maxHp > 0 ? Mathf.Clamp01((float)player.Hp / player.maxHp) : 0f;
        }

        // ───────── 체력바 ─────────
        private void UpdateHp()
        {
            float ratio = HpRatio();
            if (ratio >= trailRatio)
            {
                trailRatio = ratio;   // 회복은 바로
                trailHold = 0f;
            }
            else if (trailHold < trailDelay)
            {
                trailHold += Time.unscaledDeltaTime;
            }
            else
            {
                trailRatio = Mathf.MoveTowards(trailRatio, ratio, trailSpeed * Time.unscaledDeltaTime);
                if (trailRatio <= ratio) trailHold = 0f;
            }
            SetRatio(hpFill, ratio);
            SetRatio(hpTrail, trailRatio);
        }

        private static void SetRatio(RectTransform rect, float ratio)
        {
            if (rect == null) return;
            rect.anchorMax = new Vector2(ratio, rect.anchorMax.y);
            bool visible = ratio > 0.001f;
            if (rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
        }

        // ───────── 무기 칸 ─────────
        private void UpdateWeapons()
        {
            WeaponHolder holder = player.weapons;
            int current = holder != null ? holder.currentIndex : -1;

            if (current != lastIndex)
            {
                if (lastIndex != -1) popTimer = 0.18f;   // 교체 연출 (처음 표시할 때는 생략)
                lastIndex = current;
            }
            if (popTimer > 0f) popTimer -= Time.unscaledDeltaTime;

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                WeaponSlot slot = weaponSlots[i];
                if (slot == null || slot.root == null) continue;

                Weapon weapon = holder != null && i < maxWeaponSlots && i < holder.slots.Count ? holder.slots[i] : null;
                bool show = weapon != null;
                if (slot.root.activeSelf != show) slot.root.SetActive(show);
                if (!show) continue;

                bool selected = i == current;
                if (slot.label != null)
                {
                    if (slot.label.text != weapon.weaponName) slot.label.text = weapon.weaponName;
                    slot.label.color = selected ? selectedLabel : normalLabel;
                }
                if (slot.border != null) slot.border.color = selected ? selectedBorder : normalBorder;
                if (slot.inner != null) slot.inner.color = selected ? selectedInner : normalInner;

                float scale = 1f;
                if (selected)
                {
                    scale = 1.08f;
                    if (popTimer > 0f) scale += 0.12f * (popTimer / 0.18f);
                }
                slot.root.transform.localScale = Vector3.one * scale;
            }
        }
    }
}
