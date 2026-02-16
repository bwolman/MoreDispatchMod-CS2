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

                updateSystem.UpdateAt<ForcePoliceDispatchSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<FireToAccidentDispatchSystem>(SystemUpdatePhase.GameSimulation);
                updateSystem.UpdateAt<FireToMedicalDispatchSystem>(SystemUpdatePhase.GameSimulation);
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

                { $"Options.OPTION[{id}.{name}.AlwaysDispatchPoliceToAccidents]", "Police to All Accidents" },
                { $"Options.OPTION_DESCRIPTION[{id}.{name}.AlwaysDispatchPoliceToAccidents]", "Force police dispatch to ALL car accidents, even low-severity ones that the game would normally skip." },

                { $"Options.OPTION[{id}.{name}.DispatchFireToAccidents]", "Fire Engines to Accidents" },
                { $"Options.OPTION_DESCRIPTION[{id}.{name}.DispatchFireToAccidents]", "Additionally dispatch fire engines to all car accidents." },

                { $"Options.OPTION[{id}.{name}.DispatchFireToMedicalCalls]", "Fire Engines to Medical Calls" },
                { $"Options.OPTION_DESCRIPTION[{id}.{name}.DispatchFireToMedicalCalls]", "Additionally dispatch fire engines to all medical calls requiring transport." },

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
