using System;
using UnityEngine;

namespace Skyrift
{
    /// <summary>
    /// Enumerates every high-level state the game can be in.
    /// Transitions are driven by GameManager.
    /// </summary>
    public enum GameState
    {
        MainMenu,
        MissionBriefing,
        Loading,
        Flying,
        Paused,
        GameOver,
        Victory
    }

    /// <summary>
    /// Difficulty preset that scales enemy aggression, damage, and spawn rates.
    /// </summary>
    [Serializable]
    public enum DifficultyLevel
    {
        Rookie   = 0,
        Pilot    = 1,
        Ace      = 2,
        Legend   = 3
    }

    /// <summary>
    /// Flat data bag passed between systems when the game state changes.
    /// Listeners subscribe to GameManager.OnGameStateChanged(GameStateContext).
    /// </summary>
    [Serializable]
    public class GameStateContext
    {
        /// <summary>Previous state before the transition.</summary>
        public GameState PreviousState;

        /// <summary>New state the game has entered.</summary>
        public GameState NewState;

        /// <summary>Wall-clock time when the transition occurred.</summary>
        public float TransitionTime;

        /// <summary>Active difficulty at time of transition.</summary>
        public DifficultyLevel Difficulty;

        public GameStateContext(GameState prev, GameState next, DifficultyLevel diff)
        {
            PreviousState  = prev;
            NewState       = next;
            TransitionTime = Time.time;
            Difficulty     = diff;
        }
    }
}
