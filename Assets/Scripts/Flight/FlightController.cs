using UnityEngine;
using UnityEngine.InputSystem;

namespace Skyrift
{
    /// <summary>
    /// Reads unified input (keyboard/mouse, gamepad, touch) and writes
    /// throttle/pitch/roll/yaw into <see cref="FlightPhysics"/>.
    ///
    /// Uses Unity's new Input System.  Assign the generated
    /// <c>SkyriftInputActions</c> asset in the inspector or ensure the
    /// Player Input component references the same asset.
    /// </summary>
    [RequireComponent(typeof(FlightPhysics))]
    public class FlightController : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Input Settings")]
        [SerializeField, Range(0f, 0.3f)]
        private float deadzone            = 0.08f;

        [SerializeField, Range(0.1f, 5f)]
        private float pitchSensitivity    = 1.5f;

        [SerializeField, Range(0.1f, 5f)]
        private float rollSensitivity     = 2f;

        [SerializeField, Range(0.1f, 5f)]
        private float yawSensitivity      = 1f;

        [SerializeField]
        private AnimationCurve inputCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Mouse Look")]
        [Tooltip("When true, mouse position steers pitch/roll (arcade-style).")]
        [SerializeField] private bool     mouseSteer        = true;
        [SerializeField] private float    mouseSteerStrength = 2f;

        [Header("Throttle")]
        [SerializeField, Range(0.1f, 5f)]
        private float throttleRate        = 1.5f;   // units/second

        [Header("Mobile")]
        [Tooltip("Reference to the virtual joystick UI component for mobile.")]
        [SerializeField] private VirtualJoystick mobileJoystick;

        // ─── Private ──────────────────────────────────────────────────────────
        private FlightPhysics _physics;
        private float         _throttle;   // 0..1, accumulated

        // Raw input values this frame
        private Vector2 _stickInput;     // pitch/roll
        private float   _yawInput;
        private bool    _afterburner;
        private bool    _airbrake;
        private bool    _fireGun;
        private bool    _fireMissile;
        private bool    _cycleWeapon;

