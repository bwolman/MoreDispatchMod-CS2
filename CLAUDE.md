# More Dispatch Mod - CS2

## Project Overview
Cities: Skylines 2 mod that adds toggles to dispatch additional emergency vehicles to car accidents and medical calls. Pure ECS approach — no Harmony patches.

## Build
```
dotnet build src/MoreDispatchMod/MoreDispatchMod.csproj
```
Requires `Directory.Build.props` with `GamePath` pointing to your CS2 install (copy from `Directory.Build.props.example`).

## Architecture
- **Mod.cs**: `IMod` entry point — registers settings, localization, and 3 ECS systems
- **Settings/MoreDispatchModSettings.cs**: 3 boolean toggles + reset button in Options UI
- **Systems/ForcePoliceDispatchSystem.cs**: Forces police to all traffic accidents
- **Systems/FireToAccidentDispatchSystem.cs**: Sends fire engines to traffic accidents
- **Systems/FireToMedicalDispatchSystem.cs**: Sends fire engines to medical calls
- **Components/TagComponents.cs**: `FireDispatchedToAccident` and `FireDispatchedToMedical` markers to prevent duplicate requests

## Key Patterns
- All systems extend `GameSystemBase` and run at `SystemUpdatePhase.GameSimulation`
- 64-frame update interval matching vanilla `AccidentSiteSystem`
- Tag components on entities prevent duplicate request creation across ticks
- `RescueTarget` added to entities so `FireRescueDispatchSystem.ValidateTarget()` accepts them

## References
- Research: `/Users/BWolman/git/skywolf.net/cs_modding_claude/research/topics/EmergencyDispatch/`
- Templates: `/Users/BWolman/git/skywolf.net/cs_modding_claude/templates/`
