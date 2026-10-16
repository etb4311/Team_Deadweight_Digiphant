using UnityEngine;

namespace DeadweightTanks
{
    // A water ball fired from a trunk. Moves by sphere-casting each step, so it can't tunnel through thin walls.
    // Bounces off walls and cover up to `bounces` times (Wii Tanks style); the shooter is only hittable after a bounce.
    public class TankWaterShot : MonoBehaviour
    {
        public TankElephant shooter;
        public float speed = 14;
        public float radius = .35f;
        public int bounces = 2;
        public Color tint = new Color(.55f, .8f, 1);
        public float lifetime = 4;
        public Material splashMaterial;

        public bool Bounced { get; private set; }
        Vector3 direction;
        float age;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public void Launch(TankElephant from, Vector3 dir)
        {
            shooter = from;
            direction = new Vector3(dir.x, 0, dir.z).normalized;
        }

        void Update() => Step(Time.deltaTime);

        // One movement step. Public so the editor validation can drive it.
        public void Step(float dt)
        {
            if (!this) return;
            age += dt;
            if (age > lifetime) { Splash(); return; }
            float remaining = speed * dt;
            for (int guard = 0; guard < 4 && remaining > 0; guard++)
            {
                if (!Cast(remaining, out RaycastHit hit)) { transform.position += direction * remaining; return; }
                transform.position += direction * hit.distance;
                remaining -= hit.distance;
                var tank = hit.collider.GetComponentInParent<TankElephant>();
                // An elephant soaks the ball even during its post-hit grace (TakeHit ignores the damage then).
                if (tank) { tank.TakeHit(this); Splash(); return; }
                if (hit.distance <= 0 || bounces <= 0) { Splash(); return; } // started inside a surface: no clean bounce
                // Reflect along the ground plane and step off the surface.
                bounces--;
                Bounced = true;
                Vector3 normal = new Vector3(hit.normal.x, 0, hit.normal.z).normalized;
                if (normal.sqrMagnitude < .01f) { Splash(); return; }
                direction = Vector3.Reflect(direction, normal).normalized;
                transform.position += normal * .02f;
            }
        }

        // Nearest solid hit along the path, skipping triggers, the shooter before a bounce, and knocked-out elephants.
        bool Cast(float distance, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(transform.position, radius, direction, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                var tank = h.collider.GetComponentInParent<TankElephant>();
                // The shooter is ignored until the ball has bounced; knocked-out elephants never block.
                if (tank && (tank.KnockedOut || (tank == shooter && !Bounced))) continue;
                // Anything else counts, including surfaces the ball already overlaps (reported at distance 0).
                if (h.distance < nearest) { nearest = h.distance; best = h; }
            }
            return nearest < float.MaxValue;
        }

        void Splash()
        {
            if (!this) return;
            SpawnSplash(transform.position, splashMaterial, tint);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        // A short burst of droplets. Runtime-only; skipped in edit-mode validation.
        static void SpawnSplash(Vector3 at, Material material, Color tint)
        {
            if (!Application.isPlaying) return;
            var go = new GameObject("Splash");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = .3f;
            main.loop = false;
            main.startLifetime = .5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2, 6);
            main.startSize = new ParticleSystem.MinMaxCurve(.15f, .35f);
            var c = Color.Lerp(tint, Color.white, .3f);
            main.startColor = new Color(c.r, c.g, c.b, .9f);
            main.gravityModifier = 1.5f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 24) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = .3f;
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);
            if (material) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            ps.Play();
        }
    }
}
