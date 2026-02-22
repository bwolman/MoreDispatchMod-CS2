using Unity.Entities;

namespace MoreDispatchMod.Components
{
    /// <summary>
    /// Tracker for fire-to-accident dispatch. Lives on a separate non-rendered entity
    /// (NOT on the accident site entity) to avoid archetype changes on rendered entities
    /// that crash BatchUploadSystem.
    /// </summary>
    public struct FireAccidentTracker : IComponentData
    {
        public Entity m_AccidentEntity;
        public uint m_CreationFrame;
    }

    /// <summary>
    /// Tracker for fire-to-medical dispatch. Lives on a separate non-rendered entity
    /// (NOT on the citizen entity) to avoid archetype changes on rendered entities
    /// that crash BatchUploadSystem.
    /// </summary>
    public struct FireMedicalTracker : IComponentData
    {
        public Entity m_CitizenEntity;
        public Entity m_BuildingEntity;
        public uint m_CreationFrame;
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
