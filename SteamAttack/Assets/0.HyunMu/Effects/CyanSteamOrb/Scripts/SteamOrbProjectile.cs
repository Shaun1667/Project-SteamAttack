using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CyanSteamEffects
{
    [DisallowMultipleComponent]
    public sealed class SteamOrbProjectile : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] bodyParticles;
        [SerializeField] private ParticleSystem trail;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField, Min(.01f)] private float collisionRadius = .23f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Min(.1f)] private float trailLifetime = 1.4f;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private Transform ignoreRoot;
        private Vector3 start, target;
        private float duration, height, elapsed, cleanup;
        private bool flying, ending;
        public event Action<SteamOrbProjectile, Vector3, Collider> Impacted;
        public bool IsFlying => flying;
        public bool HasImpacted { get; private set; }
        public Vector3 PlannedTarget => target;
        public float FlightProgress => duration > 0f ? Mathf.Clamp01(elapsed / duration) : 0f;
        public Vector3 StartPosition => start;

        public static Vector3 InitialVelocity(Vector3 origin, Vector3 destination, float seconds, float arcHeight)
            => (destination - origin + Vector3.up * (4f * arcHeight)) / Mathf.Max(.1f, seconds);
        public Vector3 PositionAt(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return Vector3.Lerp(start, target, t) + Vector3.up * (4f * height * t * (1f - t));
        }
        public void Launch(Vector3 destination, float seconds = 1.4f, float arcHeight = 2.4f, Transform owner = null)
        {
            if (ending || flying) return;
            if (!Finite(destination.x) || !Finite(destination.y) || !Finite(destination.z) || !Finite(seconds) || !Finite(arcHeight))
                throw new ArgumentException("Steam orb launch values must be finite.");
            start = transform.position;
            target = destination;
            duration = Mathf.Clamp(seconds, .2f, 6f);
            height = Mathf.Clamp(arcHeight, .1f, 15f);
            ignoreRoot = owner;
            elapsed = 0f;
            flying = true;
            HasImpacted = false;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void Update()
        {
            if (ending)
            {
                cleanup -= Time.deltaTime;
                if (cleanup <= 0f) Destroy(gameObject);
                return;
            }
            if (!flying || Time.deltaTime <= 0f) return;
            // Substeps preserve the arc and sweep collision even on slower frames.
            if (duration - elapsed <= .000001f) { transform.position = target; Finish(target, null, true); return; }
            float remaining = Mathf.Min(Time.deltaTime, duration - elapsed);
            while (remaining > .000001f && flying)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                elapsed = Mathf.Min(duration, elapsed + step);
                Vector3 next = PositionAt(elapsed / duration);
                Vector3 delta = next - transform.position;
                float distance = delta.magnitude;
                float nearest = float.PositiveInfinity;
                Collider collider = null;
                Vector3 contact = next;
                if (distance > .000001f)
                {
                    int count = Physics.SphereCastNonAlloc(transform.position, collisionRadius, delta / distance, hits, distance, collisionMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        var c = hits[i].collider;
                        if (c == null || c.transform.IsChildOf(transform) || (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot))) continue;
                        if (hits[i].distance >= nearest) continue;
                        nearest = hits[i].distance;
                        collider = c;
                        contact = hits[i].point;
                    }
                }
                if (collider != null)
                {
                    transform.position += delta.normalized * nearest;
                    Finish(contact, collider, true);
                    return;
                }
                transform.position = next;
                if (elapsed >= duration) { Finish(next, null, true); return; }
                remaining -= step;
            }
        }
        public void Cancel()
        {
            if (!ending) Finish(transform.position, null, false);
        }
        private void Finish(Vector3 position, Collider collider, bool impact)
        {
            if (ending) return;
            flying = false;
            ending = true;
            cleanup = trailLifetime;
            HasImpacted = impact;
            if (bodyParticles != null)
                foreach (var particles in bodyParticles)
                    if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (trail != null) trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (impact && impactPrefab != null)
            {
                var burst = Instantiate(impactPrefab, position + Vector3.up * .08f, Quaternion.identity);
                SceneManager.MoveGameObjectToScene(burst, gameObject.scene);
            }
            if (impact) Impacted?.Invoke(this, position, collider);
            Impacted = null;
        }
    }
}