        // Cached references to Input System actions
        // (Populated from the generated class; replace with your asset's name.)
        private SkyriftInputActions _actions;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _physics = GetComponent<FlightPhysics>();
            _actions = new SkyriftInputActions();
        }

        private void OnEnable()
        {
            _actions.Flight.Enable();

            _actions.Flight.Throttle.performed += OnThrottle;
            _actions.Flight.Throttle.canceled  += OnThrottleCancel;
            _actions.Flight.Afterburner.performed += ctx => _afterburner = true;
            _actions.Flight.Afterburner.canceled  += ctx => _afterburner = false;
            _actions.Flight.Airbrake.performed    += ctx => _airbrake    = true;
            _actions.Flight.Airbrake.canceled     += ctx => _airbrake    = false;
            _actions.Flight.FireGun.performed     += ctx => _fireGun     = true;
            _actions.Flight.FireGun.canceled      += ctx => _fireGun     = false;
            _actions.Flight.FireMissile.performed += ctx => _fireMissile = true;
            _actions.Flight.FireMissile.canceled  += ctx => _fireMissile = false;
        }

        private void OnDisable()
        {
            _actions.Flight.Disable();
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPaused) return;

            GatherStickInput();
            UpdateThrottle();
            WriteToPhysics();
        }

        // ─── Input gathering ─────────────────────────────────────────────────
        private void GatherStickInput()
        {
            // Priority: gamepad → mouse → mobile touch
            if (Gamepad.current != null)
            {
                GatherGamepadInput();
            }
            else if (mouseSteer && Mouse.current != null)
            {
                GatherMouseInput();
            }
            else if (mobileJoystick != null)
            {
                GatherMobileInput();
            }
            else
            {
                GatherKeyboardInput();
            }

            // Yaw from keyboard regardless of pitch/roll source
            float yawRaw = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) yawRaw -= 1f;
                if (Keyboard.current.eKey.isPressed) yawRaw += 1f;
            }
            if (Gamepad.current != null)
                yawRaw = Gamepad.current.rightStick.x.ReadValue();

            _yawInput = ApplyDeadzoneCurve(yawRaw) * yawSensitivity;
        }

        private void GatherGamepadInput()
        {
            var gp = Gamepad.current;
            float rawPitch = -gp.leftStick.y.ReadValue();
            float rawRoll  =  gp.leftStick.x.ReadValue();

            _stickInput = new Vector2(
                ApplyDeadzoneCurve(rawRoll)  * rollSensitivity,
                ApplyDeadzoneCurve(rawPitch) * pitchSensitivity
            );
        }

        private void GatherMouseInput()
        {
            // Map mouse position relative to screen centre to -1..1 range
            Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 delta = (mousePos - screen) / screen;   // -1..1

            delta = Vector2.ClampMagnitude(delta, 1f);
            _stickInput = new Vector2(
                delta.x * rollSensitivity  * mouseSteerStrength,
                delta.y * pitchSensitivity * mouseSteerStrength
            );
        }

        private void GatherMobileInput()
        {
            Vector2 axis = mobileJoystick != null ? mobileJoystick.InputVector : Vector2.zero;
            _stickInput = new Vector2(
                ApplyDeadzoneCurve(axis.x) * rollSensitivity,
                ApplyDeadzoneCurve(axis.y) * pitchSensitivity
            );
        }

        private void GatherKeyboardInput()
        {
            float rawPitch = 0f, rawRoll = 0f;
            if (Keyboard.current == null) return;

            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                rawPitch -= 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                rawPitch += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                rawRoll -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                rawRoll += 1f;

            _stickInput = new Vector2(rawRoll * rollSensitivity, rawPitch * pitchSensitivity);
        }

        private void UpdateThrottle()
        {
            float delta = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftShiftKey.isPressed)  delta += throttleRate * Time.deltaTime;
                if (Keyboard.current.leftCtrlKey.isPressed)   delta -= throttleRate * Time.deltaTime;
            }

            // Gamepad right trigger = throttle, left trigger = brake
            if (Gamepad.current != null)
            {
                float rt = Gamepad.current.rightTrigger.ReadValue();
                float lt = Gamepad.current.leftTrigger.ReadValue();
                delta += (rt - lt) * throttleRate * Time.deltaTime;
            }

            _throttle = Mathf.Clamp01(_throttle + delta);
        }

        private void WriteToPhysics()
        {
            _physics.PitchInput        = Mathf.Clamp(_stickInput.y, -1f, 1f);
            _physics.RollInput         = Mathf.Clamp(_stickInput.x, -1f, 1f);
            _physics.YawInput          = Mathf.Clamp(_yawInput,     -1f, 1f);
            _physics.ThrottleInput     = _throttle;
            _physics.AfterburnerInput  = _afterburner ? 1f : 0f;
            _physics.AirbrakeInput     = _airbrake    ? 1f : 0f;
        }

        // ─── Input System callbacks ───────────────────────────────────────────
        private void OnThrottle(InputAction.CallbackContext ctx)
        {
            float v = ctx.ReadValue<float>();
            _throttle = Mathf.Clamp01(_throttle + v * throttleRate * Time.deltaTime);
        }

        private void OnThrottleCancel(InputAction.CallbackContext ctx) { /* nothing */ }

        // ─── Helpers ─────────────────────────────────────────────────────────
        private float ApplyDeadzoneCurve(float raw)
        {
            float abs = Mathf.Abs(raw);
            if (abs < deadzone) return 0f;
            float remapped = (abs - deadzone) / (1f - deadzone);
            return Mathf.Sign(raw) * inputCurve.Evaluate(remapped);
        }

        // ─── Public accessors (for HUD / other systems) ───────────────────────
        /// <summary>Current throttle position 0..1.</summary>
        public float Throttle => _throttle;

        /// <summary>Whether afterburner is currently active.</summary>
        public bool AfterburnerActive => _afterburner;

        /// <summary>Whether airbrakes are deployed.</summary>
        public bool AirbrakeDeployed => _airbrake;

        /// <summary>Whether fire-gun action is held this frame.</summary>
        public bool FiringGun => _fireGun;

        /// <summary>Whether fire-missile action was pressed this frame.</summary>
        public bool FiringMissile => _fireMissile;
    }

    // ─── Stub for mobile virtual joystick ────────────────────────────────────
    /// <summary>
    /// Minimal interface for a screen-space virtual joystick UI element.
    /// Replace with your preferred mobile joystick implementation
    /// (e.g. Lean Touch, Unity UI Toolkit touch zone).
    /// </summary>
    public class VirtualJoystick : MonoBehaviour
    {
        /// <summary>Normalised axis vector in the range -1..1 on each axis.</summary>
        public Vector2 InputVector { get; set; } = Vector2.zero;
    }

    // ─── Generated Input Actions stub ────────────────────────────────────────
    // IMPORTANT: Replace this stub with the class generated by Unity's
    // Input System package from your SkyriftInputActions.inputactions asset.
    // The stub is here only so the script compiles before the asset exists.
    public class SkyriftInputActions
    {
        public FlightActionMap Flight = new FlightActionMap();

        public class FlightActionMap
        {
            public InputAction Throttle     = new InputAction("Throttle",    InputActionType.Value);
            public InputAction Afterburner  = new InputAction("Afterburner", InputActionType.Button);
            public InputAction Airbrake     = new InputAction("Airbrake",    InputActionType.Button);
            public InputAction FireGun      = new InputAction("FireGun",     InputActionType.Button);
            public InputAction FireMissile  = new InputAction("FireMissile", InputActionType.Button);
            public InputAction CycleWeapon  = new InputAction("Cycle",       InputActionType.Button);

            public void Enable()
            {
                Throttle.Enable(); Afterburner.Enable(); Airbrake.Enable();
                FireGun.Enable();  FireMissile.Enable(); CycleWeapon.Enable();
            }
            public void Disable()
            {
                Throttle.Disable(); Afterburner.Disable(); Airbrake.Disable();
                FireGun.Disable();  FireMissile.Disable(); CycleWeapon.Disable();
            }
        }
    }
}
