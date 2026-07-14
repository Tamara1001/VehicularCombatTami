using UnityEngine;

/// <summary>
/// Concrete <see cref="PowerUpEffect"/> that temporarily multiplies one of
/// the vehicle's core stats by a configurable value.
///
/// SUPPORTED STATS (see <see cref="StatType"/>):
///   Speed        → ArcadeVehicleController.SpeedMultiplier
///   Acceleration → ArcadeVehicleController.AccelerationMultiplier
///   TurnSpeed    → ArcadeVehicleController.TurnSpeedMultiplier
///   Damage       → VehicleWeapon.DamageMultiplier
///   FireRate     → VehicleWeapon.FireRateMultiplier
///
/// STACKING MODEL (prototype)
///   RemoveEffect resets the multiplier to 1f rather than dividing.
///   This is the safest approach for a prototype:
///   • No floating-point drift from repeated multiply/divide cycles.
///   • Avoids "half a power-up" states if multiple effects of the same
///     stat are applied simultaneously.
///   • Limitation: two concurrent Speed boosts would both reset on the
///     earlier expiry.  For full stacking support, upgrade to an additive
///     bonus model in a future phase.
/// </summary>
[CreateAssetMenu(
    fileName = "New StatMultiplierEffect",
    menuName = "PowerUps/Stat Multiplier Effect",
    order = 1)]
public sealed class StatMultiplierEffect : PowerUpEffect
{
    // -------------------------------------------------------------------------
    // Stat selection
    // -------------------------------------------------------------------------

    /// <summary>Which vehicle stat this effect modifies.</summary>
    public enum StatType
    {
        Speed,
        Acceleration,
        TurnSpeed,
        Damage,
        FireRate
    }

    [Header("Stat Configuration")]
    [Tooltip("Which stat this asset modifies.")]
    public StatType statType = StatType.Speed;

    [Tooltip("Multiplier applied to the stat while the effect is active.\n" +
             "Values > 1 boost the stat; values < 1 penalise it.\n" +
             "Example: 1.5 = 50% faster. 0.75 = 25% slower.")]
    [Min(0.1f)]
    public float multiplierValue = 1.5f;

    // -------------------------------------------------------------------------
    // PowerUpEffect implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void ApplyEffect(IPowerUpTarget target)
    {
        switch (statType)
        {
            case StatType.Speed:
                if (target.VehicleController != null)
                    target.VehicleController.SpeedMultiplier *= multiplierValue;
                break;

            case StatType.Acceleration:
                if (target.VehicleController != null)
                    target.VehicleController.AccelerationMultiplier *= multiplierValue;
                break;

            case StatType.TurnSpeed:
                if (target.VehicleController != null)
                    target.VehicleController.TurnSpeedMultiplier *= multiplierValue;
                break;

            case StatType.Damage:
                if (target.Weapon != null)
                    target.Weapon.DamageMultiplier *= multiplierValue;
                break;

            case StatType.FireRate:
                if (target.Weapon != null)
                    target.Weapon.FireRateMultiplier *= multiplierValue;
                break;

            default:
                Debug.LogWarning($"[{name}] StatMultiplierEffect: unhandled StatType '{statType}'.");
                break;
        }

        Debug.Log($"[PowerUp] APPLY  — {effectName}: {statType} ×{multiplierValue}");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Resets the stat multiplier to 1f (no modifier).
    /// See class summary for the reasoning behind reset-to-1 vs divide-out.
    /// </remarks>
    public override void RemoveEffect(IPowerUpTarget target)
    {
        switch (statType)
        {
            case StatType.Speed:
                if (target.VehicleController != null)
                    target.VehicleController.SpeedMultiplier = 1f;
                break;

            case StatType.Acceleration:
                if (target.VehicleController != null)
                    target.VehicleController.AccelerationMultiplier = 1f;
                break;

            case StatType.TurnSpeed:
                if (target.VehicleController != null)
                    target.VehicleController.TurnSpeedMultiplier = 1f;
                break;

            case StatType.Damage:
                if (target.Weapon != null)
                    target.Weapon.DamageMultiplier = 1f;
                break;

            case StatType.FireRate:
                if (target.Weapon != null)
                    target.Weapon.FireRateMultiplier = 1f;
                break;

            default:
                Debug.LogWarning($"[{name}] StatMultiplierEffect: unhandled StatType '{statType}'.");
                break;
        }

        Debug.Log($"[PowerUp] REMOVE — {effectName}: {statType} reset to 1.0");
    }
}
