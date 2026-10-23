using NGH;
using UnityEngine;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 시작 튜토리얼 — 칼로 베어 증기를 채우는 법 (시작 구간)
    ///
    /// - 새 판 시작(오프닝을 튼 판)에만: 증기 0에서 시작한다. 보스 씬에서 돌아오거나 쓰러져 다시 섰을 때는 건드리지 않는다.
    /// - 시작 지점 앞에 허수아비(NGH_Dummy, 적 태그 · 쓰러져도 다시 섬)를 세워 둔다. 자식으로 놓인 것을 그대로 쓴다.
    /// - 오프닝이 끝나면 "좌클릭 — 허수아비를 베어 증기 채우기"를 띄운다.
    ///   증기가 처음 차오르면 "증기로 강공격·사격·시간 역행", 증기가 강공격 한 번 값(기본 30)을 넘으면 끝.
    /// - 개발자 창의 무한 증기는 이 튜토리얼이 도는 동안 잠시 멈춘다(충전 시연).
    ///
    /// NMJ_Story 프리팹 안에 둔다 → 공용 Main·NMJ_Main 모두 병합만으로 들어간다.
    /// </summary>
    [DisallowMultipleComponent]
    public class NmjStartTutorial : MonoBehaviour
    {
        [Tooltip("새 판 시작 때 증기를 0으로 비운다")]
        [SerializeField] private bool startWithEmptySteam = true;
        [Tooltip("이만큼 차면 튜토리얼을 끝낸다 (강공격 한 번 값)")]
        [SerializeField, Min(1f)] private float doneAtPressure = 30f;
        [Tooltip("새 판이 아니면(보스 씬 복귀 등) 허수아비도 숨긴다")]
        [SerializeField] private bool hideDummiesWhenNotFresh = false;

        /// <summary>튜토리얼이 도는 중 (개발자 창 무한 증기가 이때 멈춘다)</summary>
        public static bool IsRunning { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsRunning = false;

        private enum Stage { Waiting, WaitOpening, Prompted, Charging, Done }
        private Stage stage = Stage.Waiting;
        private SteamTank[] tanks;   // 플레이어에 탱크가 둘(NGH_Player 몸·YPH 증기통) 붙어 있어 모두 다룬다
        private float lastPressure;
        private float timer;

        private void Start()
        {
            // NmjOpening.Start가 먼저 돌았는지 모르니 한 프레임 뒤에 판단
            timer = 0f;
        }

        private void OnDisable() => IsRunning = false;

        private void Update()
        {
            switch (stage)
            {
                case Stage.Waiting:
                    timer += Time.unscaledDeltaTime;
                    if (timer < 0.1f) return;
                    if (!NmjOpening.FreshStart)
                    {
                        if (hideDummiesWhenNotFresh) gameObject.SetActive(false);
                        stage = Stage.Done;
                        return;
                    }
                    tanks = FindTanks();
                    if (tanks.Length == 0) { stage = Stage.Done; return; }
                    if (startWithEmptySteam) SetAll(0f);
                    lastPressure = Pressure();
                    IsRunning = true;
                    stage = Stage.WaitOpening;
                    return;

                case Stage.WaitOpening:
                    if (startWithEmptySteam && Pressure() > 0f && NmjOpening.IsPlaying) SetAll(0f);
                    if (NmjOpening.IsPlaying || NmjDialogue.IsPlaying) return;
                    NmjSubtitle.Key("좌클릭", "허수아비를 베어 증기 채우기", 4f);
                    lastPressure = Pressure();
                    stage = Stage.Prompted;
                    return;

                case Stage.Prompted:
                    if (Pressure() > lastPressure + 0.01f)
                    {
                        NmjSubtitle.Caption("칼이 닿을 때마다 증기가 찬다 — 증기로 강공격·사격·시간 역행", 4f);
                        stage = Stage.Charging;
                    }
                    lastPressure = Pressure();
                    return;

                case Stage.Charging:
                    if (Pressure() >= doneAtPressure) Finish();
                    return;
            }
        }

        private void Finish()
        {
            stage = Stage.Done;
            IsRunning = false;
        }

        /// <summary>가장 많이 찬 탱크의 증기 (칼 적중은 사격·강공격이 쓰는 증기통 쪽에 찬다)</summary>
        private float Pressure()
        {
            float p = 0f;
            foreach (SteamTank t in tanks) if (t != null) p = Mathf.Max(p, t.CurrentPressure);
            return p;
        }

        private void SetAll(float pressure)
        {
            foreach (SteamTank t in tanks) if (t != null) t.RestorePressure(pressure);
        }

        /// <summary>플레이어 몸 아래의 탱크 모두 (없으면 씬의 탱크 모두)</summary>
        public static SteamTank[] FindTanks()
        {
            PlayerController p = FindAnyObjectByType<PlayerController>();
            SteamTank[] found = p != null ? p.transform.root.GetComponentsInChildren<SteamTank>(true) : new SteamTank[0];
            return found.Length > 0 ? found : FindObjectsByType<SteamTank>();
        }
    }
}
