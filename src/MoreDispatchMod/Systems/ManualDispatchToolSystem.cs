using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Input;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
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
        private EntityQuery m_PoliceDispatchedQuery;
        private EntityQuery m_FireDispatchedQuery;
        private EntityQuery m_EMSDispatchedQuery;
        private EntityQuery m_CrimeDispatchedQuery;
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
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_PoliceCarQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Vehicles.PoliceCar>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.ReadOnly<ServiceDispatch>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            // Tracker entity queries — these match our non-rendered tracker entities
            m_PoliceDispatchedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualPoliceDispatched>());

            m_FireDispatchedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualFireDispatched>());

            m_EMSDispatchedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualEMSDispatched>());

            m_CrimeDispatchedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualCrimeDispatched>());

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
            Mod.Log.Info($"[ManualDispatchTool] OnStartRunning — police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled} crime={CrimeEnabled}");
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
            // Highlighted/BatchesUpdated are vanilla components used by all tool systems.
            EntityCommandBuffer ecb = m_Barrier.CreateCommandBuffer();

            if (!m_HighlightedQuery.IsEmptyIgnoreFilter && hitEntity != m_PreviousRaycastEntity)
            {
                ecb.AddComponent<BatchesUpdated>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                ecb.RemoveComponent<Highlighted>(m_HighlightedQuery, EntityQueryCaptureMode.AtPlayback);
                m_PreviousRaycastEntity = Entity.Null;
            }

            if (raycastHit && hitEntity != Entity.Null && !EntityManager.HasComponent<Highlighted>(hitEntity))
            {
                ecb.AddComponent<Highlighted>(hitEntity);
                ecb.AddComponent<BatchesUpdated>(hitEntity);
                m_PreviousRaycastEntity = hitEntity;
            }

            // --- Dispatch on click ---
            // IMPORTANT: NO structural changes on rendered entities (buildings/vehicles) here.
            // All AddComponent calls on rendered entities are deferred to
            // ManualDispatchCleanupSystem which runs at GameSimulation phase
            // (safe sync point where rendering is not active).
            if (raycastHit && hitEntity != Entity.Null && applyAction.WasReleasedThisFrame())
            {
                bool isBuilding = EntityManager.HasComponent<Building>(hitEntity);
                bool isVehicle = EntityManager.HasComponent<Vehicle>(hitEntity);

                Mod.Log.Info($"[ManualDispatch] Click entity={hitEntity.Index} building={isBuilding} vehicle={isVehicle} " +
                    $"police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled} crime={CrimeEnabled}");

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
            }

            return inputDeps;
        }

        private void CreatePoliceDispatch(Entity entity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Check if this target already has a police dispatch tracker
            var policeTrackers = m_PoliceDispatchedQuery.ToEntityArray(Allocator.Temp);
            bool alreadyTargeted = false;
            for (int i = 0; i < policeTrackers.Length; i++)
            {
                ManualPoliceDispatched existing = EntityManager.GetComponentData<ManualPoliceDispatched>(policeTrackers[i]);
                if (existing.m_TargetEntity == entity)
                {
                    alreadyTargeted = true;
                    break;
                }
            }

            // Also collect already-dispatched car entities to prevent double dispatch
            int dispatchedCount = policeTrackers.Length;
            Entity[] dispatchedCars = new Entity[dispatchedCount];
            for (int d = 0; d < dispatchedCount; d++)
            {
                ManualPoliceDispatched existingTag = EntityManager.GetComponentData<ManualPoliceDispatched>(policeTrackers[d]);
                dispatchedCars[d] = existingTag.m_PoliceCarEntity;
            }
            policeTrackers.Dispose();

            if (alreadyTargeted)
            {
                Mod.Log.Info($"[ManualDispatch] Police already dispatched to {entity.Index}, skipping");
                return;
            }

            Mod.Log.Info($"[ManualDispatch] Police: {dispatchedCount} cars already dispatched by mod");

            // Find the nearest available police car
            float3 targetPos = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
            var policeCars = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);

            Mod.Log.Info($"[ManualDispatch] Police: searching {policeCars.Length} cars for nearest to entity {entity.Index} at {targetPos}");

            Entity bestCar = Entity.Null;
            float bestDistSq = float.MaxValue;
            int skippedFlags = 0;
            int skippedNoRequest = 0;
            int skippedAlreadyDispatched = 0;

            for (int i = 0; i < policeCars.Length; i++)
            {
                Entity carEntity = policeCars[i];

                // Skip cars already dispatched by our mod
                bool isDispatched = false;
                for (int j = 0; j < dispatchedCount; j++)
                {
                    if (carEntity == dispatchedCars[j])
                    {
                        isDispatched = true;
                        break;
                    }
                }
                if (isDispatched)
                {
                    skippedAlreadyDispatched++;
                    continue;
                }

                Game.Vehicles.PoliceCar pc = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(carEntity);

                // Skip unavailable cars
                if ((pc.m_State & (PoliceCarFlags.Returning | PoliceCarFlags.AtTarget
                                 | PoliceCarFlags.ShiftEnded | PoliceCarFlags.Disabled
                                 | PoliceCarFlags.AccidentTarget)) != 0)
                {
                    skippedFlags++;
                    continue;
                }
                if (pc.m_RequestCount < 1)
                {
                    skippedNoRequest++;
                    continue;
                }

                float3 carPos = EntityManager.GetComponentData<Game.Objects.Transform>(carEntity).m_Position;
                float distSq = math.distancesq(carPos, targetPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCar = carEntity;
                }
            }
            policeCars.Dispose();

            Mod.Log.Info($"[ManualDispatch] Police: skippedFlags={skippedFlags} skippedNoRequest={skippedNoRequest} skippedAlreadyDispatched={skippedAlreadyDispatched} bestCar={bestCar.Index} dist={math.sqrt(bestDistSq):F0}");

            if (bestCar == Entity.Null)
            {
                Mod.Log.Info("[ManualDispatch] No available police cars found");
                return;
            }

            // Log pre-modification state
            Game.Vehicles.PoliceCar preState = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(bestCar);
            Car preCarFlags = EntityManager.GetComponentData<Car>(bestCar);
            Target preTarget = EntityManager.GetComponentData<Target>(bestCar);
            PathOwner prePathOwner = EntityManager.GetComponentData<PathOwner>(bestCar);
            DynamicBuffer<ServiceDispatch> preDispatches = EntityManager.GetBuffer<ServiceDispatch>(bestCar);
            Mod.Log.Info($"[ManualDispatch] Police PRE car={bestCar.Index}: pcState=0x{(uint)preState.m_State:X} " +
                $"carFlags=0x{(uint)preCarFlags.m_Flags:X} target={preTarget.m_Target.Index} " +
                $"pathState=0x{(uint)prePathOwner.m_State:X} dispatches={preDispatches.Length} requestCount={preState.m_RequestCount}");

            // Create request entity (non-rendered — safe for direct EntityManager)
            Entity request = EntityManager.CreateEntity();
            EntityManager.AddComponentData(request, new ServiceRequest());
            EntityManager.AddComponentData(request, new PoliceEmergencyRequest(
                entity, entity, 5f, PolicePurpose.Emergency));

            Mod.Log.Info($"[ManualDispatch] Police: created request entity {request.Index}");

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

            // DO NOT add EffectsUpdated to the car — it's a structural change on a
            // rendered entity that crashes BatchUploadSystem. Emergency car flags
            // (set via SetComponentData above) are sufficient for sirens/lights.

            // Create a NON-RENDERED tracker entity for our tag.
            // NEVER add custom components to rendered entities (buildings/vehicles) —
            // it creates new archetypes that crash BatchUploadSystem.
            Entity tracker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(tracker, new ManualPoliceDispatched
            {
                m_CreationFrame = currentFrame,
                m_TargetEntity = entity,
                m_PoliceCarEntity = bestCar,
                m_RequestEntity = request
            });

            // Log post-modification state
            Game.Vehicles.PoliceCar postState = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(bestCar);
            Car postCarFlags = EntityManager.GetComponentData<Car>(bestCar);
            Target postTarget = EntityManager.GetComponentData<Target>(bestCar);
            PathOwner postPathOwner = EntityManager.GetComponentData<PathOwner>(bestCar);
            Mod.Log.Info($"[ManualDispatch] Police POST car={bestCar.Index}: pcState=0x{(uint)postState.m_State:X} " +
                $"carFlags=0x{(uint)postCarFlags.m_Flags:X} target={postTarget.m_Target.Index} " +
                $"pathState=0x{(uint)postPathOwner.m_State:X} requestCount={postState.m_RequestCount}");

            Mod.Log.Info($"[ManualDispatch] Police car {bestCar.Index} dispatched to {entity.Index} (tracker={tracker.Index})");
        }

        private void CreateFireDispatch(Entity entity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Check if this target already has a fire dispatch tracker
            var fireTrackers = m_FireDispatchedQuery.ToEntityArray(Allocator.Temp);
            bool alreadyTargeted = false;
            for (int i = 0; i < fireTrackers.Length; i++)
            {
                ManualFireDispatched existing = EntityManager.GetComponentData<ManualFireDispatched>(fireTrackers[i]);
                if (existing.m_TargetEntity == entity)
                {
                    alreadyTargeted = true;
                    break;
                }
            }
            fireTrackers.Dispose();

            if (alreadyTargeted)
            {
                Mod.Log.Info($"[ManualDispatch] Fire already dispatched to {entity.Index}, skipping");
                return;
            }

            Mod.Log.Info($"[ManualDispatch] Fire: entity={entity.Index}");

            // RescueTarget deferred to ManualDispatchCleanupSystem (GameSimulation phase)
            // to avoid structural changes on rendered entities during tool update.

            // Create fire rescue request (non-rendered — safe for direct EntityManager)
            Entity request = EntityManager.CreateEntity();
            EntityManager.AddComponentData(request, new ServiceRequest());
            EntityManager.AddComponentData(request, new FireRescueRequest(
                entity, 1f, FireRescueRequestType.Disaster));
            EntityManager.AddComponentData(request, new RequestGroup(REQUEST_GROUP_EMERGENCY));

            Mod.Log.Info($"[ManualDispatch] Fire: created request entity {request.Index}");

            // Non-rendered tracker entity
            Entity tracker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(tracker, new ManualFireDispatched
            {
                m_CreationFrame = currentFrame,
                m_TargetEntity = entity
            });

            Mod.Log.Info($"[ManualDispatch] Fire dispatched to {entity.Index} (tracker={tracker.Index})");
        }

        private void CreateEMSDispatch(Entity buildingEntity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Collect citizens already dispatched by our mod
            var emsTrackers = m_EMSDispatchedQuery.ToEntityArray(Allocator.Temp);
            int emsTrackerCount = emsTrackers.Length;
            Entity[] alreadyDispatchedCitizens = new Entity[emsTrackerCount];
            for (int t = 0; t < emsTrackerCount; t++)
            {
                ManualEMSDispatched existing = EntityManager.GetComponentData<ManualEMSDispatched>(emsTrackers[t]);
                alreadyDispatchedCitizens[t] = existing.m_CitizenEntity;
            }
            emsTrackers.Dispose();

            var citizens = m_CitizenQuery.ToEntityArray(Allocator.Temp);
            int dispatched = 0;
            int totalInBuilding = 0;

            Mod.Log.Info($"[ManualDispatch] EMS: searching {citizens.Length} citizens for building {buildingEntity.Index}");

            for (int i = 0; i < citizens.Length; i++)
            {
                Entity citizen = citizens[i];
                CurrentBuilding cb = EntityManager.GetComponentData<CurrentBuilding>(citizen);
                if (cb.m_CurrentBuilding != buildingEntity)
                    continue;

                totalInBuilding++;

                // Skip citizens already dispatched
                bool alreadyDispatched = false;
                for (int j = 0; j < emsTrackerCount; j++)
                {
                    if (citizen == alreadyDispatchedCitizens[j])
                    {
                        alreadyDispatched = true;
                        break;
                    }
                }
                if (alreadyDispatched) continue;

                // Create AddHealthProblem event entity (non-rendered — safe for direct EntityManager)
                Entity cmd = EntityManager.CreateEntity();
                EntityManager.AddComponentData<Game.Common.Event>(cmd, default);
                EntityManager.AddComponentData(cmd, new AddHealthProblem
                {
                    m_Event = Entity.Null,
                    m_Target = citizen,
                    m_Flags = HealthProblemFlags.Sick | HealthProblemFlags.RequireTransport
                });

                // Non-rendered tracker entity
                Entity tracker = EntityManager.CreateEntity();
                EntityManager.AddComponentData(tracker, new ManualEMSDispatched
                {
                    m_CreationFrame = currentFrame,
                    m_CitizenEntity = citizen
                });
                dispatched++;
            }

            citizens.Dispose();
            Mod.Log.Info($"[ManualDispatch] EMS: dispatched to {dispatched} of {totalInBuilding} citizens in building {buildingEntity.Index}");
        }

        private void CreateCrimeDispatch(Entity buildingEntity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Check if this target already has a crime dispatch tracker
            var crimeTrackers = m_CrimeDispatchedQuery.ToEntityArray(Allocator.Temp);
            bool alreadyTargeted = false;
            for (int i = 0; i < crimeTrackers.Length; i++)
            {
                ManualCrimeDispatched existing = EntityManager.GetComponentData<ManualCrimeDispatched>(crimeTrackers[i]);
                if (existing.m_TargetEntity == buildingEntity)
                {
                    alreadyTargeted = true;
                    break;
                }
            }
            crimeTrackers.Dispose();

            if (alreadyTargeted)
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
            Mod.Log.Info($"[ManualDispatch] Crime: found {crimePrefabs.Length} crime prefabs");

            if (crimePrefabs.Length == 0)
            {
                crimePrefabs.Dispose();
                Mod.Log.Warn("[ManualDispatch] No crime prefabs found — cannot create crime scene");
                return;
            }
            Entity crimePrefab = crimePrefabs[0];
            crimePrefabs.Dispose();

            if (EntityManager.HasComponent<PrefabData>(crimePrefab))
            {
                PrefabData pd = EntityManager.GetComponentData<PrefabData>(crimePrefab);
                Mod.Log.Info($"[ManualDispatch] Crime: using prefab entity={crimePrefab.Index} prefabIndex={pd.m_Index}");
            }

            // Create persistent event entity (non-rendered — safe for direct EntityManager).
            // Must have Game.Events.Event (persistent marker, NOT Game.Common.Event which is
            // short-lived and gets destroyed in 1-2 frames), TargetElement buffer (required by
            // both AddAccidentSiteSystem and AccidentSiteSystem), and PrefabRef (for CrimeData
            // lookup: alarm delay, crime duration).
            Entity eventEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Events.Event>(eventEntity, default);
            EntityManager.AddComponentData(eventEntity, new PrefabRef(crimePrefab));
            EntityManager.AddBuffer<TargetElement>(eventEntity);
            EntityManager.AddComponentData<Created>(eventEntity, default);
            EntityManager.AddComponentData<Updated>(eventEntity, default);

            Mod.Log.Info($"[ManualDispatch] Crime: created event entity {eventEntity.Index} with PrefabRef → {crimePrefab.Index}");

            // Create AddAccidentSite command entity (non-rendered — safe for direct EntityManager).
            // Vanilla AddAccidentSiteSystem will safely add AccidentSite to the building
            // through the game's rendering-safe pipeline — avoids BatchUploadSystem crash.
            Entity addSiteCmd = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Common.Event>(addSiteCmd, default);
            EntityManager.AddComponentData(addSiteCmd, new AddAccidentSite
            {
                m_Event = eventEntity,
                m_Target = buildingEntity,
                m_Flags = AccidentSiteFlags.CrimeScene | AccidentSiteFlags.CrimeDetected
            });

            Mod.Log.Info($"[ManualDispatch] Crime: created AddAccidentSite cmd {addSiteCmd.Index} for building {buildingEntity.Index}");

            // Non-rendered tracker entity
            Entity tracker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(tracker, new ManualCrimeDispatched
            {
                m_CreationFrame = currentFrame,
                m_TargetEntity = buildingEntity,
                m_EventEntity = eventEntity
            });

            Mod.Log.Info($"[ManualDispatch] Crime tracker created for building {buildingEntity.Index} (tracker={tracker.Index})");
        }
    }
}
