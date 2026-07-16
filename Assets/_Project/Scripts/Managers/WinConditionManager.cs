// ==============================================================
// WinConditionManager.cs
// --------------------------------------------------------------
// PURPOSE:
//   Tracks the number of enemies defeated and triggers Victory
//   when the kill threshold is reached.
//
// PLACEMENT:
//   Attach to the "GameManagers" GameObject in your scene
//   (alongside GameManager, AudioManager, etc.) — OR create a
//   dedicated empty GameObject named "WinConditionManager".
//   Only ONE instance should exist per scene.
//
// ARCHITECTURE:
//   - Subscribes to OnEnemyDied for BOTH EnemyVehicleBase (Kamikaze)
//     AND CombatVehicleAI (Shooter) instances alive when Playing begins.
//   - Listens to GameManager.OnStateChanged to:
//       a) Reset + re-discover enemies on a new game (Playing state).
//       b) Silence all listeners on GameOver (player died first).
//   - Uses two parallel Dictionary<T, Action> tables — one per enemy
//     type — storing named delegates for clean per-enemy unsubscription
//     (avoids anonymous-lambda memory leaks).
//   - For runtime-spawned enemies, call the appropriate RegisterEnemy()
//     overload from your WaveManager or spawn pool after instantiation.
//
// REQUIREMENTS:
//   - EnemyVehicleBase must be in the scene (or registered via
//     RegisterEnemy) BEFORE or right as Playing state is entered.
//   - killsRequired is Inspector-tunable per level.
// ==============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks enemy kills and triggers <see cref="GameManager.GameState.Victory"/>
/// when <see cref="killsRequired"/> enemies have been defeated.
/// Place this component on the GameManagers / scene root GameObject.
/// </summary>
public class WinConditionManager : MonoBehaviour
{
    // ----------------------------------------------------------
    // INSPECTOR FIELDS
    // ----------------------------------------------------------

    [Header("Win Condition")]
    [Tooltip("How many enemies must be defeated to trigger Victory. " +
             "Adjust per level or drive from a WaveManager.")]
    [SerializeField] private int killsRequired = 5;

    // ----------------------------------------------------------
    // PRIVATE STATE
    // ----------------------------------------------------------

    /// <summary>Current number of confirmed enemy kills this session.</summary>
    private int _killCount;

    /// <summary>
    /// Maps each tracked enemy to the Action delegate we subscribed,
    /// so we can unsubscribe cleanly without anonymous-lambda issues.
    /// </summary>
    private readonly Dictionary<EnemyVehicleBase, Action> _enemyHandlers
        = new Dictionary<EnemyVehicleBase, Action>();

    /// <summary>
    /// Maps each <see cref="CombatVehicleAI"/> Shooter enemy to its
    /// stored delegate, mirroring the pattern used by _enemyHandlers.
    /// </summary>
    private readonly Dictionary<CombatVehicleAI, Action> _combatAIHandlers
        = new Dictionary<CombatVehicleAI, Action>();

    /// <summary>
    /// Set to true once win/loss is resolved so no further kills pass through
    /// (concurrent-death guard for multi-kill same-frame scenarios).
    /// </summary>
    private bool _sessionEnded;

    // ----------------------------------------------------------
    // PUBLIC READ-ONLY ACCESSORS
    // ----------------------------------------------------------

    /// <summary>Enemies defeated so far in the current session.</summary>
    public int KillCount => _killCount;

    /// <summary>Kill goal for this session.</summary>
    public int KillsRequired => killsRequired;

    // ----------------------------------------------------------
    // UNITY LIFECYCLE
    // ----------------------------------------------------------

    private void OnEnable()
    {
        GameManager.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        GameManager.OnStateChanged -= OnGameStateChanged;
        UnsubscribeAllEnemies();
    }

    // ----------------------------------------------------------
    // GAME STATE LISTENER
    // ----------------------------------------------------------

