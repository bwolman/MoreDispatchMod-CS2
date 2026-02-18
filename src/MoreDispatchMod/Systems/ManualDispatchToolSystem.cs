using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Input;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;

using MoreDispatchMod.Components;

using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace MoreDispatchMod.Systems
{
    public partial class ManualDispatchToolSystem : ToolBaseSystem
    {
        public override string toolID => "Manual Dispatch Tool";

        public bool PoliceEnabled { get; set; }
        public bool FireEnabled { get; set; }
        public bool EMSEnabled { get; set; }
        public bool CrimeEnabled { get; set; }

        private const uint REQUEST_GROUP_EMERGENCY = 4u;

        private ToolOutputBarrier m_Barrier;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_HighlightedQuery;
        private EntityQuery m_CitizenQuery;
        private EntityQuery m_PoliceCarQuery;
        private EntityQuery m_CrimePrefabQuery;
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

            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
                ComponentType.Exclude<ManualEMSDispatched>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_PoliceCarQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Vehicles.PoliceCar>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<ServiceDispatch>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_CrimePrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<CrimeData>(),
                ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info("[ManualDispatchTool] OnCreate complete");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyAction.shouldBeEnabled = true;
            m_PreviousRaycastEntity = Entity.Null;
            Mod.Log.Info($"[ManualDispatchTool] OnStartRunning — police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled}");
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            Mod.Log.Info("[ManualDispatchTool] OnStopRunning — clearing highlights");

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
            // BatchesUpdated must be added BEFORE Highlighted is removed,
            // otherwise the query no longer matches and the renderer won't clear the visual.
            if (!m_HighlightedQuery.IsEmptyIgnoreFilter && hitEntity != m_PreviousRaycastEntity)
            {
                buffer.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                buffer.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
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
                    CreatePoliceDispatch(hitEntity);
                }

                if (FireEnabled && (isBuilding || isVehicle))
                {
                    CreateFireDispatch(hitEntity);
                }

                if (EMSEnabled && isBuilding)
                {
                    CreateEMSDispatch(hitEntity);
                }

                if (CrimeEnabled && isBuilding)
                {
                    CreateCrimeDispatch(hitEntity);
                }

                Mod.Log.Info($"[ManualDispatch] Click entity={hitEntity.Index} building={isBuilding} vehicle={isVehicle} police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled} crime={CrimeEnabled}");
            }

            return inputDeps;
        }

        private void CreatePoliceDispatch(Entity entity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Already dispatched to this entity?
            if (EntityManager.HasComponent<ManualPoliceDispatched>(entity))
            {
                Mod.Log.Info($"[ManualDispatch] Police already dispatched to {entity.Index}, skipping");
                return;
            }

            // Find the nearest available police car
            float3 targetPos = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
            var policeCars = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);

            Entity bestCar = Entity.Null;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < policeCars.Length; i++)
            {
                Entity carEntity = policeCars[i];
                Game.Vehicles.PoliceCar pc = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(carEntity);

                // Skip unavailable cars
                if ((pc.m_State & (PoliceCarFlags.Returning | PoliceCarFlags.AtTarget
                                 | PoliceCarFlags.ShiftEnded | PoliceCarFlags.Disabled
                                 | PoliceCarFlags.AccidentTarget)) != 0)
                    continue;
                if (pc.m_RequestCount < 1)
                    continue;

                float3 carPos = EntityManager.GetComponentData<Game.Objects.Transform>(carEntity).m_Position;
                float distSq = math.distancesq(carPos, targetPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCar = carEntity;
                }
            }
            policeCars.Dispose();

            if (bestCar == Entity.Null)
            {
                Mod.Log.Info("[ManualDispatch] No available police cars found");
                return;
            }

            // Create request entity — keeps ResetPath from clearing Emergency flags
            Entity request = EntityManager.CreateEntity();
            EntityManager.AddComponentData(request, new ServiceRequest());
            EntityManager.AddComponentData(request, new PoliceEmergencyRequest(
                entity, entity, 5f, PolicePurpose.Emergency));

            // Inject ServiceDispatch so ResetPath reads our request and maintains Emergency flags
            DynamicBuffer<ServiceDispatch> dispatches = EntityManager.GetBuffer<ServiceDispatch>(bestCar);
            dispatches.Clear();
            dispatches.Add(new ServiceDispatch(request));

            // Set request count to match buffer length
            Game.Vehicles.PoliceCar policeCar = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(bestCar);
            policeCar.m_RequestCount = 1;
            EntityManager.SetComponentData(bestCar, policeCar);

            // Set emergency car flags — sirens and lights
            Car car = EntityManager.GetComponentData<Car>(bestCar);
            car.m_Flags |= CarFlags.Emergency | CarFlags.StayOnRoad | CarFlags.UsePublicTransportLanes;
            EntityManager.SetComponentData(bestCar, car);

            // Set navigation target
            EntityManager.SetComponentData(bestCar, new Target(entity));

            // Trigger new pathfinding to our target
            PathOwner pathOwner = EntityManager.GetComponentData<PathOwner>(bestCar);
            pathOwner.m_State |= PathFlags.Updated;
            EntityManager.SetComponentData(bestCar, pathOwner);

            // Clear EndOfPath to prevent false arrival detection before new path arrives
            if (EntityManager.HasComponent<CarCurrentLane>(bestCar))
            {
                CarCurrentLane currentLane = EntityManager.GetComponentData<CarCurrentLane>(bestCar);
                currentLane.m_LaneFlags &= ~CarLaneFlags.EndOfPath;
                EntityManager.SetComponentData(bestCar, currentLane);
            }

            // Trigger rendering update for lights/sirens
            if (!EntityManager.HasComponent<EffectsUpdated>(bestCar))
            {
                EntityManager.AddComponent<EffectsUpdated>(bestCar);
            }

            // Tag target entity for cleanup tracking
            EntityManager.AddComponentData(entity, new ManualPoliceDispatched
            {
                m_CreationFrame = currentFrame,
                m_PoliceCarEntity = bestCar,
                m_RequestEntity = request
            });

            Mod.Log.Info($"[ManualDispatch] Police car {bestCar.Index} dispatched to {entity.Index}");
        }

        private void CreateFireDispatch(Entity entity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Add RescueTarget if not present
            if (!EntityManager.HasComponent<RescueTarget>(entity))
            {
                EntityManager.AddComponentData(entity, new RescueTarget(Entity.Null));
            }

            // Create fire rescue request
            Entity request = EntityManager.CreateEntity();
            EntityManager.AddComponentData(request, new ServiceRequest());
            EntityManager.AddComponentData(request, new FireRescueRequest(
                entity, 1f, FireRescueRequestType.Disaster));
            EntityManager.AddComponentData(request, new RequestGroup(REQUEST_GROUP_EMERGENCY));

            // Tag for cleanup
            if (!EntityManager.HasComponent<ManualFireDispatched>(entity))
            {
                EntityManager.AddComponentData(entity, new ManualFireDispatched
                {
                    m_CreationFrame = currentFrame
                });
            }

            Mod.Log.Info($"[ManualDispatch] Fire dispatched to {entity.Index}");
        }

        private void CreateEMSDispatch(Entity buildingEntity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;
            var citizens = m_CitizenQuery.ToEntityArray(Allocator.Temp);
            int dispatched = 0;

            for (int i = 0; i < citizens.Length; i++)
            {
                Entity citizen = citizens[i];
                CurrentBuilding cb = EntityManager.GetComponentData<CurrentBuilding>(citizen);
                if (cb.m_CurrentBuilding != buildingEntity)
                    continue;

                // Create AddHealthProblem event entity — AddHealthProblemSystem handles:
                // stopping citizen movement, flag merging, trigger events, journal data
                Entity cmd = EntityManager.CreateEntity();
                EntityManager.AddComponentData<Game.Common.Event>(cmd, default);
                EntityManager.AddComponentData(cmd, new AddHealthProblem
                {
                    m_Event = Entity.Null,
                    m_Target = citizen,
                    m_Flags = HealthProblemFlags.Sick | HealthProblemFlags.RequireTransport
                });

                EntityManager.AddComponentData(citizen, new ManualEMSDispatched
                {
                    m_CreationFrame = currentFrame
                });
                dispatched++;
            }

            citizens.Dispose();
            Mod.Log.Info($"[ManualDispatch] EMS: dispatched to {dispatched} citizens in building {buildingEntity.Index}");
        }

        private void CreateCrimeDispatch(Entity buildingEntity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Already has a crime dispatch or existing AccidentSite?
            if (EntityManager.HasComponent<ManualCrimeDispatched>(buildingEntity))
            {
                Mod.Log.Info($"[ManualDispatch] Crime already dispatched to {buildingEntity.Index}, skipping");
                return;
            }
            if (EntityManager.HasComponent<AccidentSite>(buildingEntity))
            {
                Mod.Log.Info($"[ManualDispatch] Building {buildingEntity.Index} already has AccidentSite, skipping crime");
                return;
            }

            // Find a crime event prefab with CrimeData
            var crimePrefabs = m_CrimePrefabQuery.ToEntityArray(Allocator.Temp);
            if (crimePrefabs.Length == 0)
            {
                crimePrefabs.Dispose();
                Mod.Log.Warn("[ManualDispatch] No crime prefabs found — cannot create crime scene");
                return;
            }
            Entity crimePrefab = crimePrefabs[0];
            crimePrefabs.Dispose();

            // Create event entity with PrefabRef pointing to crime prefab
            Entity eventEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Common.Event>(eventEntity, default);
            EntityManager.AddComponentData(eventEntity, new PrefabRef(crimePrefab));

            // Add AccidentSite to building — CrimeScene + CrimeDetected for immediate police dispatch
            EntityManager.AddComponentData(buildingEntity, new AccidentSite
            {
                m_Event = eventEntity,
                m_PoliceRequest = Entity.Null,
                m_Flags = AccidentSiteFlags.CrimeScene | AccidentSiteFlags.CrimeDetected,
                m_CreationFrame = currentFrame,
                m_SecuredFrame = 0
            });

            // Tag for cleanup tracking
            EntityManager.AddComponentData(buildingEntity, new ManualCrimeDispatched
            {
                m_CreationFrame = currentFrame,
                m_EventEntity = eventEntity
            });

            Mod.Log.Info($"[ManualDispatch] Crime scene created at building {buildingEntity.Index} with prefab {crimePrefab.Index}");
        }
    }
}
