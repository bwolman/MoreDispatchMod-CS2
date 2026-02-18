import { ModRegistrar } from "cs2/modding";
import { DispatchToolButton } from "mods/DispatchToolButton";

const register: ModRegistrar = (moduleRegistry) => {
    // Single GameTopLeft component contains both button and flyout panel
    moduleRegistry.append("GameTopLeft", DispatchToolButton);

    console.log("MoreDispatchMod UI module registered.");
};

export default register;
