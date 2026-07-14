/// <summary>
/// Contract that any vehicle wishing to receive power-up effects must fulfil.
///
/// DESIGN NOTES
///   • All three properties are read-only from the interface's perspective —
///     the implementing MonoBehaviour caches the references; effects only
///     read the reference, then call methods on the target component.
///   • Any property may return null if the vehicle does not have that
///     component (e.g., a turret-less drone).  Concrete effects should null-
///     check before operating on optional components.
/// </summary>
public interface IPowerUpTarget
{
    /// <summary>The vehicle's movement controller. Never null on a standard vehicle.</summary>
    ArcadeVehicleController VehicleController { get; }

    /// <summary>The vehicle's primary weapon mount. May be null for weaponless vehicles.</summary>
    VehicleWeapon Weapon { get; }

    /// <summary>The vehicle's health state manager. Never null on a damageable vehicle.</summary>
    HealthComponent Health { get; }
}