    /// <summary>
    /// Reacts to FSM transitions:<br/>
    /// • <b>Playing</b>  → reset session state, re-discover scene enemies.<br/>
    /// • <b>GameOver</b> → unsubscribe all; the player died first.<br/>
    /// • <b>Victory</b>  → same cleanup (should be self-triggered, but safe).
    /// </summary>
    private void OnGameStateChanged(GameManager.GameState newState)
    {
        switch (newState)
        {
            case GameManager.GameState.Playing:
                ResetSession();
                DiscoverAndRegisterEnemies();
                break;

            case GameManager.GameState.GameOver:
            case GameManager.GameState.Victory:
                _sessionEnded = true;
                UnsubscribeAllEnemies();
                break;
        }
    }

    // ----------------------------------------------------------
    // SESSION MANAGEMENT
    // ----------------------------------------------------------

    /// <summary>
    /// Resets kill counter and clears tracked enemies.
    /// Called at the start of each Playing session (including restarts).
    /// </summary>
    private void ResetSession()
    {
        _killCount    = 0;
        _sessionEnded = false;
        _enemyHandlers.Clear();
        _combatAIHandlers.Clear();

        Debug.Log($"[WinConditionManager] Session reset. Goal: {killsRequired} kills.", this);
    }

    /// <summary>
    /// Scene-wide enemy discovery. Finds all <see cref="EnemyVehicleBase"/>
    /// (Kamikaze) AND <see cref="CombatVehicleAI"/> (Shooter) instances
    /// present at the moment Playing begins and registers them.
    /// Uses <c>FindObjectsByType</c> (Unity 2023.1+ API).
    /// </summary>
    private void DiscoverAndRegisterEnemies()
    {
        // --- Kamikaze enemies (EnemyVehicleBase subclasses) ---
        EnemyVehicleBase[] baseEnemies =
            FindObjectsByType<EnemyVehicleBase>(FindObjectsSortMode.None);

        foreach (EnemyVehicleBase enemy in baseEnemies)
        {
            RegisterEnemy(enemy);
        }

        // --- Shooter enemies (CombatVehicleAI — does not inherit EnemyVehicleBase) ---
        CombatVehicleAI[] shooters =
            FindObjectsByType<CombatVehicleAI>(FindObjectsSortMode.None);

        foreach (CombatVehicleAI shooter in shooters)
        {
            RegisterEnemy(shooter);
        }

        int total = _enemyHandlers.Count + _combatAIHandlers.Count;
        Debug.Log($"[WinConditionManager] Registered {total} enemies in scene "
                + $"({_enemyHandlers.Count} EnemyVehicleBase, "
                + $"{_combatAIHandlers.Count} CombatVehicleAI).", this);
    }

    // ----------------------------------------------------------
    // PUBLIC API — Runtime Enemy Registration
    // ----------------------------------------------------------

    /// <summary>
    /// Registers a Kamikaze (<see cref="EnemyVehicleBase"/>) enemy.
    /// Call this from a WaveManager or spawn system every time a new
    /// enemy is instantiated at runtime after the scene loads.
    /// </summary>
    /// <param name="enemy">The <see cref="EnemyVehicleBase"/> to track. Null-safe.</param>
    public void RegisterEnemy(EnemyVehicleBase enemy)
    {
        if (enemy == null)
        {
            Debug.LogWarning("[WinConditionManager] RegisterEnemy(EnemyVehicleBase) called with a null reference.", this);
            return;
        }

        if (_enemyHandlers.ContainsKey(enemy)) return; // Prevent duplicate subscriptions.

        // Store the named delegate so it can be removed precisely.
        Action handler = () => OnEnemyKilled(enemy);
        _enemyHandlers[enemy] = handler;
        enemy.OnEnemyDied    += handler;
    }

