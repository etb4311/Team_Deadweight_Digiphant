using System.Collections.Generic;
using UnityEngine;

namespace DeadweightTanks
{
    // Where elephants can drink: a circle round this object (the water plus a wading margin).
    public class TankPond : MonoBehaviour
    {
        [Tooltip("Drinking works within this distance of the pond centre (metres, ground plane).")]
        public float drinkRadius = 5.5f;

        static readonly List<TankPond> All = new List<TankPond>();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0;
            return d.magnitude <= drinkRadius;
        }

        // Also finds ponds in edit mode (validation), where OnEnable doesn't run for this script.
        public static bool AnyContains(Vector3 point)
        {
            All.RemoveAll(p => !p);
            if (All.Count == 0) All.AddRange(FindObjectsByType<TankPond>());
            foreach (var pond in All) if (pond && pond.Contains(point)) return true;
            return false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.2f, .6f, 1, .6f);
            Gizmos.DrawWireSphere(transform.position, drinkRadius);
        }
    }
}
