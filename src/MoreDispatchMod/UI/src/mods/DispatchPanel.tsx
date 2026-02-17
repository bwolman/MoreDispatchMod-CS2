import { useValue } from "cs2/api";
import { Button } from "cs2/ui";
import {
    panelVisible$,
    policeEnabled$,
    fireEnabled$,
    emsEnabled$,
    togglePolice,
    toggleFire,
    toggleEMS,
} from "./bindings";
import styles from "./DispatchPanel.module.scss";

export const DispatchPanel = () => {
    const visible = useValue(panelVisible$);
    const policeOn = useValue(policeEnabled$);
    const fireOn = useValue(fireEnabled$);
    const emsOn = useValue(emsEnabled$);

    if (!visible) return null;

    return (
        <div className={styles.panel}>
            <div className={styles.header}>Manual Dispatch</div>
            <div className={styles.options}>
                <Button
                    variant="flat"
                    className={`${styles.option} ${policeOn ? styles.active : ""}`}
                    selected={policeOn}
                    onSelect={togglePolice}
                >
                    <img src="coui://uil/Standard/Police.svg" className={styles.optionIcon} alt="Police" />
                    <span>Police</span>
                </Button>
                <Button
                    variant="flat"
                    className={`${styles.option} ${fireOn ? styles.active : ""}`}
                    selected={fireOn}
                    onSelect={toggleFire}
                >
                    <img src="coui://uil/Standard/FireSafety.svg" className={styles.optionIcon} alt="Fire" />
                    <span>Fire</span>
                </Button>
                <Button
                    variant="flat"
                    className={`${styles.option} ${emsOn ? styles.active : ""}`}
                    selected={emsOn}
                    onSelect={toggleEMS}
                >
                    <img src="coui://uil/Standard/Healthcare.svg" className={styles.optionIcon} alt="EMS" />
                    <span>EMS</span>
                </Button>
            </div>
        </div>
    );
};
