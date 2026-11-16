using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// ScriptableObject that holds all tunable parameters for a single aircraft
    /// type.  Create instances via Assets > Create > Skyrift > Aircraft Stats.
    ///
    /// Three reference configurations are documented below.
    ///
    /// ─── F/A-18 "Hornet" (balanced multi-role) ───────────────────────────────
    ///   aircraftName       = "F/A-18 Hornet"
    ///   maxSpeed           = 560          (m/s, ~1090 kts)
    ///   minSpeed           = 65
    ///   afterburnerSpeed   = 680
    ///   engineThrust       = 160000
    ///   afterburnerThrust  = 270000
    ///   liftCoefficient    = 2.8
    ///   dragCoefficient    = 0.028
    ///   mass               = 14500
    ///   pitchRate          = 65
    ///   rollRate           = 110
    ///   yawRate            = 28
    ///   maxHealth          = 1200
    ///   missileCount       = 6
    ///   gunAmmo            = 570
    ///   gunFireRate        = 20
    ///
    /// ─── Su-35 "Flanker-E" (high-agility superiority) ─────────────────────────
    ///   aircraftName       = "Su-35 Flanker"
    ///   maxSpeed           = 680
    ///   minSpeed           = 55
    ///   afterburnerSpeed   = 830
    ///   engineThrust       = 200000
    ///   afterburnerThrust  = 340000
    ///   liftCoefficient    = 3.4
    ///   dragCoefficient    = 0.024
    ///   mass               = 18500
    ///   pitchRate          = 80
    ///   rollRate           = 130
    ///   yawRate            = 35
    ///   maxHealth          = 1500
    ///   missileCount       = 8
    ///   gunAmmo            = 400
    ///   gunFireRate        = 18
    ///
    /// ─── A-10 "Thunderbolt" (heavy ground-attack) ─────────────────────────────
    ///   aircraftName       = "A-10 Thunderbolt"
    ///   maxSpeed           = 210
    ///   minSpeed           = 50
    ///   afterburnerSpeed   = 230          (no true AB; small boost)
    ///   engineThrust       = 80000
    ///   afterburnerThrust  = 90000
    ///   liftCoefficient    = 3.0
    ///   dragCoefficient    = 0.045
    ///   mass               = 22000
    ///   pitchRate          = 45
    ///   rollRate           = 70
    ///   yawRate            = 22
    ///   maxHealth          = 2500         (armoured)
    ///   missileCount       = 4
    ///   gunAmmo            = 1350
    ///   gunFireRate        = 65           (30mm GAU-8 rotary cannon)
    /// </summary>
    [CreateAssetMenu(fileName = "AircraftStats_New",
                     menuName  = "Skyrift/Aircraft Stats",
                     order     = 1)]
    public class AircraftStats_SO : ScriptableObject
    {
        // ─── Identification ───────────────────────────────────────────────────
        [Header("Identity")]
        public string aircraftName  = "Unknown Aircraft";
        public Sprite hudIcon;

        // ─── Speed / Propulsion ───────────────────────────────────────────────
        [Header("Speed (m/s)")]
        [Tooltip("Maximum level-flight speed without afterburner.")]
        public float maxSpeed           = 560f;

        [Tooltip("Minimum speed before stall detection triggers.")]
        public float minSpeed           = 65f;

        [Tooltip("Maximum speed while afterburner is active.")]
        public float afterburnerSpeed   = 680f;

        [Header("Thrust (Newtons)")]
        [Tooltip("Maximum thrust at full throttle without afterburner.")]
        public float engineThrust       = 160000f;

        [Tooltip("Additional thrust applied when afterburner is lit.")]
        public float afterburnerThrust  = 270000f;

        // ─── Aerodynamics ────────────────────────────────────────────────────
        [Header("Aerodynamics")]
        [Tooltip("Wing lift coefficient.  Higher = more lift per m²·v².")]
        public float liftCoefficient    = 2.8f;

        [Tooltip("Drag coefficient.  Higher = more parasitic drag at speed.")]
        public float dragCoefficient    = 0.028f;

        [Tooltip("Aircraft mass in kg.  Sets Rigidbody.mass at runtime.")]
        public float mass               = 14500f;

        // ─── Control rates (degrees/second) ──────────────────────────────────
        [Header("Control Authority (deg/s at max input)")]
        public float pitchRate  = 65f;
        public float rollRate   = 110f;
        public float yawRate    = 28f;

        // ─── Handling (used by AI and display only) ───────────────────────────
        [Header("Handling Reference")]
        [Tooltip("Minimum turn radius at max-G (metres).  Used by AI for pursuit curves.")]
        public float turnRadius = 800f;

        // ─── Health ───────────────────────────────────────────────────────────
        [Header("Durability")]
        public float maxHealth  = 1200f;

        // ─── Weapons loadout ─────────────────────────────────────────────────
        [Header("Weapons")]
        [Tooltip("Number of air-to-air missiles loaded.")]
        public int   missileCount = 6;

        [Tooltip("Total gun ammunition (rounds).")]
        public int   gunAmmo      = 570;

        [Tooltip("Rounds per second the gun fires.")]
        public float gunFireRate  = 20f;

        // ─── Editor validation ────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnValidate()
        {
            maxSpeed          = Mathf.Max(maxSpeed,          1f);
            minSpeed          = Mathf.Clamp(minSpeed,        10f, maxSpeed * 0.5f);
            afterburnerSpeed  = Mathf.Max(afterburnerSpeed,  maxSpeed);
            engineThrust      = Mathf.Max(engineThrust,      1000f);
            afterburnerThrust = Mathf.Max(afterburnerThrust, engineThrust);
            mass              = Mathf.Max(mass,              500f);
            maxHealth         = Mathf.Max(maxHealth,         1f);
            missileCount      = Mathf.Max(missileCount,      0);
            gunAmmo           = Mathf.Max(gunAmmo,           0);
            gunFireRate       = Mathf.Max(gunFireRate,       1f);
        }
#endif
    }
}
