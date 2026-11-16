#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace Skyrift
{
    /// <summary>
    /// Custom Unity Editor tools for the SKYRIFT project.
    /// Accessible via the top menu: Skyrift > ...
    ///
    /// Provides:
    ///   - Aircraft Stats quick-create wizard
    ///   - Mission Data quick-create wizard
    ///   - Scene setup helper (adds required singletons to active scene)
    ///   - Flight physics debug overlay (drawn in Scene view)
    /// </summary>
    public static class SkyriftEditorTools
    {
        private const string MenuRoot = "Skyrift/";

        // ─── Asset creation ───────────────────────────────────────────────────

        [MenuItem(MenuRoot + "Create/Aircraft Stats SO")]
        public static void CreateAircraftStats()
        {
            string path = GetSelectedFolder() + "/AircraftStats_New.asset";
            var asset   = ScriptableObject.CreateInstance<AircraftStats_SO>();
            ProjectWindowUtil.CreateAsset(asset, path);
        }

        [MenuItem(MenuRoot + "Create/Mission Data SO")]
        public static void CreateMissionData()
        {
            string path = GetSelectedFolder() + "/MissionData_New.asset";
            var asset   = ScriptableObject.CreateInstance<MissionData_SO>();
            ProjectWindowUtil.CreateAsset(asset, path);
        }

        // ─── Scene setup ──────────────────────────────────────────────────────

        [MenuItem(MenuRoot + "Scene Setup/Add Core Singletons")]
        public static void AddCoreSingletons()
        {
            EnsureSingleton<GameManager>("[ GameManager ]");
            EnsureSingleton<AudioManager>("[ AudioManager ]");
            EnsureSingleton<SceneLoader>("[ SceneLoader ]");

            Debug.Log("[SkyriftTools] Core singletons added to scene.");
        }

        [MenuItem(MenuRoot + "Scene Setup/Add Mission Objects")]
        public static void AddMissionObjects()
        {
            EnsureSingleton<MissionManager>("[ MissionManager ]");
            EnsureSingleton<ObjectiveTracker>("[ ObjectiveTracker ]");

            Debug.Log("[SkyriftTools] Mission objects added to scene.");
        }

        // ─── Validation ───────────────────────────────────────────────────────

        [MenuItem(MenuRoot + "Validate/Check Aircraft Prefabs")]
        public static void ValidateAircraftPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            int issues = 0;

            foreach (string guid in guids)
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                bool hasPhysics = go.GetComponent<FlightPhysics>()    != null;
                bool hasStats   = go.GetComponent<AircraftStats>()    != null;
                bool hasWeapons = go.GetComponent<WeaponSystem>()     != null;
                bool hasDamage  = go.GetComponent<DamageSystem>()     != null;

                if (hasPhysics || hasStats)
                {
                    if (!hasPhysics) { Debug.LogWarning($"[SkyriftTools] {path}: Missing FlightPhysics.");    issues++; }
                    if (!hasStats)   { Debug.LogWarning($"[SkyriftTools] {path}: Missing AircraftStats.");   issues++; }
                    if (!hasWeapons) { Debug.LogWarning($"[SkyriftTools] {path}: Missing WeaponSystem.");    issues++; }
                    if (!hasDamage)  { Debug.LogWarning($"[SkyriftTools] {path}: Missing DamageSystem.");    issues++; }
                }
            }

            Debug.Log(issues == 0
                ? "[SkyriftTools] All aircraft prefabs look valid."
                : $"[SkyriftTools] Found {issues} issue(s) in aircraft prefabs.");
        }

        [MenuItem(MenuRoot + "Validate/Check Mission Data")]
        public static void ValidateMissionData()
        {
            string[] guids = AssetDatabase.FindAssets("t:MissionData_SO", new[] { "Assets" });
            int issues = 0;

            foreach (string guid in guids)
            {
                string path  = AssetDatabase.GUIDToAssetPath(guid);
                var    asset = AssetDatabase.LoadAssetAtPath<MissionData_SO>(path);
                if (asset == null) continue;

                if (string.IsNullOrEmpty(asset.missionName))
                {
                    Debug.LogWarning($"[SkyriftTools] {path}: missionName is empty."); issues++;
                }
                if (asset.waves == null || asset.waves.Length == 0)
                {
                    Debug.LogWarning($"[SkyriftTools] {path}: No waves defined.");  issues++;
                }
                if (asset.objectiveDefinitions == null || asset.objectiveDefinitions.Length == 0)
                {
                    Debug.LogWarning($"[SkyriftTools] {path}: No objectives defined."); issues++;
                }
            }

            Debug.Log(issues == 0
                ? "[SkyriftTools] All mission data assets look valid."
                : $"[SkyriftTools] Found {issues} issue(s) in mission data.");
        }

        // ─── Debug overlay ────────────────────────────────────────────────────

        /// <summary>Draws flight-physics debug vectors in the Scene view for selected aircraft.</summary>
        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawFlightPhysicsGizmo(FlightPhysics physics, GizmoType gizmoType)
        {
            if (physics == null) return;

            Vector3 pos = physics.transform.position;

            // Forward (thrust direction)
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(pos, physics.transform.forward * 50f);

            // Velocity
            Gizmos.color = Color.green;
            Gizmos.DrawRay(pos, physics.LocalVelocity.normalized * 40f);

            // Lift (up)
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(pos, physics.transform.up * 30f);

            // Speed label
            Handles.Label(pos + Vector3.up * 5f,
                $"{physics.AircraftName_EditorOnly}\n{physics.SpeedKnots:F0} kts  |  {physics.AltitudeMeters:F0} m\n{(physics.IsStalled ? "STALLED" : "")}",
                EditorStyles.miniLabel);
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private static void EnsureSingleton<T>(string goName) where T : Component
        {
            if (Object.FindFirstObjectByType<T>() != null) return;
            GameObject go = new GameObject(goName);
            go.AddComponent<T>();
            Undo.RegisterCreatedObjectUndo(go, $"Add {typeof(T).Name}");
        }

        private static string GetSelectedFolder()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path)) return "Assets";
            return Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        }
    }

    // ─── FlightPhysics editor extension ──────────────────────────────────────
    // Exposes a read-only name property for the editor gizmo without
    // changing the runtime class.
    public static class FlightPhysicsEditorExt
    {
        public static string AircraftName_EditorOnly(this FlightPhysics fp)
        {
            var stats = fp.GetComponent<AircraftStats>();
            return stats != null ? stats.AircraftName : fp.gameObject.name;
        }
    }
}
#endif
