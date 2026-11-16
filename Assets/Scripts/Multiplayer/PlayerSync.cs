// Enable this file only after installing Unity Netcode for GameObjects.
// See NetworkManager.cs for installation instructions.

#if UNITY_NETCODE_GAMEOBJECTS

using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Synchronises an aircraft's transform, velocity, throttle, and weapon
    /// state across the network.
    ///
    /// Attach to the aircraft prefab alongside a <see cref="NetworkObject"/>
    /// and a <see cref="NetworkRigidbody"/>.
    ///
    /// The owning client writes input-derived state into NetworkVariables;
    /// all other clients read them and apply interpolated results.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerSync : NetworkBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [SerializeField] private float positionInterpolationSpeed = 15f;
        [SerializeField] private float rotationInterpolationSpeed = 12f;

        // ─── Networked variables ──────────────────────────────────────────────
        // Position + rotation are handled by NetworkTransform / NetworkRigidbody.
        // We manually sync the additional gameplay state below.

        private readonly NetworkVariable<float>   _netThrottle
            = new NetworkVariable<float>(0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool>    _netAfterburner
            = new NetworkVariable<bool>(false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Vector3> _netVelocity
            = new NetworkVariable<Vector3>(Vector3.zero,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<float>   _netHealth
            = new NetworkVariable<float>(1f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        // ─── Private ──────────────────────────────────────────────────────────
        private FlightPhysics    _physics;
        private FlightController _controller;
        private AircraftStats    _stats;
        private DamageSystem     _damage;
        private Rigidbody        _rb;

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _physics    = GetComponent<FlightPhysics>();
            _controller = GetComponent<FlightController>();
            _stats      = GetComponent<AircraftStats>();
            _damage     = GetComponent<DamageSystem>();
            _rb         = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Non-owner clients: disable local input and physics (use synced values)
            if (!IsOwner)
            {
                if (_controller != null) _controller.enabled = false;
                if (_rb         != null) _rb.isKinematic     = true;
            }

            // Server tracks health changes to sync to clients
            if (IsServer && _damage != null)
            {
                _damage.OnDamageTaken += normHealth => _netHealth.Value = normHealth;
            }
        }

        private void FixedUpdate()
        {
            if (IsOwner)
                WriteOwnerState();
            else
                ApplyRemoteState();
        }

        // ─── Owner writes ────────────────────────────────────────────────────
        private void WriteOwnerState()
        {
            if (_controller != null)
            {
                _netThrottle.Value     = _controller.Throttle;
                _netAfterburner.Value  = _controller.AfterburnerActive;
            }
            if (_rb != null)
                _netVelocity.Value = _rb.linearVelocity;
        }

        // ─── Remote interpolation ────────────────────────────────────────────
        private void ApplyRemoteState()
        {
            // NetworkTransform/NetworkRigidbody handles position/rotation.
            // We just apply the extra state fields.
            if (_physics != null)
            {
                _physics.ThrottleInput    = _netThrottle.Value;
                _physics.AfterburnerInput = _netAfterburner.Value ? 1f : 0f;
            }
        }

        // ─── Weapon RPCs ─────────────────────────────────────────────────────

        /// <summary>
        /// Called by the owning client's WeaponSystem when a gun is fired.
        /// Forwards through the SkyriftNetworkManager RPC chain.
        /// </summary>
        public void NotifyGunFired(Vector3 muzzlePos, Vector3 direction)
        {
            if (!IsOwner) return;
            SkyriftNetworkManager.Instance?.FireGunServerRpc(muzzlePos, direction);
        }

        /// <summary>
        /// Called server-side when an authoritative damage event needs to be
        /// applied to this aircraft.
        /// </summary>
        [ClientRpc]
        public void ApplyDamageClientRpc(float amount)
        {
            if (_damage != null)
                _damage.TakeDamage(amount);
        }
    }
}

#else

using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Stub PlayerSync — no-op until Unity Netcode for GameObjects is installed.
    /// </summary>
    public class PlayerSync : MonoBehaviour { }
}

#endif
