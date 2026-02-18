import { bindValue, trigger } from "cs2/api";

const MOD_GROUP = "MoreDispatchMod";

// Value bindings (C# → JS)
export const isToolActive$ = bindValue<boolean>(MOD_GROUP, "IsToolActive", false);
export const policeEnabled$ = bindValue<boolean>(MOD_GROUP, "PoliceEnabled", false);
export const fireEnabled$ = bindValue<boolean>(MOD_GROUP, "FireEnabled", false);
export const emsEnabled$ = bindValue<boolean>(MOD_GROUP, "EMSEnabled", false);
export const crimeEnabled$ = bindValue<boolean>(MOD_GROUP, "CrimeEnabled", false);
export const panelVisible$ = bindValue<boolean>(MOD_GROUP, "PanelVisible", false);

// Trigger bindings (JS → C#) — use .bind() pattern per CS2 modding convention
export const toggleTool = trigger.bind(null, MOD_GROUP, "ToggleTool");
export const togglePolice = trigger.bind(null, MOD_GROUP, "TogglePolice");
export const toggleFire = trigger.bind(null, MOD_GROUP, "ToggleFire");
export const toggleEMS = trigger.bind(null, MOD_GROUP, "ToggleEMS");
export const toggleCrime = trigger.bind(null, MOD_GROUP, "ToggleCrime");
