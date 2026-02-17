# More Dispatch Mod - CS2

## Project Overview
Cities: Skylines 2 mod that adds toggles to dispatch fire engines to car accidents and medical calls, plus a manual dispatch tool for sending Police/Fire/EMS to any building on-click. Pure ECS approach — no Harmony patches.

## Build
```
dotnet build src/MoreDispatchMod/MoreDispatchMod.csproj
```
Requires `Directory.Build.props` with `GamePath` pointing to your CS2 install (copy from `Directory.Build.props.example`).

UI build (required for toolbar button and panel):
```
cd src/MoreDispatchMod/UI && npm install && npm run build
```

## Architecture
- **Mod.cs**: `IMod` entry point — registers settings, localization, and 5 ECS systems
- **Settings/MoreDispatchModSettings.cs**: 2 boolean toggles + reset button in Options UI
- **Systems/FireToAccidentDispatchSystem.cs**: Sends fire engines to traffic accidents with cleanup
- **Systems/FireToMedicalDispatchSystem.cs**: Sends fire engines to medical calls, targeting buildings
- **Systems/ManualDispatchToolSystem.cs**: Custom `ToolBaseSystem` — raycast, highlight, per-click dispatch of Police/Fire/EMS
- **Systems/ManualDispatchUISystem.cs**: `UISystemBase` — ValueBinding/TriggerBinding bridge between React UI and game systems
- **Systems/ManualDispatchCleanupSystem.cs**: Removes temporary `AccidentSite`/`RescueTarget` after timeout
- **Components/TagComponents.cs**: `FireDispatchedToAccident`, `FireDispatchedToMedical`, `ManualPoliceDispatched`, `ManualFireDispatched`, and `ManualEMSDispatched` markers to prevent duplicate requests

## UI
- **UI/src/index.tsx**: ModRegistrar — appends toolbar button to GameTopLeft and panel to Game
- **UI/src/mods/bindings.ts**: ValueBinding/TriggerBinding declarations
- **UI/src/mods/DispatchToolButton.tsx**: Toolbar button component
- **UI/src/mods/DispatchPanel.tsx**: Floating panel with Police/Fire/EMS toggle buttons

## Key Patterns
- All systems extend `GameSystemBase` and run at `SystemUpdatePhase.GameSimulation`
- 64-frame log interval matching vanilla `AccidentSiteSystem`
- Tag components on entities prevent duplicate request creation across ticks
- `RescueTarget` added to entities so `FireRescueDispatchSystem.ValidateTarget()` accepts them
- **Cleanup phase runs every tick** (even when toggle is off) to remove `RescueTarget` and tags when the emergency resolves
- **Building-targeting pattern**: `FireToMedicalDispatchSystem` resolves citizen's building via `CurrentBuilding`, adds `RescueTarget` to the building (not the citizen), and targets the building in `FireRescueRequest`. The `FireDispatchedToMedical` tag stores `m_Building` for cleanup. Multiple citizens in the same building share one `RescueTarget`; it's only removed when the last referencing citizen is cleaned up.

## Design Decisions
- **No police system**: Vanilla already dispatches police to all accidents with severity > 0. The perceived gap is a capacity/travel time issue, not a dispatch request issue.
- **Never add building-domain components to citizen entities**: `RescueTarget` is a `Game.Buildings` component — adding it to citizens corrupts their archetypes and breaks `HealthcareDispatchSystem`.

## References
- Research: `/Users/BWolman/git/skywolf.net/cs_modding_claude/research/topics/EmergencyDispatch/`
- Templates: `/Users/BWolman/git/skywolf.net/cs_modding_claude/templates/`
