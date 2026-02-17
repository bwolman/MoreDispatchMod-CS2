// CSS/SCSS module declarations
declare module "*.module.scss" {
    const classes: { readonly [key: string]: string };
    export default classes;
}

declare module "*.module.css" {
    const classes: { readonly [key: string]: string };
    export default classes;
}

// CS2 runtime modules (provided by the game engine, not bundled)
declare module "cs2/api" {
    interface ValueBinding<T> { __brand: T }
    export function bindValue<T>(group: string, name: string, fallback?: T): ValueBinding<T>;
    export function trigger(group: string, name: string, ...args: any[]): void;
    export function useValue<T>(binding: ValueBinding<T>): T;
}

declare module "cs2/modding" {
    import { ComponentType } from "react";
    export interface ModuleRegistry {
        append(module: string, component: ComponentType<any>): void;
        extend(module: string, extensionId: string, component: ComponentType<any>): void;
    }
    export type ModRegistrar = (moduleRegistry: ModuleRegistry) => void;
}

declare module "cs2/ui" {
    import { ComponentType, ReactNode } from "react";
    export interface ButtonProps {
        variant?: "floating" | "flat" | "icon" | string;
        className?: string;
        selected?: boolean;
        onSelect?: () => void;
        children?: ReactNode;
    }
    export const Button: ComponentType<ButtonProps>;
}

declare module "mod.json" {
    const mod: { id: string };
    export default mod;
}
