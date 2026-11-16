using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Skyrift
{
    // ─── Objective type ───────────────────────────────────────────────────────
    public enum ObjectiveType
    {
        DestroyAll,       // destroy every enemy in the wave
        DestroyCount,     // destroy N enemies
        DefendTarget,     // keep a target alive for X seconds
        ReachWaypoint,    // fly through a trigger zone
        Escort,           // keep escort target alive while reaching a point
        TimeTrial         // complete primary objectives before time runs out
    }

    /// <summary>
    /// Tracks individual mission objectives and reports completion.
    /// Drives the objectives HUD list; each objective is displayed with a
    /// checkbox and progress counter.
    /// </summary>
    public class ObjectiveTracker : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────
        [Header("HUD")]
        [SerializeField] private TMP_Text objectivesListText;
        [SerializeField] private Color    completedColor = Color.green;
        [SerializeField] private Color    activeColor    = Color.white;
        [SerializeField] private Color    failedColor    = Color.red;

        // ─── Events ───────────────────────────────────────────────────────────
        /// <summary>Fired once when every objective is in the Completed state.</summary>
        public event Action OnAllObjectivesComplete;

        /// <summary>Fired whenever a single objective changes state.</summary>
        public event Action<ObjectiveState> OnObjectiveUpdated;

        // ─── Inner types ──────────────────────────────────────────────────────
        public enum ObjStatus { Pending, Active, Completed, Failed }

        [Serializable]
        public class ObjectiveState
        {
            public string       Label;
            public ObjectiveType Type;
            public ObjStatus     Status;
            public int           RequiredCount;
            public int           CurrentCount;
            public bool          IsPrimary;

            public bool IsComplete => Status == ObjStatus.Completed;
            public float Progress  => RequiredCount > 0
                ? Mathf.Clamp01((float)CurrentCount / RequiredCount) : 0f;
        }

        // ─── Private ──────────────────────────────────────────────────────────
        private readonly List<ObjectiveState> _objectives = new List<ObjectiveState>();
        private MissionData_SO                _missionData;

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Populates objectives from the given mission data.</summary>
        public void InitialiseObjectives(MissionData_SO data)
        {
            if (data == null) return;
            _missionData = data;
            _objectives.Clear();

            if (data.objectiveDefinitions == null) return;

            foreach (var def in data.objectiveDefinitions)
            {
                _objectives.Add(new ObjectiveState
                {
                    Label         = def.label,
                    Type          = def.type,
                    Status        = ObjStatus.Active,
                    RequiredCount = def.requiredCount,
                    CurrentCount  = 0,
                    IsPrimary     = def.isPrimary
                });
            }

            RefreshHUD();
        }

        /// <summary>
        /// Notifies the tracker that an enemy was destroyed.
        /// Advances any DestroyAll / DestroyCount objectives.
        /// </summary>
        public void NotifyEnemyKilled(DamageSystem victim)
        {
            bool changed = false;
            foreach (var obj in _objectives)
            {
                if (obj.Status != ObjStatus.Active) continue;
                if (obj.Type != ObjectiveType.DestroyAll && obj.Type != ObjectiveType.DestroyCount)
                    continue;

                obj.CurrentCount++;
                if (obj.RequiredCount > 0 && obj.CurrentCount >= obj.RequiredCount)
                    SetObjectiveComplete(obj);

                changed = true;
            }

            if (changed) RefreshHUD();
        }

        /// <summary>Marks a waypoint objective as complete by index.</summary>
        public void NotifyWaypointReached(int objectiveIndex)
        {
            if (objectiveIndex < 0 || objectiveIndex >= _objectives.Count) return;
            var obj = _objectives[objectiveIndex];
            if (obj.Type == ObjectiveType.ReachWaypoint)
                SetObjectiveComplete(obj);
        }

        /// <summary>Marks an escort objective complete or failed.</summary>
        public void NotifyEscortResult(bool survived)
        {
            foreach (var obj in _objectives)
            {
                if (obj.Type != ObjectiveType.Escort || obj.Status != ObjStatus.Active) continue;
                if (survived) SetObjectiveComplete(obj);
                else          SetObjectiveFailed(obj);
            }
        }

        /// <summary>Marks a defend-target objective complete (called by timer).</summary>
        public void NotifyDefendComplete()
        {
            foreach (var obj in _objectives)
            {
                if (obj.Type == ObjectiveType.DefendTarget && obj.Status == ObjStatus.Active)
                    SetObjectiveComplete(obj);
            }
        }

        // ─── Private helpers ─────────────────────────────────────────────────
        private void SetObjectiveComplete(ObjectiveState obj)
        {
            obj.Status       = ObjStatus.Completed;
            obj.CurrentCount = obj.RequiredCount;
            OnObjectiveUpdated?.Invoke(obj);
            RefreshHUD();
            CheckAllComplete();
        }

        private void SetObjectiveFailed(ObjectiveState obj)
        {
            obj.Status = ObjStatus.Failed;
            OnObjectiveUpdated?.Invoke(obj);
            RefreshHUD();

            if (obj.IsPrimary)
                MissionManager.Instance?.FailMission($"Objective failed: {obj.Label}");
        }

        private void CheckAllComplete()
        {
            foreach (var obj in _objectives)
            {
                if (obj.IsPrimary && obj.Status != ObjStatus.Completed)
                    return;
            }
            OnAllObjectivesComplete?.Invoke();
        }

        private void RefreshHUD()
        {
            if (objectivesListText == null) return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (var obj in _objectives)
            {
                string prefix = obj.Status switch
                {
                    ObjStatus.Completed => "<color=#00FF00>[X]</color> ",
                    ObjStatus.Failed    => "<color=#FF4444>[!]</color> ",
                    _                   => "<color=#FFFFFF>[ ]</color> "
                };

                string counter = (obj.RequiredCount > 1)
                    ? $" ({obj.CurrentCount}/{obj.RequiredCount})"
                    : "";

                string tag = obj.IsPrimary ? "" : " (Secondary)";

                sb.AppendLine($"{prefix}{obj.Label}{counter}{tag}");
            }
            objectivesListText.text = sb.ToString();
        }
    }
}
