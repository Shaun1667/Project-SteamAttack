using UnityEngine;

namespace MadLab
{
    [DisallowMultipleComponent]
    public sealed class LabCoreExplosionBurst : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lifetime = 2.2f;
        [SerializeField] private Light coreFlash;
        private float age;
        private float initialIntensity;
        private AudioSource burstAudio;
        private bool audioPaused;
        public float Age => age;

        private void Awake()
        {
            initialIntensity = coreFlash != null ? coreFlash.intensity : 0f;
            burstAudio = GetComponent<AudioSource>();
        }
        private void Start()
        {
            foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
        }
        private void Update()
        {
            if (burstAudio != null)
            {
                if (Time.timeScale <= 0f && burstAudio.isPlaying)
                {
                    burstAudio.Pause();
                    audioPaused = true;
                }
                else if (Time.timeScale > 0f && audioPaused)
                {
                    burstAudio.UnPause();
                    audioPaused = false;
                }
            }

            // Match the game's pause clock, including the short light pulse and cleanup.
            age += Time.deltaTime;
            if (coreFlash != null)
            {
                float fade = Mathf.Clamp01(1f - age / 0.22f);
                coreFlash.intensity = initialIntensity * fade * fade;
                coreFlash.enabled = fade > 0f;
            }
            if (age >= lifetime) Destroy(gameObject);
        }
    }
}
