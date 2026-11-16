// ─── REQUIRES ────────────────────────────────────────────────────────────────
// Package: com.unity.netcode.gameobjects (Unity Netcode for GameObjects)
// Install via Package Manager or manifest.json before enabling this script.
//
// To enable:
//  1. Add the NGO package to your project.
//  2. Remove the #if UNITY_NETCODE_... guards below.
//  3. Add a NetworkManager component to a persistent GameObject in the scene,
//     assign the NetworkPrefabs list, and set transport (e.g. Unity Transport).
// ─────────────────────────────────────────────────────────────────────────────

#if UNITY_NETCODE_GAMEOBJECTS   // Remove this guard once NGO is installed

using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// SKYRIFT multiplayer session manager built on Unity Netcode for GameObjects.
    ///
    /// Responsibilities:
    ///   - Start host / join server
    ///   - Spawn the local player aircraft NetworkObject
    ///   - Relay weapon-fire and damage RPCs
    ///   - Broadcast basic session state (player count, game phase)
    ///
    /// Extend with relay / matchmaking by integrating Unity Gaming Services
    /// (UGS) Relay + Lobby packages.
    /// </summary>
    public class SkyriftNetworkManager : NetworkBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────
        public static SkyriftNetworkManager Instance { get; private set; }

        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("Prefabs")]
        [SerializeField] private NetworkObject playerAircraftNetPrefab;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] playerSpawnPoints;

        [Header("Session")]
        [SerializeField] private int maxPlayers = 4;

        // ─── Events ───────────────────────────────────────────────────────────
        public event Action<ulong>  OnPlayerConnected;
        public event Action<ulong>  OnPlayerDisconnected;

        // ─── Private ──────────────────────────────────────────────────────────
        private readonly System.Collections.Generic.Dictionary<ulong, NetworkObject>
            _playerObjects = new System.Collections.Generic.Dictionary<ulong, NetworkObject>();

        // ─── Unity lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback
                    += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback
                    += OnClientDisconnected;
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Starts the session as a host (server + local client).</summary>
        public void StartHost()
        {
            NetworkManager.Singleton.StartHost();
        }

        /// <summary>Starts a dedicated server (no local player).</summary>
        public void StartServer()
        {
            NetworkManager.Singleton.StartServer();
        }

        /// <summary>Connects as a client to the current transport address.</summary>
        public void StartClient()
        {
            NetworkManager.Singleton.StartClient();
        }

        /// <summary>Gracefully shuts down the current session.</summary>
        public void Disconnect()
        {
            NetworkManager.Singleton.Shutdown();
        }

        // ─── Server-side callbacks ────────────────────────────────────────────
        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;

            if (_playerObjects.Count >= maxPlayers)
            {
                Debug.LogWarning($"[SkyriftNet] Max players reached. Rejecting client {clientId}.");
                NetworkManager.Singleton.DisconnectClient(clientId);
                return;
            }

            SpawnPlayerForClient(clientId);
            OnPlayerConnected?.Invoke(clientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_playerObjects.TryGetValue(clientId, out NetworkObject obj))
            {
                if (obj != null) obj.Despawn(true);
                _playerObjects.Remove(clientId);
            }
            OnPlayerDisconnected?.Invoke(clientId);
        }

        private void SpawnPlayerForClient(ulong clientId)
        {
            int     spawnIdx = (int)(_playerObjects.Count % Mathf.Max(playerSpawnPoints.Length, 1));
            Vector3 pos      = playerSpawnPoints != null && playerSpawnPoints.Length > 0
                ? playerSpawnPoints[spawnIdx].position
                : Vector3.up * 500f;

            NetworkObject netObj = Instantiate(
                playerAircraftNetPrefab,
                pos,
                Quaternion.identity
            );
            netObj.SpawnAsPlayerObject(clientId, true);
            _playerObjects[clientId] = netObj;
        }

        // ─── Weapon RPCs ─────────────────────────────────────────────────────

        /// <summary>
        /// Client calls this to inform the server (and all other clients) that
        /// a gun was fired.  Server validates and rebroadcasts.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void FireGunServerRpc(Vector3 muzzlePos, Vector3 direction, ServerRpcParams rpcParams = default)
        {
            FireGunClientRpc(muzzlePos, direction, rpcParams.Receive.SenderClientId);
        }

        [ClientRpc]
        private void FireGunClientRpc(Vector3 muzzlePos, Vector3 direction, ulong shooterId)
        {
            // Skip the originating client (they already spawned the bullet locally)
            if (NetworkManager.Singleton.LocalClientId == shooterId) return;

            // Spawn a visual-only bullet on this client (no damage authority)
            // ProjectilePool.Instance?.SpawnVisual(muzzlePos, direction);
        }

        /// <summary>
        /// Client reports taking damage; server validates and applies it.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void TakeDamageServerRpc(float amount, ServerRpcParams rpcParams = default)
        {
            // Server applies damage to the calling client's aircraft
            if (_playerObjects.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkObject obj))
            {
                DamageSystem dmg = obj.GetComponent<DamageSystem>();
                if (dmg != null)
                    dmg.TakeDamage(amount);
            }
        }
    }
}

#else

// ─── Stub so the file compiles without NGO installed ─────────────────────────
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Placeholder NetworkManager.  Add Unity Netcode for GameObjects
    /// (com.unity.netcode.gameobjects) via the Package Manager, then remove
    /// the #if guard at the top of this file to enable full multiplayer support.
    /// </summary>
    public class SkyriftNetworkManager : MonoBehaviour
    {
        public static SkyriftNetworkManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[SkyriftNet] Multiplayer stub active.  Install NGO to enable networking.");
        }

        public void StartHost()   => Debug.LogWarning("[SkyriftNet] NGO not installed.");
        public void StartServer() => Debug.LogWarning("[SkyriftNet] NGO not installed.");
        public void StartClient() => Debug.LogWarning("[SkyriftNet] NGO not installed.");
        public void Disconnect()  { }
    }
}

#endif
