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
    /// Tracks which building received RescueTarget for proper cleanup.
    /// </summary>
    public struct FireDispatchedToMedical : IComponentData
    {
        public Entity m_Building;
    }

    /// <summary>
    /// Marker on entities that received a manual police dispatch.
    /// Tracks creation frame for timeout cleanup and whether we added AccidentSite.
    /// </summary>
    public struct ManualPoliceDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public bool m_AddedAccidentSite;
    }

    /// <summary>
    /// Marker on entities that received a manual fire dispatch.
    /// Tracks creation frame for timeout cleanup.
    /// </summary>
    public struct ManualFireDispatched : IComponentData
    {
        public uint m_CreationFrame;
    }

    /// <summary>
    /// Marker on citizen entities that received a manual EMS dispatch.
    /// Tracks creation frame for timeout cleanup. HealthProblem is managed by
    /// the game's AddHealthProblemSystem — we create event entities, not direct components.
    /// </summary>
    public struct ManualEMSDispatched : IComponentData
    {
        public uint m_CreationFrame;
    }
}
