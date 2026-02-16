using Unity.Entities;

namespace MoreDispatchMod.Components
{
    /// <summary>
    /// Marker component added to AccidentSite entities that already have a
    /// fire rescue request created by this mod, preventing duplicate requests.
    /// </summary>
    public struct FireDispatchedToAccident : IComponentData
    {
    }

    /// <summary>
    /// Marker component added to citizen entities with HealthProblem that already
    /// have a fire rescue request created by this mod, preventing duplicate requests.
    /// </summary>
    public struct FireDispatchedToMedical : IComponentData
    {
    }
}
