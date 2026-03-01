using Colossal.IO.AssetDatabase;

using Game.Modding;
using Game.Settings;

namespace MoreDispatchMod.Settings
{
    [FileLocation("MoreDispatchMod")]
    [SettingsUITabOrder("General")]
    [SettingsUIGroupOrder("Dispatch")]
    [SettingsUIShowGroupName]
    public class MoreDispatchModSettings : ModSetting
    {
        public MoreDispatchModSettings(IMod mod) : base(mod) { }

        [SettingsUISection("General", "Dispatch")]
        public bool DispatchFireToAccidents { get; set; }

        [SettingsUISection("General", "Dispatch")]
        public bool DispatchFireToMedicalCalls { get; set; }

        [SettingsUISection("General", "Dispatch")]
        public bool PreventHelicopterBuildingFires { get; set; }

        [SettingsUISection("General", "Dispatch")]
        public bool AllowMultiplePolicePerBuilding { get; set; }

        [SettingsUISection("General", "Dispatch")]
        [SettingsUISlider(min = 50, max = 500, step = 25)]
        public int AreaCrimeRadius { get; set; }

        [SettingsUISection("General", "Dispatch")]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool ResetSettings
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            DispatchFireToAccidents = false;
            DispatchFireToMedicalCalls = false;
            PreventHelicopterBuildingFires = false;
            AllowMultiplePolicePerBuilding = false;
            AreaCrimeRadius = 150;
        }
    }
}
