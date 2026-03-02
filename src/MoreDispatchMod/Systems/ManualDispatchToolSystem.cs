using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Events;
using Game.Input;
using Game.Objects;
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
        public bool AccidentEnabled { get; set; }
        public bool AreaCrimeEnabled { get; set; }

        private const uint REQUEST_GROUP_EMERGENCY = 4u;
        private const int CRIME_DISPATCH_CAP = 50;
        private const int MAX_AREA_CRIME_PER_CLICK = 50;

        private ToolOutputBarrier m_Barrier;
        private SimulationSystem m_SimulationSystem;
        private OverlayRenderSystem m_OverlayRenderSystem;
        private EntityQuery m_HighlightedQuery;
        private EntityQuery m_CitizenQuery;
        private EntityQuery m_PoliceDispatchedQuery;
        private EntityQuery m_FireDispatchedQuery;
        private EntityQuery m_EMSDispatchedQuery;
        private EntityQuery m_CrimeDispatchedQuery;
        private EntityQuery m_CrimePrefabQuery;
        private EntityQuery m_AccidentDispatchedQuery;
        private EntityQuery m_TrafficAccidentPrefabQuery;
        private EntityQuery m_AllBuildingsQuery;
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
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();

            m_HighlightedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Highlighted>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadOnly<Citizen>(),
                ComponentType.ReadOnly<CurrentBuilding>(),
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

            m_AccidentDispatchedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualAccidentDispatched>());

            // EventData (not PrefabData) uniquely identifies event-type prefab entities.
            // The prefab entity must carry TrafficAccidentData, FireData, CrimeData,
            // and EventData — downstream systems look up fire/chain-reaction data via PrefabRef.
            m_TrafficAccidentPrefabQuery = GetEntityQuery(
                ComponentType.ReadOnly<TrafficAccidentData>(),
                ComponentType.ReadOnly<EventData>());

            m_AllBuildingsQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            Mod.Log.Info("[ManualDispatchTool] OnCreate complete");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyAction.shouldBeEnabled = true;
            m_PreviousRaycastEntity = Entity.Null;
            Mod.Log.Info($"[ManualDispatchTool] OnStartRunning — police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled} crime={CrimeEnabled} accident={AccidentEnabled} areaCrime={AreaCrimeEnabled}");
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
            bool raycastHit = GetRaycastResult(out Entity hitEntity, out _);

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
                    $"police={PoliceEnabled} fire={FireEnabled} ems={EMSEnabled} crime={CrimeEnabled} accident={AccidentEnabled} areaCrime={AreaCrimeEnabled}");

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

                if (AccidentEnabled && isVehicle)
                {
                    CreateAccidentDispatch(hitEntity);
                }

                if (AreaCrimeEnabled && isBuilding)
                {
                    CreateAreaCrimeDispatch(hitEntity);
                }
            }

            // --- Area Crime radius overlay ---
            // Draw a projected circle on the terrain showing the dispatch radius.
            if (AreaCrimeEnabled && raycastHit && hitEntity != Entity.Null
                && EntityManager.HasComponent<Building>(hitEntity)
                && EntityManager.HasComponent<Transform>(hitEntity))
            {
                OverlayRenderSystem.Buffer overlayBuffer = m_OverlayRenderSystem.GetBuffer(out JobHandle overlayDeps);
                inputDeps = JobHandle.CombineDependencies(inputDeps, overlayDeps);

                float3 center = EntityManager.GetComponentData<Transform>(hitEntity).m_Position;
                float diameter = Mod.Settings.AreaCrimeRadius * 2f;

                overlayBuffer.DrawCircle(
                    new UnityEngine.Color(0.8f, 0.1f, 0.1f, 0.8f),     // outline: dark red
                    new UnityEngine.Color(0.8f, 0.1f, 0.1f, 0.1f),     // fill: very light red
                    1.5f,                                                 // outline width (meters)
                    OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f),                                   // direction (north)
                    center,
                    diameter
                );
            }

            return inputDeps;
        }

        private void CreatePoliceDispatch(Entity entity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            if (!Mod.Settings.AllowMultiplePolicePerBuilding)
            {
                var policeTrackers = m_PoliceDispatchedQuery.ToEntityArray(Allocator.Temp);
                bool alreadyTargeted = false;
                for (int i = 0; i < policeTrackers.Length; i++)
                {
                    ManualPoliceDispatched existing = EntityManager.GetComponentData<ManualPoliceDispatched>(policeTrackers[i]);
                    if (existing.m_TargetEntity == entity) { alreadyTargeted = true; break; }
                }
                policeTrackers.Dispose();
                if (alreadyTargeted)
                {
                    Mod.Log.Info($"[ManualDispatch] Police already dispatched to {entity.Index}, skipping");
                    return;
                }
            }

            if (EntityManager.HasComponent<AccidentSite>(entity))
            {
                Mod.Log.Info($"[ManualDispatch] Building {entity.Index} already has AccidentSite, skipping police");
                return;
            }

            var crimePrefabs = m_CrimePrefabQuery.ToEntityArray(Allocator.Temp);
            if (crimePrefabs.Length == 0)
            {
                crimePrefabs.Dispose();
                Mod.Log.Warn("[ManualDispatch] No crime prefabs found — cannot dispatch police");
                return;
            }
            Entity crimePrefab = crimePrefabs[0];
            crimePrefabs.Dispose();

            // Persistent event entity (non-rendered — same pattern as Crime dispatch)
            Entity eventEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Events.Event>(eventEntity, default);
            EntityManager.AddComponentData(eventEntity, new PrefabRef(crimePrefab));
            EntityManager.AddBuffer<TargetElement>(eventEntity);
            EntityManager.AddComponentData<Created>(eventEntity, default);
            EntityManager.AddComponentData<Updated>(eventEntity, default);

            // AddAccidentSite command entity (non-rendered — safe, processed by AddAccidentSiteSystem)
            Entity addSiteCmd = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Common.Event>(addSiteCmd, default);
            EntityManager.AddComponentData(addSiteCmd, new AddAccidentSite
            {
                m_Event = eventEntity,
                m_Target = entity,
                m_Flags = AccidentSiteFlags.CrimeScene | AccidentSiteFlags.CrimeDetected
            });

            // Non-rendered tracker
            Entity tracker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(tracker, new ManualPoliceDispatched
            {
                m_CreationFrame = currentFrame,
                m_TargetEntity = entity,
                m_EventEntity = eventEntity
            });

            Mod.Log.Info($"[ManualDispatch] Police dispatch to {entity.Index} (tracker={tracker.Index} event={eventEntity.Index})");
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

            // Non-rendered tracker entity — also stores the request entity so cleanup can destroy it
            Entity tracker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(tracker, new ManualFireDispatched
            {
                m_CreationFrame = currentFrame,
                m_TargetEntity = entity,
                m_RequestEntity = request
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
            int activeCount = crimeTrackers.Length;
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

            // Cap concurrent crime dispatches to prevent BatchUploadSystem crash from too many AccidentSites
            if (activeCount >= CRIME_DISPATCH_CAP)
            {
                Mod.Log.Info($"[ManualDispatch] Crime cap reached ({CRIME_DISPATCH_CAP}), skipping building {buildingEntity.Index}");
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

        private void CreateAccidentDispatch(Entity vehicleEntity)
        {
            uint currentFrame = m_SimulationSystem.frameIndex;

            // Guard: vehicle already in an accident
            if (EntityManager.HasComponent<InvolvedInAccident>(vehicleEntity))
            {
                Mod.Log.Info($"[ManualDispatch] Vehicle {vehicleEntity.Index} already in accident, skipping");
                return;
            }

            // Guard: already tracked by our mod
            var trackers = m_AccidentDispatchedQuery.ToEntityArray(Allocator.Temp);
            bool alreadyTracked = false;
            for (int i = 0; i < trackers.Length; i++)
            {
                ManualAccidentDispatched t = EntityManager.GetComponentData<ManualAccidentDispatched>(trackers[i]);
                if (t.m_VehicleEntity == vehicleEntity) { alreadyTracked = true; break; }
            }
            trackers.Dispose();
            if (alreadyTracked)
            {
                Mod.Log.Info($"[ManualDispatch] Accident already tracked for vehicle {vehicleEntity.Index}, skipping");
                return;
            }

            // Find a traffic accident prefab for PrefabRef on the event entity
            Entity accidentPrefab = Entity.Null;
            var prefabs = m_TrafficAccidentPrefabQuery.ToEntityArray(Allocator.Temp);
            if (prefabs.Length > 0) accidentPrefab = prefabs[0];
            else Mod.Log.Warn("[ManualDispatch] No TrafficAccidentData prefab found — event entity will lack PrefabRef");
            prefabs.Dispose();

            // Compute lateral push perpendicular to vehicle's forward direction
            Transform vehicleTransform = EntityManager.GetComponentData<Transform>(vehicleEntity);
            float3 forward = math.mul(vehicleTransform.m_Rotation, new float3(0f, 0f, 1f));
            float3 right = math.normalize(math.cross(forward, new float3(0f, 1f, 0f)));
            float3 velocityDelta = right * 10f + new float3(0f, 1f, 0f);  // hard lateral + slight upward
            float3 angularDelta = new float3(0f, 3f, 0f);                  // spin around Y axis

            // Persistent accident event entity (Game.Events.Event — NOT Common.Event).
            // All 6 components are added synchronously via direct EntityManager calls so
            // the entity is fully formed before any downstream system runs.
            // Note: EntityManager.CreateArchetype() and CreateEntity(EntityArchetype) trigger
            // ReadOnlySpan<> compile errors on netstandard2.1 in this Unity.Entities version,
            // so we use sequential AddComponentData/AddBuffer instead. These are main-thread
            // synchronous calls — the TargetElement buffer exists before AccidentVehicleSystem
            // (64-frame cycle) or AccidentSiteSystem runs. HasBuffer checks will return true.
            Entity eventEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Events.Event>(eventEntity, default);
            EntityManager.AddComponentData<Game.Events.TrafficAccident>(eventEntity, default);
            EntityManager.AddBuffer<TargetElement>(eventEntity);
            EntityManager.AddComponentData<Created>(eventEntity, default);
            EntityManager.AddComponentData<Updated>(eventEntity, default);
            if (accidentPrefab != Entity.Null)
                EntityManager.AddComponentData(eventEntity, new PrefabRef(accidentPrefab));

            // Impact command entity (Game.Common.Event — consumed by ImpactSystem next frame)
            Entity impactCmd = EntityManager.CreateEntity();
            EntityManager.AddComponentData<Game.Common.Event>(impactCmd, default);
            EntityManager.AddComponentData(impactCmd, new Impact
            {
                m_Event = eventEntity,
                m_Target = vehicleEntity,
                m_VelocityDelta = velocityDelta,
                m_AngularVelocityDelta = angularDelta,
                m_Severity = 10f,
                m_CheckStoppedEvent = false
            });

            // Non-rendered tracker entity
            Entity tracker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(tracker, new ManualAccidentDispatched
            {
                m_CreationFrame = currentFrame,
                m_VehicleEntity = vehicleEntity,
                m_EventEntity = eventEntity
            });

            Mod.Log.Info($"[ManualDispatch] Accident triggered on vehicle {vehicleEntity.Index} " +
                $"(tracker={tracker.Index} event={eventEntity.Index} impact={impactCmd.Index})");
        }

        private void CreateAreaCrimeDispatch(Entity clickedBuilding)
        {
            if (!EntityManager.HasComponent<Transform>(clickedBuilding))
            {
                Mod.Log.Warn($"[ManualDispatch] AreaCrime: clicked building {clickedBuilding.Index} has no Transform, skipping");
                return;
            }

            float radius = Mod.Settings.AreaCrimeRadius;
            float radiusSq = radius * radius;
            float3 center = EntityManager.GetComponentData<Transform>(clickedBuilding).m_Position;

            var buildings = m_AllBuildingsQuery.ToEntityArray(Allocator.Temp);
            int dispatched = 0;

            for (int i = 0; i < buildings.Length; i++)
            {
                if (dispatched >= MAX_AREA_CRIME_PER_CLICK)
                    break;

                Entity building = buildings[i];
                float3 pos = EntityManager.GetComponentData<Transform>(building).m_Position;
                float2 delta = pos.xz - center.xz;
                if (math.dot(delta, delta) > radiusSq)
                    continue;

                CreateCrimeDispatch(building);
                dispatched++;
            }

            buildings.Dispose();
            Mod.Log.Info($"[ManualDispatch] AreaCrime: attempted dispatch to {dispatched} buildings within {radius}m of {clickedBuilding.Index}");
        }
    }
}
