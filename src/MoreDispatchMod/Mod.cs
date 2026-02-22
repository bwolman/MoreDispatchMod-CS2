using System;
using System.Collections.Generic;

using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;

using Game;
using Game.Modding;
using Game.SceneFlow;

using MoreDispatchMod.Settings;
using MoreDispatchMod.Systems;

namespace MoreDispatchMod
{
    public sealed class Mod : IMod
    {
        internal static ILog Log { get; } = LogManager
            .GetLogger(nameof(MoreDispatchMod))
            .SetShowsErrorsInUI(true);

        internal static MoreDispatchModSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            try
            {
                Settings = new MoreDispatchModSettings(this);
                Settings.RegisterInOptionsUI();
                AssetDatabase.global.LoadSettings("MoreDispatchMod", Settings, new MoreDispatchModSettings(this));

                LoadLocalization();

                Log.Info($"[Startup] FireAccident={Settings.DispatchFireToAccidents} FireMedical={Settings.DispatchFireToMedicalCalls}");

                updateSystem.UpdateAt<FireToAccidentDispatchSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<FireToMedicalDispatchSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<PreventHelicopterBuildingFireSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<ManualDispatchCleanupSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<ManualDispatchToolSystem>(SystemUpdatePhase.ToolUpdate);
                updateSystem.UpdateAt<ManualDispatchUISystem>(SystemUpdatePhase.UIUpdate);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed during mod initialization");
            }
        }

        private void LoadLocalization()
        {
            string id = Settings.id;
            string name = Settings.name;

            var sources = new Dictionary<string, string>
            {
                { $"Options.SECTION[{id}]", "More Dispatch Mod" },

                { $"Options.TAB[{id}.General]", "General" },
                { $"Options.GROUP[{id}.Dispatch]", "Dispatch" },

                { $"Options.OPTION[{id}.{name}.DispatchFireToAccidents]", "Fire Engines to Accidents" },
                { $"Options.OPTION_DESCRIPTION[{id}.{name}.DispatchFireToAccidents]", "Additionally dispatch fire engines to all car accidents." },

                { $"Options.OPTION[{id}.{name}.DispatchFireToMedicalCalls]", "Fire Engines to Medical Calls" },
                { $"Options.OPTION_DESCRIPTION[{id}.{name}.DispatchFireToMedicalCalls]", "Additionally dispatch fire engines to all medical calls requiring transport." },

                { $"Options.OPTION[{id}.{name}.PreventHelicopterBuildingFires]", "Prevent Helicopters at Building Fires" },
                { $"Options.OPTION_DESCRIPTION[{id}.{name}.PreventHelicopterBuildingFires]", "Fire helicopters will only respond to forest fires, not building fires." },

                { $"Options.OPTION[{id}.{name}.ResetSettings]", "Reset to Defaults" },
                { $"Options.WARNING[{id}.{name}.ResetSettings]", "Are you sure you want to reset all More Dispatch Mod settings to their default values?" },
            };

            GameManager.instance.localizationManager.AddSource("en-US", new MemorySource(sources));
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
        }
    }
}
