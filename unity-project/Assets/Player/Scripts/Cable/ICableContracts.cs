using UnityEngine;

namespace LastLivingSouls.Cable
{
    /// <summary>Read-only cable status for UI.</summary>
    public interface ICableReadout
    {
        float MaxLength { get; }
        float UsedLength { get; }
        float UsedNormalized { get; }
        string StatusHint { get; }
    }

    /// <summary>Movement leash used by the player controller.</summary>
    public interface ICableLeash
    {
        Vector3 ClampWishVelocity(Vector3 wishVelocity, float deltaTime);
    }
}
