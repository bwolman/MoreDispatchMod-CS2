using Game;
using Game.Buildings;
using Game.Common;
using Game.Events;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;

using MoreDispatchMod.Components;

using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace MoreDispatchMod.Systems
{
    public partial class ManualDispatchToolSystem : ToolBaseSystem
    {
        public override string toolID => "Manual Dispatch Tool";

        public bool PoliceEnabled { get; set; }
        public bool FireEnabled { get; set; }
        public bool EMSEnabled { get; set; }

        private ToolOutputBarrier m_Barrier;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_HighlightedQuery;
        private Entity m_PreviousRaycastEntity;

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            Enabled = false;

            m_Barrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_HighlightedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Highlighted>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyAction.shouldBeEnabled = true;
            m_PreviousRaycastEntity = Entity.Null;
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();

            // Remove all highlighting we added
            if (!m_HighlightedQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.AddComponent<BatchesUpdated>(m_HighlightedQuery);
                EntityManager.RemoveComponent<Highlighted>(m_HighlightedQuery);
            }

            m_PreviousRaycastEntity = Entity.Null;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.MovingObjects;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            bool raycastHit = GetRaycastResult(out Entity hitEntity, out RaycastHit hit);

            // --- Highlight management ---
            EntityCommandBuffer buffer = m_Barrier.CreateCommandBuffer();

            // Remove old highlight if entity changed
            if (!m_HighlightedQuery.IsEmptyIgnoreFilter && hitEntity != m_PreviousRaycastEntity)
            {
                buffer.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                buffer.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                m_PreviousRaycastEntity = Entity.Null;
            }

            // Add highlight to new entity
            if (raycastHit && hitEntity != Entity.Null && !EntityManager.HasComponent<Highlighted>(hitEntity))
            {
                buffer.AddComponent<Highlighted>(hitEntity);
                buffer.AddComponent<BatchesUpdated>(hitEntity);
                m_PreviousRaycastEntity = hitEntity;
            }

            // --- Dispatch on click ---
            if (raycastHit && hitEntity != Entity.Null && applyAction.WasReleasedThisFrame())
            {
                bool isBuilding = EntityManager.HasComponent<Building>(hitEntity);
                bool isVehicle = EntityManager.HasComponent<Vehicle>(hitEntity);

                if (PoliceEnabled && (isBuilding || isVehicle))
                {
                    CreatePoliceDispatch(hitEntity, buffer);
                }

                if (FireEnabled && (isBuilding || isVehicle))
                {
                    CreateFireDispatch(hitEntity, buffer);
                }

                if (EMSEnabled && isBuilding)
                {
                    CreateEMSDispatch(hitEntity, buffer);
                }

                Mod.Log.Info($"[ManualDispatch] Click entity={hitEntity.Index} building={isBuilding} vehicle={isVehicle} police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled}");
            }

            return inputDeps;
        }

        private void CreatePoliceDispatch(Entity entity, EntityCommandBuffer buffer)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Add AccidentSite if not present
            bool addedSite = false;
            if (!EntityManager.HasComponent<AccidentSite>(entity))
            {
                buffer.AddComponent(entity, new AccidentSite
                {
                    m_Event = Entity.Null,
                    m_PoliceRequest = Entity.Null,
                    m_Flags = AccidentSiteFlags.RequirePolice | AccidentSiteFlags.TrafficAccident,
                    m_CreationFrame = currentFrame,
                    m_SecuredFrame = 0u
                });
                addedSite = true;
            }
            else
            {
                // Set RequirePolice on existing AccidentSite, but skip if request already pending
                AccidentSite site = EntityManager.GetComponentData<AccidentSite>(entity);
                if (site.m_PoliceRequest != Entity.Null && EntityManager.Exists(site.m_PoliceRequest))
                {
                    Mod.Log.Info($"[ManualDispatch] Police request already pending for {entity.Index}, skipping");
                    return;
                }
                site.m_Flags |= AccidentSiteFlags.RequirePolice;
                EntityManager.SetComponentData(entity, site);
            }

            // Create police emergency request
            Entity request = buffer.CreateEntity();
            buffer.AddComponent(request, new ServiceRequest());
            buffer.AddComponent(request, new PoliceEmergencyRequest(
                entity, entity, 5f, PolicePurpose.Emergency));
            buffer.AddComponent(request, new RequestGroup(4u));

            // Tag for cleanup
            if (!EntityManager.HasComponent<ManualPoliceDispatched>(entity))
            {
                buffer.AddComponent(entity, new ManualPoliceDispatched
                {
                    m_CreationFrame = currentFrame,
                    m_AddedAccidentSite = addedSite
                });
            }

            Mod.Log.Info($"[ManualDispatch] Police dispatched to {entity.Index} (addedSite={addedSite})");
        }

        private void CreateFireDispatch(Entity entity, EntityCommandBuffer buffer)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Add RescueTarget if not present
            if (!EntityManager.HasComponent<RescueTarget>(entity))
            {
                buffer.AddComponent(entity, new RescueTarget(Entity.Null));
            }

            // Create fire rescue request
            Entity request = buffer.CreateEntity();
            buffer.AddComponent(request, new ServiceRequest());
            buffer.AddComponent(request, new FireRescueRequest(
                entity, 1f, FireRescueRequestType.Disaster));
            buffer.AddComponent(request, new RequestGroup(4u));

            // Tag for cleanup
            if (!EntityManager.HasComponent<ManualFireDispatched>(entity))
            {
                buffer.AddComponent(entity, new ManualFireDispatched
                {
                    m_CreationFrame = currentFrame
                });
            }

            Mod.Log.Info($"[ManualDispatch] Fire dispatched to {entity.Index}");
        }

        private void CreateEMSDispatch(Entity entity, EntityCommandBuffer buffer)
        {
            if (EntityManager.HasComponent<ManualEMSDispatched>(entity))
            {
                Mod.Log.Info($"[ManualDispatch] EMS already dispatched to building {entity.Index}, skipping");
                return;
            }

            Entity request = buffer.CreateEntity();
            buffer.AddComponent(request, new ServiceRequest());
            buffer.AddComponent(request, new HealthcareRequest(entity, HealthcareRequestType.Ambulance));
            buffer.AddComponent(request, new RequestGroup(16u));

            buffer.AddComponent(entity, new ManualEMSDispatched { m_CreationFrame = m_SimulationSystem.frameIndex });

            Mod.Log.Info($"[ManualDispatch] EMS dispatched to building {entity.Index}");
        }
    }
}
