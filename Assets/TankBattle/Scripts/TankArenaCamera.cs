using UnityEngine;

namespace DeadweightTanks
{
    // Fixed overhead view that fits the whole arena, like Wii Tanks. Runs after the DigiPhant panel has
    // resized the camera viewport, so the fit uses the visible area.
    [DefaultExecutionOrder(200)]
    [ExecuteAlways] // also frames the arena in the editor's Game view
    [RequireComponent(typeof(Camera))]
    public class TankArenaCamera : MonoBehaviour
    {
        public Vector3 arenaCentre;
        [Tooltip("Half the arena size: x = width / 2, y = depth / 2.")]
        public Vector2 arenaHalfSize = new Vector2(22, 15);
        [Range(30, 90)] public float pitch = 62;
        [Tooltip("Extra room around the arena, as a fraction.")]
        [Range(0, .5f)] public float margin = .08f;
        [Tooltip("Fraction of the view height kept clear at the bottom (e.g. for player cards). Set by the match HUD.")]
        [Range(0, .4f)] public float reserveBottom;

        Camera cam;

        void LateUpdate() => Place();

        public void Place()
        {
            if (!cam) cam = GetComponent<Camera>();
            float p = pitch * Mathf.Deg2Rad;
            float tanFull = Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad), tanH = tanFull * cam.aspect;
            // Fit the arena into the view minus the reserved strip, then tilt down so it sits above that strip.
            float tanV = tanFull * (1 - reserveBottom);
            float tilt = Mathf.Atan(tanFull * reserveBottom) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(pitch + tilt, 0, 0);
            float halfX = arenaHalfSize.x * (1 + margin), halfZ = arenaHalfSize.y * (1 + margin) + 2; // + elephant height
            // The near (bottom) edge is halfZ*cos(pitch) closer than the centre, so it's the widest part of the
            // view to fit; the depth is foreshortened by sin(pitch).
            float distance = Mathf.Max(halfX / tanH, halfZ * Mathf.Sin(p) / tanV) + halfZ * Mathf.Cos(p);
            // Position from the untilted view direction, so the tilt only shifts the picture up.
            transform.SetPositionAndRotation(arenaCentre - Quaternion.Euler(pitch, 0, 0) * Vector3.forward * distance, rotation);
        }
    }
}
