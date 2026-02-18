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
    /// Tracks creation frame for timeout cleanup, the police car entity
    /// that received the ServiceDispatch, and the request entity for cleanup.
    /// </summary>
    public struct ManualPoliceDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_PoliceCarEntity;
        public Entity m_RequestEntity;
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

    /// <summary>
    /// Marker on building entities that received a manual crime dispatch.
    /// AccidentSiteSystem manages the AccidentSite lifecycle — we track the event entity
    /// for cleanup when vanilla removes the AccidentSite.
    /// </summary>
    public struct ManualCrimeDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_EventEntity;
    }
}
