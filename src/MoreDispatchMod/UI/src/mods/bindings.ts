import { bindValue, trigger } from "cs2/api";

const MOD_GROUP = "MoreDispatchMod";

// Value bindings (C# → JS)
export const isToolActive$ = bindValue<boolean>(MOD_GROUP, "IsToolActive", false);
export const policeEnabled$ = bindValue<boolean>(MOD_GROUP, "PoliceEnabled", false);
export const fireEnabled$ = bindValue<boolean>(MOD_GROUP, "FireEnabled", false);
export const emsEnabled$ = bindValue<boolean>(MOD_GROUP, "EMSEnabled", false);
export const panelVisible$ = bindValue<boolean>(MOD_GROUP, "PanelVisible", false);

// Trigger bindings (JS → C#)
export const toggleTool = () => trigger(MOD_GROUP, "ToggleTool");
export const togglePolice = () => trigger(MOD_GROUP, "TogglePolice");
export const toggleFire = () => trigger(MOD_GROUP, "ToggleFire");
export const toggleEMS = () => trigger(MOD_GROUP, "ToggleEMS");
