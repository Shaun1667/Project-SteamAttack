using UnityEngine;
namespace CyanSteamEffects
{
    public sealed class SteamOrbDemoLauncher : MonoBehaviour
    {
        [SerializeField] private SteamOrbProjectile projectilePrefab;
        [SerializeField] private Transform origin;
        [SerializeField] private Transform destination;
        [SerializeField] private float interval = 3.2f;
        private float countdown = .6f;
        private void Update() { countdown -= Time.deltaTime; if (countdown <= 0f) { Fire(); countdown = Mathf.Max(.5f, interval); } }
        [ContextMenu("Fire steam orb (Play Mode)")]
        public void Fire()
        {
            if (!Application.isPlaying || projectilePrefab == null || origin == null || destination == null) return;
            var orb = Instantiate(projectilePrefab, origin.position, Quaternion.identity);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(orb.gameObject, gameObject.scene);
            orb.Launch(destination.position, 1.6f, 2.2f);
        }
    }
}
