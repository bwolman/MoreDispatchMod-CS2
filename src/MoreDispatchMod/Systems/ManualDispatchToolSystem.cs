using System.Collections.Generic;

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
using UnityEngine;

namespace MoreDispatchMod.Systems
{
    public partial class ManualDispatchToolSystem : ToolBaseSystem
    {
        public override string toolID => "Manual Dispatch Tool";

        public bool PoliceEnabled { get; set; }
        public bool FireEnabled { get; set; }
        public bool EMSEnabled { get; set; }
        public bool CrimeEnabled { get; set; }
        public bool AreaCrimeEnabled { get; set; }

        private const uint REQUEST_GROUP_EMERGENCY = 4u;
        private const int AREA_CRIME_MAX_DISPATCH = 5;  // conservative; raise after verifying safe concurrent limit

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
        private EntityQuery m_BuildingQuery;
        private EntityQuery m_PoliceCarQuery;
        private EntityQuery m_PoliceStationQuery;
        private EntityQuery m_AreaCrimeTrackerQuery;
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

            m_BuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_PoliceCarQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Vehicles.PoliceCar>(),
                ComponentType.ReadOnly<Car>(),
                ComponentType.ReadOnly<CarCurrentLane>(),
                ComponentType.ReadOnly<PathOwner>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<ServiceDispatch>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_PoliceStationQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.PoliceStation>(),
                ComponentType.ReadOnly<Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());

            m_AreaCrimeTrackerQuery = GetEntityQuery(
                ComponentType.ReadOnly<ManualAreaCrimeDispatched>());

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

