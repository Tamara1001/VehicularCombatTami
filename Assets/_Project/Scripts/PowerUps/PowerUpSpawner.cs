using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Spawns, animates, and recycles a single power-up pickup in the world.
///
/// ── ARCHITECTURE OVERVIEW ──────────────────────────────────────────────────
///
///   • Each spawner owns ONE pickup slot.  When collected, the visual is hidden
///     and a respawn countdown begins.  After <see cref="respawnCooldown"/>
///     seconds a new (possibly different) effect is randomly selected from
///     <see cref="availableEffects"/> and the pickup reactivates.
///
///   • The spawner references the <em>visual model</em> as a child Transform
///     so only the mesh/particles are toggled; the spawner's own Collider stays
///     active for trigger detection at all times (safe — OnTriggerEnter guards
///     the <see cref="_isAvailable"/> flag anyway).
///
///   • Player / enemy detection is intentionally done via
///     <c>GetComponentInParent&lt;ActiveEffectsHandler&gt;()</c>.
///     Vehicle colliders live on child GameObjects; the handler lives on the
///     root.  Searching upward guarantees we find it regardless of hierarchy
///     depth.
///
///   • Audio is routed through <see cref="AudioManager.PlaySFX"/> using a
///     configurable SFX ID string — consistent with the project's audio
///     architecture.  If AudioManager is absent, audio is silently skipped.
///
///   • A <see cref="UnityEvent"/> fires on successful collection so designers
///     can wire up particles, score popups, or any other reaction without
///     touching code.
///
/// ── SCENE SETUP ───────────────────────────────────────────────────────────
///
///   1. Place an empty GameObject in the world.  Add this component.
///   2. Add a child GameObject with a Mesh Renderer + optional Particle System;
///      assign it to <see cref="visualModel"/>.
///   3. Add a Collider (e.g. Sphere Collider) on the root GameObject — check
///      "Is Trigger".  The spawner's own layer should overlap the vehicle layer
///      in the Physics Matrix.
///   4. Populate <see cref="availableEffects"/> with your ScriptableObject
///      effect assets.
/// </summary>
public sealed class PowerUpSpawner : MonoBehaviour
{
    // =========================================================================
    // Inspector — Effect Data
    // =========================================================================

    [Header("Effect Data")]
    [Tooltip("Pool of effects this spawner can grant. One is chosen at random on each (re)spawn.\n" +
             "Must contain at least one entry.")]
    [SerializeField] private List<PowerUpEffect> availableEffects = new List<PowerUpEffect>();

    [Tooltip("How long the applied effect lasts on the vehicle (seconds).\n" +
             "For instant effects (e.g. Heal) this value is used only for HUD display.")]
    [SerializeField] [Min(0.01f)] private float effectDuration = 10f;

    // =========================================================================
    // Inspector — Respawn
    // =========================================================================

    [Header("Respawn")]
    [Tooltip("Seconds to wait before this pickup reactivates after being collected.")]
    [SerializeField] [Min(0f)] private float respawnCooldown = 15f;

    // =========================================================================
    // Inspector — Visual Model
    // =========================================================================

    [Header("Visual")]
    [Tooltip("The child Transform that holds the mesh / particle systems.\n" +
             "This object is enabled/disabled on collect and respawn.\n" +
             "The root's Collider is left untouched so trigger detection always works.")]
    [SerializeField] private Transform visualModel;

    // ── Spin ─────────────────────────────────────────────────────────────────

    [Tooltip("Degrees per second the visual spins around the world Y-axis.")]
    [SerializeField] private float spinSpeed = 90f;

    // ── Bob ──────────────────────────────────────────────────────────────────

    [Tooltip("Maximum vertical offset from the spawn position (metres).\n" +
             "Set to 0 to disable bobbing.")]
    [SerializeField] private float bobAmplitude = 0.25f;

    [Tooltip("Complete up-down cycles per second.")]
    [SerializeField] [Min(0.01f)] private float bobFrequency = 1f;

    // =========================================================================
    // Inspector — Audio & Events
    // =========================================================================

    [Header("Audio")]
    [Tooltip("SFX ID registered in AudioManager to play on pickup.\n" +
             "Leave blank to skip audio.  Example: 'sfx_powerup_collect'.")]
    [SerializeField] private string pickupSfxId = "sfx_powerup_collect";

    [Header("Events")]
    [Tooltip("Fired after a vehicle successfully collects this pickup.\n" +
             "Use this to trigger particle bursts, score popups, etc. without code.")]
    [SerializeField] private UnityEvent onCollected;

    // =========================================================================
    // Private Runtime State
    // =========================================================================

    /// <summary>True when the pickup is visible and can be collected.</summary>
    private bool _isAvailable;

    /// <summary>Seconds elapsed since the last collection. Counts toward <see cref="respawnCooldown"/>.</summary>
    private float _respawnTimer;

    /// <summary>The effect that will be applied when this pickup is collected.</summary>
    private PowerUpEffect _currentEffect;

    /// <summary>
    /// World-space Y position of the spawner at Start.
    /// Used as the centre-line of the bob animation.
    /// </summary>
    private float _originY;

    // =========================================================================
    // Unity Lifecycle
    // =========================================================================

    private void Awake()
    {
        // Validate essential inspector references before the first frame.
        if (visualModel == null)
        {
            Debug.LogError("[PowerUpSpawner] 'Visual Model' is not assigned. " +
                           "Assign the child mesh/particle Transform in the Inspector.", this);
        }

        if (availableEffects == null || availableEffects.Count == 0)
        {
            Debug.LogError("[PowerUpSpawner] 'Available Effects' list is empty. " +
                           "Add at least one PowerUpEffect ScriptableObject asset.", this);
        }
    }

