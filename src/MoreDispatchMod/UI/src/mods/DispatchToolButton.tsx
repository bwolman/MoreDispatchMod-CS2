import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { isToolActive$, toggleTool } from "./bindings";
import styles from "./DispatchToolButton.module.scss";

export const DispatchToolButton = () => {
    const isActive = useValue(isToolActive$);

    return (
        <Button
            variant="floating"
            className={styles.dispatchButton}
            selected={isActive}
            onSelect={toggleTool}
        >
            <img
                src="coui://uil/Standard/StarFilled.svg"
                className={styles.icon}
            />
        </Button>
    );
};
