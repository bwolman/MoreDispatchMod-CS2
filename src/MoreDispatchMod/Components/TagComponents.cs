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
        public Entity m_EventEntity;  // crime event entity (same role as ManualCrimeDispatched.m_EventEntity)
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

    /// <summary>
    /// Tracker for direct-dispatch area crime (Path D2). Lives on a separate non-rendered entity.
    /// Tracks the dispatched police car, target building, and request entity for cleanup.
    /// No AccidentSite is added to any building — avoids BatchUploadSystem GPU batch crash.
    /// </summary>
    public struct ManualAreaCrimeDispatched : IComponentData
    {
        public uint m_CreationFrame;
        public Entity m_CarEntity;       // the dispatched police car
        public Entity m_TargetBuilding;  // the building it was sent to
        public Entity m_RequestEntity;   // the PoliceEmergencyRequest entity (needs cleanup)
    }

    /// <summary>
    /// Marker added to a FireRescueRequest entity after we have issued a HandleRequest cancel for it.
    /// Prevents PreventHelicopterBuildingFireSystem from issuing a second cancel on the next frame
    /// before HandleRequestSystem has had a chance to destroy the request entity.
    /// Lives on the (non-rendered) request entity — structural changes on non-rendered entities are safe.
    /// </summary>
    public struct HelicopterCancelled : IComponentData { }
}
