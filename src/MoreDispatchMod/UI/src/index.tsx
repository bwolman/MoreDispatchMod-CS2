import { ModRegistrar } from "cs2/modding";
import { DispatchToolButton } from "mods/DispatchToolButton";
import { DispatchPanel } from "mods/DispatchPanel";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append("GameTopLeft", DispatchToolButton);
    moduleRegistry.append("Game", DispatchPanel);

    console.log("MoreDispatchMod UI module registered.");
};

export default register;
