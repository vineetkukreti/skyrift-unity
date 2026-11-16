using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Third-person chase camera for SKYRIFT.
    /// Place on the Camera object (or a Cinemachine Virtual Camera's Follow
    /// target) and assign the aircraft transform as <see cref="target"/>.
    ///
    /// Provides: configurable offset, look-ahead based on velocity,
    /// smooth damping, cockpit (first-person) mode toggle, and
    /// field-of-view scaling with speed for a sense of velocity.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private FlightPhysics flightPhysics;

        [Header("Third Person Offset")]
        [SerializeField] private Vector3 offset         = new Vector3(0f, 3f, -14f);
        [SerializeField] private float   positionDamping = 6f;
        [SerializeField] private float   rotationDamping = 8f;

        [Header("Look-Ahead")]
        [Tooltip("How far ahead the camera looks in the direction of travel.")]
        [SerializeField] private float lookAheadDistance = 8f;
        [SerializeField] private float lookAheadDamping  = 4f;

        [Header("FOV")]
        [SerializeField] private float   baseFOV       = 65f;
        [SerializeField] private float   maxFOVBoost   = 20f;
        [Tooltip("Fraction of MaxSpeed at which FOV reaches its peak.")]
        [SerializeField] private float   fovSpeedRamp  = 0.85f;

        [Header("Cockpit Mode")]
        [SerializeField] private Vector3 cockpitOffset    = new Vector3(0f, 1.2f, 2.5f);
        [SerializeField] private float   cockpitFOV       = 80f;
        [SerializeField] private bool    startInCockpit   = false;

        [Header("Shake")]
        [SerializeField] private float   stallShakeAmount = 0.12f;
        [SerializeField] private float   stallShakeSpeed  = 22f;

        // ─── Private ──────────────────────────────────────────────────────────
        private Camera _cam;
        private bool   _cockpitMode;
        private Vector3 _currentLookAhead;
        private Vector3 _velocity;      // used by SmoothDamp

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _cam        = GetComponent<Camera>();
            _cockpitMode = startInCockpit;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            if (_cockpitMode)
                UpdateCockpitCamera();
            else
                UpdateChaseCamera();

            UpdateFOV();
        }

        // ─── Camera modes ────────────────────────────────────────────────────
        private void UpdateChaseCamera()
        {
            // World-space desired position: offset is in aircraft local space
            Vector3 desiredPos = target.TransformPoint(offset);

            // Stall shake
            if (flightPhysics != null && flightPhysics.IsStalled)
            {
                float shake = Mathf.Sin(Time.time * stallShakeSpeed) * stallShakeAmount;
                desiredPos += target.up * shake;
            }

            // Smooth position
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPos,
                ref _velocity,
                1f / positionDamping
            );

            // Look-ahead: camera looks slightly toward velocity direction
            if (flightPhysics != null)
            {
                Vector3 velocityDir = flightPhysics.SpeedMS > 1f
                    ? flightPhysics.LocalVelocity.normalized
                    : Vector3.forward;

                Vector3 targetLookAhead = target.TransformDirection(velocityDir) * lookAheadDistance;
                _currentLookAhead = Vector3.Lerp(
                    _currentLookAhead,
                    targetLookAhead,
                    Time.deltaTime * lookAheadDamping
                );
            }

            // Smooth rotation toward target
            Quaternion desiredRot = Quaternion.LookRotation(
                (target.position + _currentLookAhead) - transform.position,
                target.up
            );
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRot,
                Time.deltaTime * rotationDamping
            );
        }

        private void UpdateCockpitCamera()
        {
            transform.position = target.TransformPoint(cockpitOffset);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                target.rotation,
                Time.deltaTime * rotationDamping * 1.5f
            );
        }

        private void UpdateFOV()
        {
            if (_cam == null || flightPhysics == null) return;

            float normSpeed = Mathf.Clamp01(flightPhysics.SpeedMS / (flightPhysics.SpeedMS + 1f));

            // Use stats max speed for proper normalisation when available
            var stats = target?.GetComponent<AircraftStats>();
            if (stats != null)
                normSpeed = Mathf.Clamp01(flightPhysics.SpeedMS / stats.MaxSpeed);

            float targetFOV = _cockpitMode
                ? cockpitFOV
                : baseFOV + maxFOVBoost * Mathf.SmoothStep(0f, 1f, normSpeed / fovSpeedRamp);

            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFOV, Time.deltaTime * 3f);
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Toggles between chase-cam and cockpit view.</summary>
        public void ToggleCockpitMode()
        {
            _cockpitMode = !_cockpitMode;
        }

        /// <summary>Forces a specific view mode.</summary>
        public void SetCockpitMode(bool cockpit)
        {
            _cockpitMode = cockpit;
        }

        /// <summary>Reassigns the camera follow target at runtime.</summary>
        public void SetTarget(Transform newTarget, FlightPhysics newPhysics)
        {
            target        = newTarget;
            flightPhysics = newPhysics;
        }
    }
}
