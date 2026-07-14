using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on the vehicle root.  Implements <see cref="IPowerUpTarget"/> so that
/// <see cref="PowerUpEffect"/> assets can query the vehicle's sub-components,
/// and manages the full lifecycle (apply → tick → remove) of every active effect.
///
/// ARCHITECTURE
///   • Caches <see cref="ArcadeVehicleController"/>, <see cref="VehicleWeapon"/>,
///     and <see cref="HealthComponent"/> once in Awake — zero GetComponent calls
///     at runtime.
///   • Maintains a <c>Dictionary&lt;PowerUpEffect, float&gt;</c> mapping each
///     active effect asset to its remaining duration in seconds.
///   • Uses a separate "expired" list built during Update to avoid mutating the
///     dictionary while iterating it (safe, zero-alloc after warm-up).
///
/// STACKING MODEL
///   Picking up the same power-up while it is already active adds to the timer
///   rather than re-applying the stat change.  This avoids runaway multiplier
///   growth and keeps the reset-to-1 removal strategy safe.
///
/// PLACEMENT
///   Attach this MonoBehaviour to the vehicle's root GameObject, alongside
///   <see cref="ArcadeVehicleController"/> and <see cref="HealthComponent"/>.
/// </summary>
[RequireComponent(typeof(ArcadeVehicleController), typeof(HealthComponent))]
public sealed class ActiveEffectsHandler : MonoBehaviour, IPowerUpTarget
{
    // -------------------------------------------------------------------------
    // IPowerUpTarget — cached references (set in Awake, read-only thereafter)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public ArcadeVehicleController VehicleController { get; private set; }

    /// <inheritdoc/>
    public VehicleWeapon Weapon { get; private set; }

    /// <inheritdoc/>
    public HealthComponent Health { get; private set; }

    // -------------------------------------------------------------------------
    // Active-effect state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps each currently-active effect asset to its remaining duration (seconds).
    /// </summary>
    private readonly Dictionary<PowerUpEffect, float> _activeEffects
        = new Dictionary<PowerUpEffect, float>();

    /// <summary>
    /// Reused list of effects whose timers expired this frame.
    /// Populated during the read pass; consumed in the remove pass.
    /// Allocated once — no per-frame heap allocations.
    /// </summary>
    private readonly List<PowerUpEffect> _expiredEffects = new List<PowerUpEffect>();

    /// <summary>
    /// Reused staging buffer for (effect, newTimer) pairs produced in the
    /// read pass and applied in the write-back pass.
    /// Keeps all dictionary mutations strictly outside the primary foreach.
    /// </summary>
    private readonly List<(PowerUpEffect effect, float newTimer)> _timerUpdates
        = new List<(PowerUpEffect, float)>();

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        VehicleController = GetComponent<ArcadeVehicleController>();
        Health            = GetComponent<HealthComponent>();

        // VehicleWeapon may live on a child turret — search the hierarchy.
        Weapon = GetComponentInChildren<VehicleWeapon>();

        if (VehicleController == null)
            Debug.LogError("[ActiveEffectsHandler] ArcadeVehicleController not found on this GameObject.", this);

        if (Health == null)
            Debug.LogError("[ActiveEffectsHandler] HealthComponent not found on this GameObject.", this);

        if (Weapon == null)
            Debug.LogWarning("[ActiveEffectsHandler] No VehicleWeapon found in children — weapon effects will be skipped.", this);
    }

    private void Update()
    {
        if (_activeEffects.Count == 0) return;

        // ── PASS 1 — Read only (no dictionary mutations) ──────────────────────
        // ROOT CAUSE OF THE BUG: writing _activeEffects[key] inside a foreach
        // over _activeEffects.Keys bumps the dictionary's internal version
        // counter.  The enumerator detects the version change and throws
        // InvalidOperationException even though no key was added or removed.
        //
        // FIX: iterate using KeyValuePair so the value is read from the
        // enumerator's snapshot (kvp.Value).  Stage every mutation in
        // _timerUpdates and _expiredEffects; apply them AFTER the loop ends
        // and the enumerator has been disposed by the compiler-generated
        // try/finally block.
        foreach (KeyValuePair<PowerUpEffect, float> kvp in _activeEffects)
        {
            float newTimer = kvp.Value - Time.deltaTime;

            // Stage the write-back — do NOT touch _activeEffects here.
            _timerUpdates.Add((kvp.Key, newTimer));

            if (newTimer <= 0f)
            {
                _expiredEffects.Add(kvp.Key);
            }
        }

        // ── PASS 2 — Write-back updated timers (enumerator is now disposed) ───
        foreach ((PowerUpEffect effect, float newTimer) in _timerUpdates)
        {
            _activeEffects[effect] = newTimer;
        }
        _timerUpdates.Clear();

        // ── PASS 3 — Remove expired effects and call their cleanup logic ───────
        foreach (PowerUpEffect expired in _expiredEffects)
        {
            expired.RemoveEffect(this);
            _activeEffects.Remove(expired);
        }
        _expiredEffects.Clear();
    }

    // -------------------------------------------------------------------------
    // Public API — called by the pickup trigger (Phase 4)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Activates a power-up effect on this vehicle.
    ///
    /// STACKING RULE
    ///   If the effect is already active, its timer is extended by
    ///   <paramref name="duration"/> — the stat multiplier is NOT re-applied.
    ///   If the effect is new, <see cref="PowerUpEffect.ApplyEffect"/> is called
    ///   and the timer starts.
    /// </summary>
    /// <param name="effect">
    ///   The ScriptableObject effect asset to activate. Must not be null.
    /// </param>
    /// <param name="duration">
    ///   How long the effect should last in seconds.
    ///   For instant effects (e.g., <see cref="InstantHealEffect"/>) this value
    ///   controls only the HUD display window; pass a small value such as 0.01f.
    /// </param>
    public void AddEffect(PowerUpEffect effect, float duration)
    {
        if (effect == null)
        {
            Debug.LogError("[ActiveEffectsHandler] AddEffect called with a null effect — ignored.", this);
            return;
        }

        if (duration <= 0f)
        {
            Debug.LogWarning($"[ActiveEffectsHandler] AddEffect: duration for '{effect.effectName}' " +
                             $"is {duration:F3}s — effect will expire immediately.", this);
        }

        if (_activeEffects.ContainsKey(effect))
        {
            // Stack time only — do NOT re-apply the multiplier.
            _activeEffects[effect] += duration;
            Debug.Log($"[PowerUp] STACK  — {effect.effectName}: +{duration:F1}s " +
                      $"(new total: {_activeEffects[effect]:F1}s)");
        }
        else
        {
            // New effect: apply it and start the countdown.
            effect.ApplyEffect(this);
            _activeEffects[effect] = duration;
        }
    }

    // -------------------------------------------------------------------------
    // Debug helpers (editor-visible in play mode)
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    /// <summary>
    /// Draws active-effect timers in the Scene view for quick debugging.
    /// Only compiled in the Editor — zero overhead in builds.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (_activeEffects.Count == 0) return;

        int i = 0;
        foreach (var kvp in _activeEffects)
        {
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * (1.5f + i * 0.4f),
                $"{kvp.Key.effectName}: {kvp.Value:F1}s");
            i++;
        }
    }
#endif
}