    /// <summary>
    /// Registers a Shooter (<see cref="CombatVehicleAI"/>) enemy.
    /// Mirrors <see cref="RegisterEnemy(EnemyVehicleBase)"/> exactly;
    /// routes death notification through <see cref="OnCombatAIKilled"/>.
    /// Call this from a WaveManager whenever a Shooter is spawned at runtime.
    /// </summary>
    /// <param name="enemy">The <see cref="CombatVehicleAI"/> to track. Null-safe.</param>
    public void RegisterEnemy(CombatVehicleAI enemy)
    {
        if (enemy == null)
        {
            Debug.LogWarning("[WinConditionManager] RegisterEnemy(CombatVehicleAI) called with a null reference.", this);
            return;
        }

        if (_combatAIHandlers.ContainsKey(enemy)) return; // Prevent duplicate subscriptions.

        Action handler = () => OnCombatAIKilled(enemy);
        _combatAIHandlers[enemy] = handler;
        enemy.OnEnemyDied       += handler;
    }

    // ----------------------------------------------------------
    // ENEMY DEATH HANDLERS
    // ----------------------------------------------------------

    /// <summary>
    /// Invoked when a registered <see cref="EnemyVehicleBase"/> (Kamikaze) dies.
    /// Increments the kill counter and checks the win condition.
    /// </summary>
    private void OnEnemyKilled(EnemyVehicleBase enemy)
    {
        RegisterKill(enemy != null ? enemy.name : "Unknown EnemyVehicleBase");
    }

    /// <summary>
    /// Invoked when a registered <see cref="CombatVehicleAI"/> (Shooter) dies.
    /// Increments the kill counter and checks the win condition.
    /// </summary>
    private void OnCombatAIKilled(CombatVehicleAI enemy)
    {
        RegisterKill(enemy != null ? enemy.name : "Unknown CombatVehicleAI");
    }

    /// <summary>
    /// Shared kill-registration logic called by both death handlers.
    /// Keeps the increment + guard logic in a single place (DRY).
    /// </summary>
    /// <param name="enemyName">Display name used for the Debug.Log.</param>
    private void RegisterKill(string enemyName)
    {
        // Guard: session already resolved (GameOver or Victory).
        if (_sessionEnded) return;

        // Guard: only count kills while actively Playing.
        if (GameManager.Instance == null ||
            GameManager.Instance.CurrentState != GameManager.GameState.Playing)
        {
            return;
        }

        _killCount++;
        Debug.Log($"[WinConditionManager] Kill confirmed: '{enemyName}'. " +
                  $"Progress: {_killCount}/{killsRequired}.", this);

        CheckWinCondition();
    }

    /// <summary>
    /// Evaluates whether the kill threshold has been reached.
    /// Fires at most once per session thanks to the <see cref="_sessionEnded"/> flag.
    /// </summary>
    private void CheckWinCondition()
    {
        if (_killCount < killsRequired) return;

        _sessionEnded = true;
        UnsubscribeAllEnemies();

        Debug.Log($"[WinConditionManager] 🏆 Victory! {_killCount}/{killsRequired} enemies defeated.", this);
        GameManager.Instance.ChangeState(GameManager.GameState.Victory);
    }

    // ----------------------------------------------------------
    // CLEANUP
    // ----------------------------------------------------------

    /// <summary>
    /// Unsubscribes from every tracked enemy (both types) using the stored
    /// delegates, then clears both dictionaries.
    /// Prevents dangling event references on scene reload or game end.
    /// </summary>
    private void UnsubscribeAllEnemies()
    {
        // --- EnemyVehicleBase (Kamikaze) ---
        foreach (var kvp in _enemyHandlers)
        {
            if (kvp.Key != null)
            {
                kvp.Key.OnEnemyDied -= kvp.Value;
            }
        }
        _enemyHandlers.Clear();

        // --- CombatVehicleAI (Shooter) ---
        foreach (var kvp in _combatAIHandlers)
        {
            if (kvp.Key != null)
            {
                kvp.Key.OnEnemyDied -= kvp.Value;
            }
        }
        _combatAIHandlers.Clear();
    }
}
