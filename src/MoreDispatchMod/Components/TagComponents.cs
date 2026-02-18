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
    /// Tracker for manual police dispatch. Lives on a separate non-rendered entity
    /// (NOT on the building/vehicle) to avoid archetype changes on rendered entities
    /// that crash BatchUploadSystem.
    /// </summary>
    public struct ManualPoliceDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_TargetEntity;
        public Entity m_PoliceCarEntity;
        public Entity m_RequestEntity;
    }

    /// <summary>
    /// Tracker for manual fire dispatch. Lives on a separate non-rendered entity.
    /// </summary>
    public struct ManualFireDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_TargetEntity;
    }

    /// <summary>
    /// Tracker for manual EMS dispatch. Lives on a separate non-rendered entity.
    /// </summary>
    public struct ManualEMSDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_CitizenEntity;
    }

    /// <summary>
    /// Tracker for manual crime dispatch. Lives on a separate non-rendered entity.
    /// </summary>
    public struct ManualCrimeDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_TargetEntity;
        public Entity m_EventEntity;
    }
}
