import { useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";
import {
    isToolActive$,
    panelVisible$,
    policeEnabled$,
    fireEnabled$,
    emsEnabled$,
    crimeEnabled$,
    areaCrimeEnabled$,
    toggleTool,
    togglePolice,
    toggleFire,
    toggleEMS,
    toggleCrime,
    toggleAreaCrime,
} from "./bindings";
import styles from "./DispatchToolButton.module.scss";

export const DispatchToolButton = () => {
    const isActive = useValue(isToolActive$);
    const panelOpen = useValue(panelVisible$);
    const policeOn = useValue(policeEnabled$);
    const fireOn = useValue(fireEnabled$);
    const emsOn = useValue(emsEnabled$);
    const crimeOn = useValue(crimeEnabled$);
    const areaCrimeOn = useValue(areaCrimeEnabled$);

    return (
        <>
            <Tooltip tooltip="Manual Dispatch">
                <Button
                    variant="floating"
                    className={styles.dispatchButton}
                    selected={isActive}
                    onSelect={toggleTool}
                >
                    <img
                        src="coui://uil/Standard/Alarm.svg"
                        className={styles.icon}
                        alt="Dispatch"
                    />
                </Button>
            </Tooltip>
            {panelOpen && (
                <div className={styles.panel}>
                    <div className={styles.header}>Manual Dispatch</div>
                    <div className={styles.options}>
                        <Button
                            variant="flat"
                            className={`${styles.option} ${policeOn ? styles.active : ""}`}
                            selected={policeOn}
                            onSelect={togglePolice}
                        >
                            <img src="coui://uil/Standard/Police.svg" className={styles.optionIcon} />
                            <span>Police</span>
                        </Button>
                        <Button
                            variant="flat"
                            className={`${styles.option} ${fireOn ? styles.active : ""}`}
                            selected={fireOn}
                            onSelect={toggleFire}
                        >
                            <img src="coui://uil/Standard/FireSafety.svg" className={styles.optionIcon} />
                            <span>Fire</span>
                        </Button>
                        <Button
                            variant="flat"
                            className={`${styles.option} ${emsOn ? styles.active : ""}`}
                            selected={emsOn}
                            onSelect={toggleEMS}
                        >
                            <img src="coui://uil/Standard/Healthcare.svg" className={styles.optionIcon} />
                            <span>EMS</span>
                        </Button>
                        <Button
                            variant="flat"
                            className={`${styles.option} ${crimeOn ? styles.active : ""}`}
                            selected={crimeOn}
                            onSelect={toggleCrime}
                        >
                            <img src="coui://uil/Standard/Crime.svg" className={styles.optionIcon} />
                            <span>Single Crime</span>
                        </Button>
                        <Button
                            variant="flat"
                            className={`${styles.option} ${areaCrimeOn ? styles.active : ""}`}
                            selected={areaCrimeOn}
                            onSelect={toggleAreaCrime}
                        >
                            <img src="coui://uil/Standard/Crime.svg" className={styles.optionIcon} />
                            <span>Area Crime</span>
                        </Button>
                    </div>
                </div>
            )}
        </>
    );
};
