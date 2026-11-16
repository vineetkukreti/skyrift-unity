# SKYRIFT — Unity 6 HDRP Arcade Flight Game

Ace Combat 7-style arcade flight action, targeting mobile + PC crossplay.

---

## Unity Version

**Unity 6000.0.23f1 (Unity 6 LTS)** or later. `ProjectSettings/ProjectVersion.txt` currently records 6000.4.1f1.

## Status

Work in progress: C# gameplay scripts (flight, combat, AI, mission, UI, audio, optional multiplayer) and three scenes are in place; art assets, prefabs and the Input Actions asset still need to be created in-editor (see below). `Library/`, `Logs/`, `UserSettings/` and IDE project files are not committed — Unity regenerates them.

---

## Required Packages

Install these via **Window > Package Manager** or add directly to
`Packages/manifest.json`.

| Package | ID | Min Version |
|---|---|---|
| High Definition RP | `com.unity.render-pipelines.high-definition` | 17.0 |
| Input System | `com.unity.inputsystem` | 1.8 |
| Cinemachine | `com.unity.cinemachine` | 3.1 |
| TextMeshPro | `com.unity.textmeshpro` | 3.2 |
| Netcode for GameObjects | `com.unity.netcode.gameobjects` | 1.9 |
| Unity Transport | `com.unity.transport` | 2.3 |
| Unity Relay (UGS) | `com.unity.services.relay` | 1.4 |

---

## Project Setup (first time)

1. Open Unity Hub, click **Add > Add project from disk**, select this folder.
2. Open the project in Unity 6 (6000.0.23f1 or later).
3. Install required packages listed above.
4. Accept any "Switch to New Input System" prompts — the project uses the
   new Input System exclusively.
5. Open **Edit > Project Settings > Player** and enable
   "Active Input Handling: Input System Package (New)".
6. In **Edit > Project Settings > Graphics**, assign the included HDRP
   Global Settings asset (create one via the HDRP wizard if not present).

---

## Scene Structure

```
Assets/
  Scenes/
    MainMenu.unity         -- main menu, no player aircraft
    Loading.unity          -- async loading screen
    Mission_01.unity       -- first playable mission
```

Each mission scene contains:
- `[ GameManager ]`       — persistent singleton (DontDestroyOnLoad)
- `[ AudioManager ]`      — music + SFX singleton
- `[ MissionManager ]`    — reads MissionData_SO, spawns waves
- `[ WaveSpawner ]`       — handles enemy instantiation
- `[ ObjectiveTracker ]`  — tracks kill counts, waypoints, etc.
- `Player_Spawn`          — empty Transform marking player start point
- `Enemy_SpawnPoints[]`   — array of spawn Transforms for WaveSpawner

---

## Architecture Overview

```
GameManager (singleton)
  |
  +-- GameState (MainMenu / MissionBriefing / Loading / Flying / Paused / GameOver / Victory)
  |
  +-- MissionManager
  |     +-- MissionData_SO  (ScriptableObject — mission config)
  |     +-- WaveSpawner     (instantiates enemy prefabs)
  |     +-- ObjectiveTracker(kill counters, waypoints, escort checks)
  |
  +-- AudioManager (singleton)
        +-- EngineAudio     (per-aircraft, pitch mapped to throttle/speed)

Aircraft Prefab (player or enemy)
  |
  +-- AircraftStats      (reads AircraftStats_SO, exposes health)
  +-- FlightPhysics      (Rigidbody-based lift/drag/thrust)
  +-- FlightController   (Input System → FlightPhysics writes)
  +-- WeaponSystem       (gun pool, missile spawn, flares)
  +-- LockOnSystem       (cone scan, PN acquisition, flare break)
  +-- DamageSystem       (health, VFX, kill events)
  +-- EnemyAI            (state machine: Patrol→Engage→Attack→Evade→Retreat)
  |     +-- AIStateMachine
  |     +-- DogfightBehavior (maneuver library: Immelmann, Split-S, etc.)
  +-- FormationFlying    (wingman slot management)
  +-- CameraFollow       (chase cam + cockpit mode)
  +-- EngineAudio        (engine/wind audio)

HUD (separate Canvas)
  +-- HUDController      (speed/alt/heading, health, ammo, lock ring)
  +-- RadarSystem        (top-down blip radar)
  +-- MissionBriefingUI  (typewriter briefing, objectives list)
  +-- PauseMenu          (settings, restart, main menu)

Multiplayer (optional, requires NGO)
  +-- SkyriftNetworkManager  (host/server/client lifecycle, player spawn)
  +-- PlayerSync             (NetworkVariable state sync, weapon RPCs)
```

---

## Input Bindings (default)

| Action | Keyboard | Gamepad |
|---|---|---|
| Pitch up/down | W / S | Left stick Y |
| Roll left/right | A / D | Left stick X |
| Yaw left/right | Q / E | Right stick X |
| Throttle up/down | Left Shift / Left Ctrl | Right trigger / Left trigger |
| Afterburner | Left Shift (hold full) | Right bumper |
| Airbrakes | Space | Left bumper |
| Fire gun | Left click | Right trigger (alt) |
| Fire missile | Right click | A button |
| Deploy flare | F | B button |
| Cycle target | Tab | Y button |
| Pause | Escape | Start |
| Cockpit toggle | V | D-pad up |

Create the **SkyriftInputActions.inputactions** asset in Unity's Input
System and regenerate the `SkyriftInputActions` C# class to replace the
stub in `FlightController.cs`.

---

## Adding a New Aircraft

1. Duplicate an existing aircraft prefab.
2. Create a new **AircraftStats_SO** via **Assets > Create > Skyrift > Aircraft Stats**.
3. Fill in the stat fields (see the three reference configs in `AircraftStats_SO.cs`).
4. Assign the SO to the **AircraftStats** component on the prefab.
5. Register the prefab in **MissionData_SO > waves > enemies**.

---

## Adding a New Mission

1. Create a **MissionData_SO** via **Assets > Create > Skyrift > Mission Data**.
2. Fill in: mission name, briefing text, objectives, and wave definitions.
3. Create a new scene, add the required scene objects (see Scene Structure above).
4. Assign the MissionData_SO to the **MissionManager** in that scene.
5. Add the scene to **Build Settings**.
6. Call `GameManager.Instance.LoadMission("YourSceneName")` from your menu.

---

## Multiplayer Setup

1. Install **Netcode for GameObjects** and **Unity Transport**.
2. Open `NetworkManager.cs` and `PlayerSync.cs`, remove the
   `#if UNITY_NETCODE_GAMEOBJECTS` guards.
3. Add a **NetworkManager** component (NGO) to a persistent GameObject.
4. Assign the player aircraft prefab to the **NetworkPrefabs** list in NGO.
5. For relay/matchmaking, integrate **Unity Gaming Services** (UGS):
   - `com.unity.services.relay`
   - `com.unity.services.lobby`
   - Sign in via `AuthenticationService.Instance.SignInAnonymouslyAsync()`.

---

## HDRP Notes

- Use **HDRP Lit** materials on all aircraft meshes.
- Enable **Volumetric Clouds** in the HDRP Sky and Fog Volume for atmospheric depth.
- Missile trail effects use **VFX Graph** emitters — create Graph assets in
  `Assets/VFX/`.
- Afterburner glow uses an emissive material with an HDR colour value above 1.

---

## License

Internal development build — not licensed for distribution.
(c) 2026 SKYRIFT Development Team.
