// ==============================================================
// HealthComponent.cs
// --------------------------------------------------------------
// PURPOSE:
//   A reusable, self-contained health state manager that can be
//   attached to ANY entity (Player, Enemy, Destructible Prop).
//   It implements IDamageable and communicates all health changes
//   outward via C# events (Observer Pattern), keeping it
//   completely decoupled from UI, animations, and AI systems.
//
// STRICT RULES — This script MUST NOT:
//   - Update any UI element directly.
//   - Destroy the GameObject it lives on.
//   - Trigger any animations.
//   All those concerns belong to LISTENERS of its events.
//
// HOW TO USE:
//   1. Attach this component to any entity that should have health.
//   2. Subscribe external systems to OnHealthChanged and OnDied.
//      Example (in a HealthBar UI script):
//        healthComponent.OnHealthChanged += UpdateBar;
//        healthComponent.OnDied          += ShowDeathScreen;
// ==============================================================

using System;
using UnityEngine;

/// <summary>
/// A universal health state manager implementing <see cref="IDamageable"/>.
/// Uses C# events to notify subscribers of health changes and death,
/// following the Observer Pattern.
/// </summary>
public class HealthComponent : MonoBehaviour, IDamageable
{
    // ----------------------------------------------------------
    // INSPECTOR FIELDS
    // All state is private. [SerializeField] exposes them to the
    // Unity Inspector without breaking encapsulation.
    // ----------------------------------------------------------

    [Header("Health Settings")]

    [Tooltip("The maximum health points this entity starts with.")]
    [SerializeField] private int maxHealth = 100;

    // ----------------------------------------------------------
    // PRIVATE STATE
    // Runtime state is kept private to prevent external mutation.
    // ----------------------------------------------------------

    /// <summary>The entity's current health at runtime.</summary>
    private int currentHealth;

    /// <summary>
    /// Guard flag that prevents any further processing once the
    /// entity has died. Checked at the top of TakeDamage().
    /// </summary>
    private bool isDead;

    // ----------------------------------------------------------
    // PUBLIC EVENTS (Observer Pattern)
    // External systems subscribe to these to react to health
    // changes. This component never knows who is listening.
    // ----------------------------------------------------------

    /// <summary>
    /// Fired whenever health changes. Passes the normalized health
    /// fraction (0.0 = empty, 1.0 = full) for use by UI health bars.
    /// </summary>
    public event Action<float> OnHealthChanged;

    /// <summary>
    /// Fired once, exactly when health reaches zero for the first time.
    /// Listeners can use this to play death animations, trigger FSM
    /// transitions, award score, etc.
    /// </summary>
    public event Action OnDied;

    // ----------------------------------------------------------
    // UNITY LIFECYCLE
    // ----------------------------------------------------------

    /// <summary>
    /// Initialises health to its maximum value and resets the
    /// dead flag on component start.
    /// </summary>
    private void Start()
    {
        currentHealth = maxHealth;
        isDead = false;

        // Broadcast initial state so any listeners that subscribe
        // before Start() runs are immediately synchronised.
        OnHealthChanged?.Invoke(GetNormalizedHealth());
    }

    // ----------------------------------------------------------
    // IDamageable IMPLEMENTATION
    // ----------------------------------------------------------

    /// <summary>
    /// Applies damage to this entity, clamping health to a minimum
    /// of zero. Fires <see cref="OnHealthChanged"/> on every hit and
    /// <see cref="OnDied"/> exactly once when health reaches zero.
    /// </summary>
    /// <param name="amount">
    /// Positive integer damage value to subtract from current health.
    /// </param>
    public void TakeDamage(int amount)
    {
        // Guard: silently ignore all damage once the entity is dead.
        // This prevents double-death triggers (e.g., two projectiles
        // hitting in the same frame) and keeps event logic clean.
        if (isDead) return;

        // Validate input to prevent accidental negative-damage exploits
        // that would effectively heal the entity through this method.
        if (amount <= 0) return;

        // Subtract damage and clamp so health never goes below zero.
        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        // Always notify health-change listeners (e.g., UI health bar).
        OnHealthChanged?.Invoke(GetNormalizedHealth());

        // Check for death condition.
        if (currentHealth <= 0)
        {
            isDead = true;

            // Notify death listeners (FSM, animation controller, game
            // manager, etc.). This component's job ends here — what
            // happens next is the listener's responsibility.
            OnDied?.Invoke();
        }
    }

    // ----------------------------------------------------------
    // PUBLIC READ-ONLY ACCESSORS
    // Expose read-only state without breaking encapsulation.
    // No public setters exist — only TakeDamage() mutates state.
    // ----------------------------------------------------------

    /// <summary>Devuelve la vida actual (útil para UI de texto y Debug).</summary>
    public int CurrentHealth => currentHealth;

    /// <summary>Devuelve la vida máxima.</summary>
    public int MaxHealth => maxHealth;

    /// <summary>
    /// Returns the current health as a normalised float between 0 and 1.
    /// Useful for driving UI sliders or shader effects.
    /// </summary>
    /// <returns>A float in the range [0.0, 1.0].</returns>
    public float GetNormalizedHealth()
    {
        // Guard against division by zero if maxHealth is misconfigured.
        if (maxHealth <= 0) return 0f;
        return (float)currentHealth / maxHealth;
    }

    /// <summary>
    /// Returns <c>true</c> if this entity's health has reached zero.
    /// </summary>
    public bool IsDead => isDead;
}
