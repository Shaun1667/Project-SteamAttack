using UnityEngine;
namespace LaserScorchEffects
{
    /// <summary>Reusable smoke/flash/dust emitters. Returns to an inactive state instead of destroying itself.</summary>
    public sealed class ScorchBurstLifetime : MonoBehaviour
    {
        [SerializeField] private ParticleSystem smoke, flash, dust;
        [SerializeField, Min(.1f)] private float lifetime = 2.2f;
        private bool initialized, pooled;
        private float age;
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Configure(smoke); Configure(flash); Configure(dust);
        }
        private static void Configure(ParticleSystem system)
        {
            if (system == null) return;
            var main = system.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            var emission = system.emission;
            emission.enabled = false;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void PreparePool()
        {
            pooled = true;
            Initialize();
            StopAndHide();
        }
        private void OnEnable()
        {
            Initialize();
            if (!pooled) { Begin(); for (int i = 0; i < 3; i++) EmitAt(transform.position, transform.up, 1 + i * 47); }
        }
        public void Begin()
        {
            Initialize();
            age = 0f;
            gameObject.SetActive(true);
            Restart(smoke); Restart(flash); Restart(dust);
        }
        private static void Restart(ParticleSystem system)
        {
            if (system == null) return;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(false);
        }
        private static float Random01(int seed)
        {
            uint value = (uint)(seed * 747796405 + 2891336453u);
            value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
            value = (value >> 22) ^ value;
            return (value & 65535) / 65535f;
        }
        public void EmitAt(Vector3 point, Vector3 normal, int seed)
        {
            Vector3 side = Vector3.Cross(normal, Vector3.forward).normalized;
            if (side.sqrMagnitude < .1f) side = Vector3.right;
            Vector3 forward = Vector3.Cross(side, normal);
            Emit(smoke, 3, point, normal, side, forward, seed, .32f, 1.25f, 2.8f);
            Emit(flash, (seed & 1) == 1 ? 1 : 0, point, normal, side, forward, seed + 151, .12f, .08f, .3f);
            Emit(dust, 2, point, normal, side, forward, seed + 311, .24f, 1.3f, 3.8f);
        }
        private static void Emit(ParticleSystem system, int count, Vector3 point, Vector3 normal,
            Vector3 side, Vector3 forward, int seed, float spread, float lowSpeed, float highSpeed)
        {
            if (system == null) return;
            for (int i = 0; i < count; i++)
            {
                int n = seed + i * 53;
                Vector3 lateral = side * (Random01(n) * 2f - 1f) + forward * (Random01(n + 17) * 2f - 1f);
                var emit = new ParticleSystem.EmitParams();
                emit.applyShapeToPosition = false;
                emit.position = point + normal * (.04f + Random01(n + 19) * .13f) + lateral * spread;
                emit.velocity = normal * Mathf.Lerp(lowSpeed, highSpeed, Random01(n + 31)) + lateral * .65f;
                system.Emit(emit, 1);
            }
        }
        private void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime) StopAndHide();
        }
        private void Clear()
        {
            if (smoke != null) smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (flash != null) flash.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (dust != null) dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void StopAndHide() { Clear(); gameObject.SetActive(false); }
        private void OnDisable() => Clear();
    }
}
