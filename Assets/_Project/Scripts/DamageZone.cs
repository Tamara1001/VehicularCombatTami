// ==============================================================
// DamageZone.cs
// --------------------------------------------------------------
// PURPOSE:
//   A static hazard module for continuous-damage traps such as
//   spikes, fire pits, or acid pools. Any entity with a Collider
//   and a component implementing IDamageable that enters/stays
//   in this trigger zone will receive periodic damage.
//
// HOW IT WORKS (Decoupling via Interface):
//   This script never references PlayerController, EnemyAI, or
//   any concrete type. It only asks: "Does whatever just entered
//   my trigger implement IDamageable?" If yes, it calls
//   TakeDamage(). It does not care WHAT is being hurt.
//
// SETUP REQUIREMENTS (see Editor Guide for full walkthrough):
//   - This GameObject MUST have a Collider with "Is Trigger" = true.
//   - This GameObject MUST have a Rigidbody (can be kinematic) OR
//     the hitting entity must have a Rigidbody. Unity requires at
//     least one Rigidbody for trigger callbacks to fire.
// ==============================================================

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A continuous-damage hazard zone. Applies periodic damage to any
/// entity that implements <see cref="IDamageable"/> while it remains
/// inside this trigger collider.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DamageZone : MonoBehaviour
{
    // ----------------------------------------------------------
    // INSPECTOR FIELDS
    // All fields are private — [SerializeField] gives Inspector
    // access without exposing public setters to other scripts.
    // ----------------------------------------------------------

    [Header("Damage Settings")]

    [Tooltip("Damage applied to the entity on each damage tick.")]
    [SerializeField] private int damageAmount = 10;

    [Tooltip(
        "Seconds between each damage application. " +
        "Lower values = faster damage ticks. " +
        "E.g. 0.5 = damage every half-second.")]
    [SerializeField] private float damageTickRate = 1f;

    // ----------------------------------------------------------
    // PRIVATE STATE
    // ----------------------------------------------------------

    /// <summary>
    /// Tracks the per-collider cooldown timers so each entity in the
    /// zone is damaged independently on its own tick interval.
    ///
    /// KEY   = the Collider currently overlapping this trigger.
    /// VALUE = the Time.time at which that collider is next eligible
    ///         to receive a damage tick.
    ///
    /// Using a Dictionary here instead of a single float allows
    /// multiple entities to be in the zone simultaneously with
    /// independent, non-interfering cooldown timers.
    /// </summary>
    private readonly Dictionary<Collider, float> nextDamageTimeMap
        = new Dictionary<Collider, float>();

    // ----------------------------------------------------------
    // UNITY LIFECYCLE — TRIGGER EVENTS
    //
    // PERFORMANCE NOTE:
    //   IDamageable is resolved ONCE in OnTriggerEnter via
    //   GetComponentInParent, which walks up the hierarchy so that
    //   child colliders correctly reach a HealthComponent on the
    //   parent root. The Collider itself (not the root) is stored
    //   as the dictionary key so each collider keeps its own
    //   independent cooldown timer.
    // ----------------------------------------------------------

    /// <summary>
    /// Called by Unity when a Collider enters this trigger volume.
    /// Registers the collider in the damage-time map so it is
    /// eligible for ticking in OnTriggerStay.
    /// </summary>
    /// <param name="other">The Collider that entered the trigger.</param>
    private void OnTriggerEnter(Collider other)
    {
        // Walk UP the hierarchy from the entering collider's GameObject
        // so that child colliders can still resolve an IDamageable that
        // lives on a parent (e.g. HealthComponent on the vehicle root).
        // GetComponentInParent also checks the object itself, so this is
        // a strict superset of the old TryGetComponent call.
        if (other.GetComponentInParent<IDamageable>() != null)
        {
            // Register with an initial next-damage time of NOW so
            // the first tick fires immediately on the first Stay frame.
            // Key = the specific Collider to keep independent cooldowns
            // when an entity has multiple colliders in the zone.
            if (!nextDamageTimeMap.ContainsKey(other))
            {
                nextDamageTimeMap[other] = Time.time;
            }
        }
    }

    /// <summary>
    /// Called by Unity every FixedUpdate frame while a Collider remains
    /// inside this trigger volume. Applies damage on the configured
    /// tick interval using a per-collider cooldown stored in the map.
    /// </summary>
    /// <param name="other">The Collider currently overlapping.</param>
    private void OnTriggerStay(Collider other)
    {
        // Early-out: if this collider was never registered (i.e., it
        // doesn't implement IDamageable), do nothing — no GetComponent.
        if (!nextDamageTimeMap.TryGetValue(other, out float nextDamageTime))
            return;

        // Check if the cooldown for this specific entity has elapsed.
        if (Time.time < nextDamageTime) return;

        // Cooldown has elapsed — resolve the interface by walking up the
        // hierarchy so that child-collider entities (player, AI vehicles)
        // whose HealthComponent sits on the parent root are found correctly.
        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);

            // ── TEMPORARY DIAGNOSTIC ──────────────────────────────────────
            // Confirms hierarchy detection is working. Remove once verified.
            Debug.Log($"🔥 [DamageZone] Damage applied to: {other.transform.root.name}");
            // ─────────────────────────────────────────────────────────────

            // Schedule the NEXT tick for this collider.
            nextDamageTimeMap[other] = Time.time + damageTickRate;
        }
    }

    /// <summary>
    /// Called by Unity when a Collider exits this trigger volume.
    /// Cleans up the map entry so we don't accumulate stale references.
    /// </summary>
    /// <param name="other">The Collider that exited the trigger.</param>
    private void OnTriggerExit(Collider other)
    {
        // Always attempt removal — Dictionary.Remove is a no-op if the
        // key doesn't exist, so this is safe for non-damageable colliders.
        nextDamageTimeMap.Remove(other);
    }

    // ----------------------------------------------------------
    // EDITOR HELPERS
    // ----------------------------------------------------------

#if UNITY_EDITOR
    /// <summary>
    /// Draws a yellow wire cube in the Scene view to visualise
    /// the hazard zone extent. Requires a BoxCollider to be readable.
    /// Falls back gracefully if no BoxCollider is present.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.35f); // orange, semi-transparent

        // Try to mirror the BoxCollider's shape for an accurate preview.
        if (TryGetComponent<BoxCollider>(out BoxCollider box))
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);

            Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else
        {
            // Generic sphere fallback for non-box colliders.
            Gizmos.DrawSphere(transform.position, 0.5f);
        }
    }
#endif
}
