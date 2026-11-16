using System.Collections.Generic;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Formation flying controller.  A flight lead uses this component;
    /// each wingman references it to maintain their offset position.
    ///
    /// Supports: finger-four, line-abreast, echelon-right, trail, and vic
    /// formations.  Wingmen smoothly transition between formation slots when
    /// the formation type changes.
    /// </summary>
    public class FormationFlying : MonoBehaviour
    {
        // ─── Formation enum ───────────────────────────────────────────────────
        public enum FormationType { FingerFour, LineAbreast, EchelonRight, Trail, Vic }

        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Formation")]
        [SerializeField] private FormationType formation       = FormationType.FingerFour;
        [SerializeField] private float         slotSpacing     = 150f;   // metres between aircraft
        [SerializeField] private float         followDamping   = 4f;
        [SerializeField] private float         maxSlotDeviation = 50f;   // before re-joining

        [Header("Lead Reference")]
        [SerializeField] private bool isLead = true;

        // ─── Slot offsets per formation ───────────────────────────────────────
        // Indexed by wingman slot (0 = lead's right wing, 1 = lead's left wing, etc.)
        private static readonly Dictionary<FormationType, Vector3[]> SlotOffsets
            = new Dictionary<FormationType, Vector3[]>
        {
            { FormationType.FingerFour,  new [] { new Vector3( 1,-0.05f,-0.8f), new Vector3(-1,-0.05f,-0.8f), new Vector3( 2,-0.1f,-1.6f) } },
            { FormationType.LineAbreast, new [] { new Vector3( 1, 0, 0),        new Vector3(-1, 0, 0),         new Vector3( 2, 0,  0) } },
            { FormationType.EchelonRight,new [] { new Vector3( 1, 0,-1),        new Vector3( 2, 0,-2),         new Vector3( 3, 0, -3) } },
            { FormationType.Trail,       new [] { new Vector3( 0, 0,-1),        new Vector3( 0, 0,-2),         new Vector3( 0, 0, -3) } },
            { FormationType.Vic,         new [] { new Vector3( 1,-0.05f,-0.8f), new Vector3(-1,-0.05f,-0.8f), new Vector3( 0,-0.1f,-1.6f) } }
        };

        // ─── Runtime ─────────────────────────────────────────────────────────
        private readonly List<WingmanAgent> _wingmen = new List<WingmanAgent>(3);

        // ─── Inner type ───────────────────────────────────────────────────────
        private class WingmanAgent
        {
            public Transform      Transform;
            public FlightPhysics  Physics;
            public int            Slot;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Registers a wingman aircraft in the given slot (0-based).</summary>
        public void RegisterWingman(Transform wingman, FlightPhysics physics, int slot)
        {
            _wingmen.Add(new WingmanAgent { Transform = wingman, Physics = physics, Slot = slot });
        }

        /// <summary>Removes a wingman (called when an aircraft is destroyed).</summary>
        public void UnregisterWingman(Transform wingman)
        {
            _wingmen.RemoveAll(w => w.Transform == wingman);
        }

        /// <summary>Changes the active formation.  Wingmen re-slot smoothly.</summary>
        public void SetFormation(FormationType type)
        {
            formation = type;
        }

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Update()
        {
            if (!isLead) return;

            foreach (var wingman in _wingmen)
            {
                if (wingman.Transform == null || wingman.Physics == null) continue;
                UpdateWingmanSlot(wingman);
            }
        }

        private void UpdateWingmanSlot(WingmanAgent wm)
        {
            Vector3[] offsets = SlotOffsets[formation];
            int       slot    = Mathf.Clamp(wm.Slot, 0, offsets.Length - 1);

            // World-space slot position (scaled by slotSpacing)
            Vector3 slotWorldPos = transform.TransformPoint(offsets[slot] * slotSpacing);

            // Steer the wingman toward their slot
            Vector3 toSlot = slotWorldPos - wm.Transform.position;
            float   dist   = toSlot.magnitude;

            if (dist < maxSlotDeviation)
            {
                // Hold slot — match lead heading and speed
                wm.Physics.PitchInput    = 0f;
                wm.Physics.RollInput     = 0f;
                wm.Physics.YawInput      = 0f;
                wm.Physics.ThrottleInput = GetComponent<FlightPhysics>()?.ThrottleInput ?? 0.7f;
            }
            else
            {
                // Navigate toward slot
                Vector3 localToSlot = wm.Transform.InverseTransformDirection(toSlot.normalized);
                wm.Physics.PitchInput    = Mathf.Clamp(-localToSlot.y * 2f, -1f,  1f);
                wm.Physics.RollInput     = Mathf.Clamp( localToSlot.x * 2f, -1f,  1f);
                wm.Physics.YawInput      = Mathf.Clamp( localToSlot.x * 0.5f,-1f, 1f);
                wm.Physics.ThrottleInput = Mathf.Lerp(0.7f, 1f, Mathf.InverseLerp(0f, 500f, dist));
            }
        }

        // ─── Debug gizmo ─────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!isLead || !SlotOffsets.ContainsKey(formation)) return;

            Gizmos.color = Color.cyan;
            foreach (var offset in SlotOffsets[formation])
            {
                Vector3 slotPos = transform.TransformPoint(offset * slotSpacing);
                Gizmos.DrawWireSphere(slotPos, 20f);
                Gizmos.DrawLine(transform.position, slotPos);
            }
        }
#endif
    }
}
