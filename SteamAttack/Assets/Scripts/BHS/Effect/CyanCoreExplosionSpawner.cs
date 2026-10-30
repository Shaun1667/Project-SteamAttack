using UnityEngine;

namespace CyanCoreEffects
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Effects/Cyan Core Explosion Spawner")]
    public sealed class CyanCoreExplosionSpawner : MonoBehaviour
    {
        [SerializeField, Tooltip("Assign Prefabs/CyanCoreExplosion.")]
        private GameObject effectPrefab;
        [SerializeField, Tooltip("Position and blue Z axis used for the blast. Defaults to this object.")]
        private Transform emissionPoint;
        [SerializeField, Min(0.1f)] private float effectScale = 1f;
        [SerializeField] private bool playOnStart;
        [SerializeField, Tooltip("Enable only for a repeating preview.")]
        private bool repeatForPreview;
        [SerializeField, Min(0.1f)] private float repeatInterval = 3.6f;
        private float nextPreviewTime;

        private void Start()
        {
            nextPreviewTime = Time.time + repeatInterval;
            if (playOnStart) PlayEffect();
        }

        private void Update()
        {
            if (repeatForPreview && Time.time >= nextPreviewTime)
                PlayEffect();
        }

        [ContextMenu("Play Effect (Play Mode)")]
        public void PlayEffect()
        {
            if (!Application.isPlaying) return;
            nextPreviewTime = Time.time + Mathf.Max(0.1f, repeatInterval);
            if (effectPrefab == null)
            {
                Debug.LogWarning("Assign the CyanCoreExplosion prefab before playing the effect.", this);
                return;
            }

            Transform origin = emissionPoint != null ? emissionPoint : transform;
            GameObject burst = Instantiate(effectPrefab, origin.position, origin.rotation);
            burst.transform.localScale *= Mathf.Max(0.1f, effectScale);
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(burst, gameObject.scene);
        }
    }
}