            // --- Area Crime overlay ---
            // Draw a radius circle at the raycast hit position when Area Crime mode is on.
            if (AreaCrimeEnabled && raycastHit)
            {
                OverlayRenderSystem.Buffer overlayBuffer = m_OverlayRenderSystem.GetBuffer(out JobHandle overlayDeps);
                overlayDeps.Complete();

                float radius = Mod.Settings.AreaCrimeRadius;
                overlayBuffer.DrawCircle(
                    new UnityEngine.Color(1.0f, 0.3f, 0.0f, 0.9f),      // outline: bright orange
                    new UnityEngine.Color(0.85f, 0.15f, 0.15f, 0.25f),  // fill: semi-transparent red
                    0f,                                                    // dash length (0 = solid)
                    0,                                                     // styleFlags
                    new float2(0f, 1f),                                    // surface normal (Y-up terrain)
                    hit.m_HitPosition,
                    radius * 2f);                                          // DrawCircle takes diameter

                m_OverlayRenderSystem.AddBufferWriter(Dependency);
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

                if (AreaCrimeEnabled)
                {
                    CreateAreaCrimeDispatch(hit.m_HitPosition);
                }
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

        private void CreateAreaCrimeDispatch(float3 center)
        {
            float radius = Mod.Settings.AreaCrimeRadius;
            float radiusSq = radius * radius;
            uint currentFrame = m_SimulationSystem.frameIndex;

            // ---- 1. Collect candidate buildings sorted by distance ----
            var allBuildings = m_BuildingQuery.ToEntityArray(Allocator.Temp);
            var candidates = new List<(Entity building, float distSq)>();
            for (int i = 0; i < allBuildings.Length; i++)
            {
                Entity b = allBuildings[i];
                float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(b).m_Position;
                float d = math.distancesq(new float2(pos.x, pos.z), new float2(center.x, center.z));
                if (d <= radiusSq) candidates.Add((b, d));
            }
            allBuildings.Dispose();
            candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            // ---- 2. Build busy-car / busy-building sets from existing trackers ----
            var trackers = m_AreaCrimeTrackerQuery.ToEntityArray(Allocator.Temp);
            var busyCars = new HashSet<Entity>();
            var busyBuildings = new HashSet<Entity>();
            for (int i = 0; i < trackers.Length; i++)
            {
                var t = EntityManager.GetComponentData<ManualAreaCrimeDispatched>(trackers[i]);
                busyCars.Add(t.m_CarEntity);
                busyBuildings.Add(t.m_TargetBuilding);
            }
            trackers.Dispose();

            // ---- 3. Index available police cars by their owner station ----
            var allCars = m_PoliceCarQuery.ToEntityArray(Allocator.Temp);
            var carsByStation = new Dictionary<Entity, List<Entity>>();
            for (int i = 0; i < allCars.Length; i++)
            {
                Entity car = allCars[i];
                if (busyCars.Contains(car)) continue;

                Game.Vehicles.PoliceCar pc = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(car);
                if ((pc.m_State & (PoliceCarFlags.Returning | PoliceCarFlags.AtTarget
                                 | PoliceCarFlags.ShiftEnded | PoliceCarFlags.Disabled)) != 0)
                    continue;
                if (pc.m_RequestCount > 1) continue;
                if (!EntityManager.HasComponent<Owner>(car)) continue;

                Entity station = EntityManager.GetComponentData<Owner>(car).m_Owner;
                if (!carsByStation.ContainsKey(station))
                    carsByStation[station] = new List<Entity>();
                carsByStation[station].Add(car);
            }
            allCars.Dispose();

            var stationEntities = m_PoliceStationQuery.ToEntityArray(Allocator.Temp);

            // ---- 4. For each candidate building, dispatch from the nearest available station ----
            int dispatched = 0;
            for (int i = 0; i < candidates.Count && dispatched < AREA_CRIME_MAX_DISPATCH; i++)
            {
                Entity building = candidates[i].building;
                if (busyBuildings.Contains(building)) continue;

                float3 buildingPos = EntityManager.GetComponentData<Game.Objects.Transform>(building).m_Position;

                // Find nearest station that has an available car
                Entity bestStation = Entity.Null;
                float bestDist = float.MaxValue;
                for (int s = 0; s < stationEntities.Length; s++)
                {
                    Entity station = stationEntities[s];
                    if (!carsByStation.ContainsKey(station) || carsByStation[station].Count == 0)
                        continue;
                    float3 stPos = EntityManager.GetComponentData<Game.Objects.Transform>(station).m_Position;
                    float d = math.distancesq(new float2(stPos.x, stPos.z),
                                              new float2(buildingPos.x, buildingPos.z));
                    if (d < bestDist) { bestDist = d; bestStation = station; }
                }

                if (bestStation == Entity.Null)
                {
                    Mod.Log.Info($"[ManualDispatch] AreaCrime: no available car for building {building.Index}, skipping");
                    continue;
                }

                Entity car = carsByStation[bestStation][0];
                carsByStation[bestStation].RemoveAt(0); // prevent double-assignment
                busyBuildings.Add(building);

                // ---- 5. Path D2: create PoliceEmergencyRequest + inject into car's ServiceDispatch ----
                // All operations below are SetComponentData / buffer ops — safe on rendered entities.
                Entity request = EntityManager.CreateEntity();
                EntityManager.AddComponentData(request, new ServiceRequest());
                EntityManager.AddComponentData(request, new PoliceEmergencyRequest
                {
                    m_Site = building,
                    m_Target = building,
                    m_Priority = 1f,
                    m_Purpose = PolicePurpose.Emergency
                });

                var dispatchBuf = EntityManager.GetBuffer<ServiceDispatch>(car);
                dispatchBuf.Clear();
                dispatchBuf.Add(new ServiceDispatch { m_Request = request });

                Car carComp = EntityManager.GetComponentData<Car>(car);
                carComp.m_Flags |= CarFlags.Emergency | CarFlags.StayOnRoad
                                 | CarFlags.UsePublicTransportLanes;
                carComp.m_Flags &= ~CarFlags.AnyLaneTarget;
                EntityManager.SetComponentData(car, carComp);

                Game.Vehicles.PoliceCar policeCar = EntityManager.GetComponentData<Game.Vehicles.PoliceCar>(car);
                policeCar.m_State |= PoliceCarFlags.AccidentTarget;
                policeCar.m_State &= ~(PoliceCarFlags.Returning | PoliceCarFlags.AtTarget
                                     | PoliceCarFlags.Cancelled);
                EntityManager.SetComponentData(car, policeCar);

                EntityManager.SetComponentData(car, new Target(building));

                CarCurrentLane currentLane = EntityManager.GetComponentData<CarCurrentLane>(car);
                currentLane.m_LaneFlags &= ~CarLaneFlags.EndOfPath;
                EntityManager.SetComponentData(car, currentLane);

                PathOwner pathOwner = EntityManager.GetComponentData<PathOwner>(car);
                pathOwner.m_State |= PathFlags.Updated;
                EntityManager.SetComponentData(car, pathOwner);
                // Note: do NOT AddComponent<EffectsUpdated> — structural change on a vehicle.
                // PoliceCarAISystem.ResetPath() adds it after path recalculation.

                Entity tracker = EntityManager.CreateEntity();
                EntityManager.AddComponentData(tracker, new ManualAreaCrimeDispatched
                {
                    m_CreationFrame = currentFrame,
                    m_CarEntity = car,
                    m_TargetBuilding = building,
                    m_RequestEntity = request
                });

                Mod.Log.Info($"[ManualDispatch] AreaCrime D2: car={car.Index} (station={bestStation.Index}) " +
                    $"→ building={building.Index} (tracker={tracker.Index} request={request.Index})");
                dispatched++;
            }
            stationEntities.Dispose();

            Mod.Log.Info($"[ManualDispatch] AreaCrime: center=({center.x:F0},{center.z:F0}) " +
                $"radius={radius}m buildings={candidates.Count} dispatched={dispatched} " +
                $"(cap={AREA_CRIME_MAX_DISPATCH})");
        }
    }
}
