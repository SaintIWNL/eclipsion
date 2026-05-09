using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Rat.LifeInsurance;

[Serializable, NetSerializable]
public sealed class LifeInsuranceConsoleState : BoundUserInterfaceState
{
    public readonly int StoredProteins;
    public readonly int RequiredProteins;
    public readonly List<LifeInsuranceTargetEntry> Targets;

    public LifeInsuranceConsoleState(int storedProteins, int requiredProteins, List<LifeInsuranceTargetEntry> targets)
    {
        StoredProteins = storedProteins;
        RequiredProteins = requiredProteins;
        Targets = targets;
    }
}

[Serializable, NetSerializable]
public sealed class LifeInsuranceTargetEntry
{
    public readonly NetEntity Target;
    public readonly string Name;
    public readonly string RoleName;
    public readonly bool AlreadyInsured;
    public readonly bool HasPendingRespawn;
    /// <summary>Credits charged from the operator when purchasing a policy for this mind (scales with RespawnCount).</summary>
    public readonly int NextCreditsPrice;

    public LifeInsuranceTargetEntry(NetEntity target, string name, string roleName, bool alreadyInsured, bool hasPendingRespawn, int nextCreditsPrice)
    {
        Target = target;
        Name = name;
        RoleName = roleName;
        AlreadyInsured = alreadyInsured;
        HasPendingRespawn = hasPendingRespawn;
        NextCreditsPrice = nextCreditsPrice;
    }
}

[Serializable, NetSerializable]
public sealed class LifeInsuranceSelectTargetMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;

    public LifeInsuranceSelectTargetMessage(NetEntity target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class LifeInsuranceEjectProteinsMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class LifeInsuranceVoidInsuranceMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity Target;

    public LifeInsuranceVoidInsuranceMessage(NetEntity target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public enum LifeInsuranceUiKey : byte
{
    Key
}
