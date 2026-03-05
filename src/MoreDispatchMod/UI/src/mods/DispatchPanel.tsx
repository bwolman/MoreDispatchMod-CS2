import { useValue } from "cs2/api";
import { Button, Portal } from "cs2/ui";
import {
    panelVisible$,
    policeEnabled$,
    fireEnabled$,
    emsEnabled$,
    crimeEnabled$,
    accidentEnabled$,
    areaCrimeEnabled$,
    taxiRerouteEnabled$,
    togglePolice,
    toggleFire,
    toggleEMS,
    toggleCrime,
    toggleAccident,
    toggleAreaCrime,
    toggleTaxiReroute,
} from "./bindings";
import styles from "./DispatchPanel.module.scss";

export const DispatchPanel = () => {
    const visible = useValue(panelVisible$);
    const policeOn = useValue(policeEnabled$);
    const fireOn = useValue(fireEnabled$);
    const emsOn = useValue(emsEnabled$);
    const crimeOn = useValue(crimeEnabled$);
    const accidentOn = useValue(accidentEnabled$);
    const areaCrimeOn = useValue(areaCrimeEnabled$);
    const taxiRerouteOn = useValue(taxiRerouteEnabled$);

    if (!visible) return null;

    return (
        <Portal>
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
                    <Button
                        variant="flat"
                        className={`${styles.option} ${crimeOn ? styles.active : ""}`}
                        selected={crimeOn}
                        onSelect={toggleCrime}
                    >
                        <img src="coui://uil/Standard/Crime.svg" className={styles.optionIcon} alt="Crime" />
                        <span>Crime</span>
                    </Button>
                    <Button
                        variant="flat"
                        className={`${styles.option} ${accidentOn ? styles.active : ""}`}
                        selected={accidentOn}
                        onSelect={toggleAccident}
                    >
                        <img src="coui://uil/Standard/Car.svg" className={styles.optionIcon} alt="Accident" />
                        <span>Accident</span>
                    </Button>
                    <Button
                        variant="flat"
                        className={`${styles.option} ${areaCrimeOn ? styles.active : ""}`}
                        selected={areaCrimeOn}
                        onSelect={toggleAreaCrime}
                    >
                        <img src="coui://uil/Standard/Crime.svg" className={styles.optionIcon} alt="Area Crime" />
                        <span>Area Crime</span>
                    </Button>
                    <Button
                        variant="flat"
                        className={`${styles.option} ${taxiRerouteOn ? styles.active : ""}`}
                        selected={taxiRerouteOn}
                        onSelect={toggleTaxiReroute}
                    >
                        <img src="coui://uil/Standard/Car.svg" className={styles.optionIcon} alt="Taxi Reroute" />
                        <span>Taxi Reroute</span>
                    </Button>
                </div>
            </div>
        </Portal>
    );
};
