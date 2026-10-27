using UnityEngine;
namespace CyanSteamEffects
{
    public sealed class SteamImpactLifetime : MonoBehaviour
    {
        [SerializeField, Min(.2f)] private float lifetime = 2f;
        private float age;
        private void Update() { age += Time.deltaTime; if (age >= lifetime) Destroy(gameObject); }
    }
}
