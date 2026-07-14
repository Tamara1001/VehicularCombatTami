using UnityEngine;

/// <summary>
/// Concrete <see cref="PowerUpEffect"/> that instantly heals the vehicle
/// when it is first picked up.
///
/// DURATION BEHAVIOUR
///   This is an instant effect — it fires once on pickup and has no lasting
///   state to remove.  <see cref="RemoveEffect"/> is intentionally a no-op.
///   The <see cref="ActiveEffectsHandler"/> will still honour the pickup's
///   duration field (used for HUD display timers), but calling RemoveEffect
///   at the end of that window causes no game-state change.
///
/// HEALTH DEPENDENCY
///   Requires <see cref="HealthComponent.Heal"/> to exist.
///   That method is added to HealthComponent alongside this script.
/// </summary>
[CreateAssetMenu(
    fileName = "New InstantHealEffect",
    menuName = "PowerUps/Instant Heal Effect",
    order = 2)]
public sealed class InstantHealEffect : PowerUpEffect
{
    [Header("Heal Configuration")]
    [Tooltip("Number of health points restored on pickup.")]
    [Min(1)]
    public int healAmount = 25;

    // -------------------------------------------------------------------------
    // PowerUpEffect implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>Calls <see cref="HealthComponent.Heal"/> immediately.</remarks>
    public override void ApplyEffect(IPowerUpTarget target)
    {
        if (target.Health == null)
        {
            Debug.LogWarning($"[PowerUp] {effectName}: target has no HealthComponent — heal skipped.");
            return;
        }

        target.Health.Heal(healAmount);
        Debug.Log($"[PowerUp] APPLY  — {effectName}: healed {healAmount} HP.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Instant effects have nothing to reverse; this method is intentionally empty.
    /// </remarks>
    public override void RemoveEffect(IPowerUpTarget target)
    {
        // No-op: healing is irreversible by design.
    }
}
