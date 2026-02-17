using Colossal.UI.Binding;

using Game;
using Game.Tools;
using Game.UI;

using Unity.Entities;

namespace MoreDispatchMod.Systems
{
    public partial class ManualDispatchUISystem : UISystemBase
    {
        private const string kGroup = "MoreDispatchMod";

        public override GameMode gameMode => GameMode.Game;

        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultToolSystem;
        private ManualDispatchToolSystem m_DispatchTool;

        private ValueBinding<bool> m_IsToolActive;
        private ValueBinding<bool> m_PoliceEnabled;
        private ValueBinding<bool> m_FireEnabled;
        private ValueBinding<bool> m_EMSEnabled;
        private ValueBinding<bool> m_PanelVisible;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_DispatchTool = World.GetOrCreateSystemManaged<ManualDispatchToolSystem>();

            // Value bindings (C# → JS)
            AddBinding(m_IsToolActive = new ValueBinding<bool>(kGroup, "IsToolActive", false));
            AddBinding(m_PoliceEnabled = new ValueBinding<bool>(kGroup, "PoliceEnabled", false));
            AddBinding(m_FireEnabled = new ValueBinding<bool>(kGroup, "FireEnabled", false));
            AddBinding(m_EMSEnabled = new ValueBinding<bool>(kGroup, "EMSEnabled", false));
            AddBinding(m_PanelVisible = new ValueBinding<bool>(kGroup, "PanelVisible", false));

            // Trigger bindings (JS → C#)
            AddBinding(new TriggerBinding(kGroup, "ToggleTool", HandleToggleTool));
            AddBinding(new TriggerBinding(kGroup, "TogglePolice", HandleTogglePolice));
            AddBinding(new TriggerBinding(kGroup, "ToggleFire", HandleToggleFire));
            AddBinding(new TriggerBinding(kGroup, "ToggleEMS", HandleToggleEMS));

            // Track external tool changes (Escape key, other tool selected)
            m_ToolSystem.EventToolChanged += OnToolChanged;
        }

        private void HandleToggleTool()
        {
            if (m_ToolSystem.activeTool == m_DispatchTool)
            {
                // Deactivate
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }
            else
            {
                // Activate
                m_ToolSystem.activeTool = m_DispatchTool;
            }
        }

        private void HandleTogglePolice()
        {
            m_DispatchTool.PoliceEnabled = !m_DispatchTool.PoliceEnabled;
            m_PoliceEnabled.Update(m_DispatchTool.PoliceEnabled);
            DeactivateIfNoneEnabled();
        }

        private void HandleToggleFire()
        {
            m_DispatchTool.FireEnabled = !m_DispatchTool.FireEnabled;
            m_FireEnabled.Update(m_DispatchTool.FireEnabled);
            DeactivateIfNoneEnabled();
        }

        private void HandleToggleEMS()
        {
            m_DispatchTool.EMSEnabled = !m_DispatchTool.EMSEnabled;
            m_EMSEnabled.Update(m_DispatchTool.EMSEnabled);
            DeactivateIfNoneEnabled();
        }

        private void DeactivateIfNoneEnabled()
        {
            if (!m_DispatchTool.PoliceEnabled && !m_DispatchTool.FireEnabled && !m_DispatchTool.EMSEnabled)
            {
                if (m_ToolSystem.activeTool == m_DispatchTool)
                {
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                }
            }
        }

        private void OnToolChanged(ToolBaseSystem tool)
        {
            bool isActive = tool == m_DispatchTool;
            m_IsToolActive.Update(isActive);
            m_PanelVisible.Update(isActive);

            if (!isActive)
            {
                // Reset dispatch toggles when tool deactivates
                m_DispatchTool.PoliceEnabled = false;
                m_DispatchTool.FireEnabled = false;
                m_DispatchTool.EMSEnabled = false;
                m_PoliceEnabled.Update(false);
                m_FireEnabled.Update(false);
                m_EMSEnabled.Update(false);
            }
        }
    }
}
