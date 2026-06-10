// ==============================================================
// IDamageable.cs
// --------------------------------------------------------------
// PURPOSE:
//   Defines the universal contract for any entity in the game
//   that can receive damage. By depending on this interface
//   instead of concrete classes, damage dealers (projectiles,
//   traps, melee attacks) are completely decoupled from damage
//   receivers (players, enemies, destructible props).
//
// USAGE:
//   Any MonoBehaviour that can be damaged must implement this
//   interface. The HealthComponent script provides the canonical
//   implementation.
//
// ARCHITECTURE NOTE (Dependency Inversion Principle):
//   High-level damage-dealing modules depend on this abstraction,
//   not on concrete types like PlayerController or EnemyAI.
// ==============================================================

/// <summary>
/// Universal contract for any entity that can receive damage.
/// Implement this interface on any MonoBehaviour that should
/// participate in the damage system.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Applies the specified amount of damage to this entity.
    /// Implementations are responsible for their own health
    /// management, clamping, and death logic.
    /// </summary>
    /// <param name="amount">
    /// The raw damage amount to apply. Must be a positive integer.
    /// Negative values (healing) are intentionally excluded from
    /// this contract to keep the interface's responsibility clear.
    /// </param>
    void TakeDamage(int amount);
}
