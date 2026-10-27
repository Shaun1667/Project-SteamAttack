using UnityEngine;
namespace LaserScorchEffects
{
    public sealed class ScorchDemoPanel : MonoBehaviour
    {
        [SerializeField] private LaserScorchSweepAttack attack;
        [SerializeField] private bool autoReplay = true;
        [SerializeField, Min(.1f)] private float replayDelay = 1f;
        private float idleTime;
        public bool AutoReplay { get => autoReplay; set { autoReplay = value; idleTime = 0f; } }

        private void Update()
        {
            if (attack == null || attack.IsBusy || !autoReplay) { idleTime = 0f; return; }
            idleTime += Time.deltaTime;
            if (idleTime >= replayDelay) ReplayOnce();
        }
        public bool ReplayOnce()
        {
            if (attack == null || attack.IsBusy || !attack.isActiveAndEnabled) return false;
            if (!attack.Fire()) return false;
            idleTime = 0f;
            return true;
        }
        private void OnGUI()
        {
            if (attack == null) return;
            GUI.Box(new Rect(20,20,365,218), "LASER SCORCH / SINGLE SWEEP");
            GUI.Label(new Rect(34,51,337,24), "ONE PASS  >  DELAY  >  ONE FULL BLAST");
            GUI.Label(new Rect(34,78,337,24), "Phase: " + attack.Phase + "    Plays: " + attack.CastCount);
            GUI.Label(new Rect(34,103,337,24), "Visual effect only - no built-in damage.");
            bool nextAuto = GUI.Toggle(new Rect(34,133,330,26), autoReplay, "Auto replay (demo only)");
            if (nextAuto != autoReplay) AutoReplay = nextAuto;
            GUI.enabled = !attack.IsBusy;
            if (GUI.Button(new Rect(34,171,160,36), "Replay one sweep")) ReplayOnce();
            GUI.enabled = true;
            GUI.Label(new Rect(205,179,170,24), attack.IsBusy ? "Wait for effect to finish" : "Ready");
        }
    }
}
