using UnityEngine;

/// <summary>
/// Abstract base class for all power-up effects in the game.
///
/// DESIGN PATTERN — Strategy + ScriptableObject
///   Each concrete subclass is a self-contained data asset created in the Unity
///   Editor via [CreateAssetMenu].  The <see cref="ActiveEffectsHandler"/> holds
///   a collection of active instances and drives the ApplyEffect / RemoveEffect
///   lifecycle.  Effects carry no runtime state — all mutable state lives on the
///   target's components, keeping ScriptableObject assets safely shareable across
///   prefab instances and scenes.
///
/// LIFECYCLE GUARANTEE
///   • ApplyEffect is called exactly ONCE when the effect becomes active.
///   • RemoveEffect is called exactly ONCE when the timer expires.
///   • Neither method is called from editor code — both are runtime-only.
/// </summary>
public abstract class PowerUpEffect : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Human-readable name shown in the HUD and debug logs.")]
    public string effectName = "Unnamed Effect";

    [Tooltip("Icon displayed on the HUD while this effect is active.")]
    public Sprite effectIcon;

    // -------------------------------------------------------------------------
    // Abstract contract
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies this effect to the given target.
    /// Called once when the effect starts (or is picked up for the first time).
    /// </summary>
    /// <param name="target">
    /// The vehicle that collected the power-up.  Never null — validated by
    /// <see cref="ActiveEffectsHandler"/> before calling.
    /// </param>
    public abstract void ApplyEffect(IPowerUpTarget target);

    /// <summary>
    /// Removes / reverses this effect from the given target.
    /// Called once when the duration timer reaches zero.
    /// </summary>
    /// <param name="target">The same vehicle that received the effect.</param>
    public abstract void RemoveEffect(IPowerUpTarget target);
}