    private void Start()
    {
        _originY = transform.position.y;

        // Activate immediately on scene load.
        Respawn();
    }

    private void Update()
    {
        if (_isAvailable)
        {
            AnimateVisual();
        }
        else
        {
            TickRespawn();
        }
    }

    // =========================================================================
    // Animation
    // =========================================================================

    /// <summary>
    /// Applies a continuous world-Y spin and a sinusoidal bob to <see cref="visualModel"/>.
    /// Called every frame while the pickup is available.
    /// </summary>
    private void AnimateVisual()
    {
        if (visualModel == null) return;

        // ── Spin: rotate around world Y at constant speed ─────────────────────
        visualModel.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        // ── Bob: move the spawner root up/down along a sine wave ─────────────
        // Time.time gives an ever-increasing phase; using the instance's position
        // in the sine via a position offset would desynchronise multiple spawners,
        // so we use a shared time base — the slight phase alignment between nearby
        // pickups is acceptable (and actually looks nice on a grid layout).
        float newY = _originY + Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        Vector3 pos = transform.position;
        pos.y = newY;
        transform.position = pos;
    }

    // =========================================================================
    // Respawn Logic
    // =========================================================================

    /// <summary>
    /// Advances the respawn countdown each frame while the pickup is unavailable.
    /// </summary>
    private void TickRespawn()
    {
        _respawnTimer += Time.deltaTime;

        if (_respawnTimer >= respawnCooldown)
        {
            Respawn();
        }
    }

    /// <summary>
    /// Selects a new random effect, resets state, and makes the pickup visible.
    /// Called both at scene-start and after each collection.
    /// </summary>
    private void Respawn()
    {
        // Guard: nothing to respawn without effects.
        if (availableEffects == null || availableEffects.Count == 0) return;

        // Select a random effect from the available pool.
        _currentEffect = availableEffects[Random.Range(0, availableEffects.Count)];

        // Reset the respawn timer BEFORE setting the flag so TickRespawn
        // cannot fire Respawn() again in the same frame.
        _respawnTimer = 0f;
        _isAvailable  = true;

        // Restore the visual to the spawn-point Y before showing it
        // (the bob may have moved the transform while it was active before).
        Vector3 pos = transform.position;
        pos.y = _originY;
        transform.position = pos;

        // Show the visual model.
        if (visualModel != null)
            visualModel.gameObject.SetActive(true);

        Debug.Log($"[PowerUpSpawner] '{name}' is now active with effect: " +
                  $"'{(_currentEffect != null ? _currentEffect.effectName : "NULL")}'.");
    }

    // =========================================================================
    // Pickup Detection
    // =========================================================================

    /// <summary>
    /// Responds to any collider entering this trigger.
    ///
    /// SEARCH STRATEGY
    ///   <c>GetComponentInParent</c> is used intentionally: a vehicle's physics
    ///   colliders are on child GameObjects, but <see cref="ActiveEffectsHandler"/>
    ///   lives on the root.  Searching upward from the hit child collider
    ///   guarantees the handler is found regardless of hierarchy depth.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // ── Guard: already collected this frame or mid-respawn ────────────────
        if (!_isAvailable) return;

        // ── Guard: no effect assigned (empty list / null entry) ───────────────
        if (_currentEffect == null)
        {
            Debug.LogWarning("[PowerUpSpawner] OnTriggerEnter: _currentEffect is null — pickup ignored.", this);
            return;
        }

        // ── Find the handler on this vehicle ──────────────────────────────────
        // We search up from the hit collider's transform, not the root, because
        // child colliders are common (wheel colliders, weapon colliders, etc.).
        ActiveEffectsHandler handler = other.GetComponentInParent<ActiveEffectsHandler>();

        if (handler == null) return; // Ignore walls, projectiles, non-vehicle objects.

        // ── Apply effect ──────────────────────────────────────────────────────
        handler.AddEffect(_currentEffect, effectDuration);

        // ── Hide the pickup ───────────────────────────────────────────────────
        _isAvailable = false;

        if (visualModel != null)
            visualModel.gameObject.SetActive(false);

        // Snap Y back so the pickup doesn't pop to a mid-bob position on respawn.
        Vector3 pos = transform.position;
        pos.y = _originY;
        transform.position = pos;

        // ── Audio ─────────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(pickupSfxId) && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(pickupSfxId);
        }

        // ── Designer hook ─────────────────────────────────────────────────────
        onCollected?.Invoke();

        Debug.Log($"[PowerUpSpawner] '{name}' collected by '{other.transform.root.name}'. " +
                  $"Effect: '{_currentEffect.effectName}' for {effectDuration:F1}s. " +
                  $"Respawning in {respawnCooldown:F1}s.");
    }

    // =========================================================================
    // Editor Helpers
    // =========================================================================

#if UNITY_EDITOR
    /// <summary>
    /// Draws a wire sphere and the current effect name in the Scene view
    /// to help designers place spawners precisely.
    /// Zero runtime cost — Editor-only compilation.
    /// </summary>
    private void OnDrawGizmos()
    {
        // Colour-code by availability (green = ready, yellow = on cooldown).
        Gizmos.color = _isAvailable ? new Color(0f, 1f, 0.2f, 0.35f)
                                    : new Color(1f, 0.8f, 0f,  0.35f);

        Gizmos.DrawSphere(transform.position, 0.5f);
        Gizmos.color = _isAvailable ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        // Label: effect name and cooldown progress in play mode.
        string label = Application.isPlaying
            ? (_isAvailable
                ? $"▶ {(_currentEffect != null ? _currentEffect.effectName : "??")}"
                : $"⏱ {Mathf.Max(0f, respawnCooldown - _respawnTimer):F1}s")
            : gameObject.name;

        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.7f, label);
    }
#endif
}
