// ==============================================================
// PlayerDeathListener.cs
// --------------------------------------------------------------
// PURPOSE:
//   Bridges the player's death event (HealthComponent.OnDied)
//   to the GameManager FSM transition (GameOver state).
//
// PLACEMENT:
//   Attach to the Player's root GameObject — the same one that
//   has HealthComponent. Works alongside PlayerRegistration.
//
// ARCHITECTURE:
//   - Subscribes to HealthComponent.OnDied at Start (after
//     HealthComponent.Start() has initialised its state).
//   - When the player dies, calls GameManager.GameOver and
//     unregisters the player from GameManager so enemies fall
//     back to idle safely.
//   - Also listens to GameManager.OnStateChanged so it can
//     self-silence if the game is already over (race-condition
//     guard in case two damage sources kill the player in the
//     same frame and the state has already transitioned).
// ==============================================================

using UnityEngine;

/// <summary>
/// Attach to the Player root GameObject.
/// Listens to <see cref="HealthComponent.OnDied"/> and drives
/// the <see cref="GameManager"/> FSM into <c>GameOver</c> state.
/// </summary>
[RequireComponent(typeof(HealthComponent))]
public class PlayerDeathListener : MonoBehaviour
{
    // ----------------------------------------------------------
    // PRIVATE REFERENCES
    // ----------------------------------------------------------

    private HealthComponent _health;

    // ----------------------------------------------------------
    // UNITY LIFECYCLE
    // ----------------------------------------------------------

    private void Awake()
    {
        _health = GetComponent<HealthComponent>();
    }

    private void OnEnable()
    {
        // Subscribe to the static GameManager event so we can detect
        // if the game ends (e.g. Victory) before the player formally dies.
        GameManager.OnStateChanged += OnGameStateChanged;
    }

    private void OnDisable()
    {
        GameManager.OnStateChanged -= OnGameStateChanged;
        UnsubscribeFromHealth();
    }

    private void Start()
    {
        // Subscribe AFTER HealthComponent.Start() has run so the health
        // is properly initialised before we hook into death events.
        SubscribeToHealth();
    }

    // ----------------------------------------------------------
    // EVENT SUBSCRIPTIONS
    // ----------------------------------------------------------

    private void SubscribeToHealth()
    {
        if (_health == null)
        {
            Debug.LogError("[PlayerDeathListener] No HealthComponent found on this GameObject. " +
                           "PlayerDeathListener requires a HealthComponent on the same GameObject.", this);
            return;
        }

        _health.OnDied += OnPlayerDied;
        Debug.Log("[PlayerDeathListener] Subscribed to HealthComponent.OnDied.", this);
    }

    private void UnsubscribeFromHealth()
    {
        if (_health != null)
        {
            _health.OnDied -= OnPlayerDied;
        }
    }

    // ----------------------------------------------------------
    // EVENT HANDLERS
    // ----------------------------------------------------------

    /// <summary>
    /// Called by <see cref="HealthComponent.OnDied"/> when the player's
    /// health reaches zero. Drives the FSM to <c>GameOver</c> and clears
    /// the player reference in <see cref="GameManager"/>.
    /// </summary>
    private void OnPlayerDied()
    {
        // Guard: do not trigger GameOver if the session has already ended
        // (e.g. Victory was declared in the same frame, or game is not playing).
        if (GameManager.Instance == null) return;

        var currentState = GameManager.Instance.CurrentState;
        if (currentState != GameManager.GameState.Playing &&
            currentState != GameManager.GameState.Pause)
        {
            Debug.Log("[PlayerDeathListener] Player died but game is not in Playing state. " +
                      $"Current state: {currentState}. Ignoring.", this);
            return;
        }

        Debug.Log("[PlayerDeathListener] Player has died → triggering GameOver.", this);

        // Unsubscribe immediately so no further death events slip through.
        UnsubscribeFromHealth();

        // Unregister the player so enemies transition to idle.
        GameManager.Instance.UnregisterPlayer();

        // Drive the FSM to GameOver — UIManager and AudioManager will react.
        GameManager.Instance.ChangeState(GameManager.GameState.GameOver);
    }

    /// <summary>
    /// Called when the game state changes globally.
    /// Used as a safety guard: if the game ends (Victory/GameOver) for
    /// any external reason (e.g. WinConditionManager), we stop listening
    /// to health events to prevent a redundant GameOver call.
    /// </summary>
    private void OnGameStateChanged(GameManager.GameState newState)
    {
        if (newState == GameManager.GameState.GameOver ||
            newState == GameManager.GameState.Victory)
        {
            // Session over — no longer need to monitor player death.
            UnsubscribeFromHealth();
        }
    }
}
