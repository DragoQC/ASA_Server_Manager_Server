(function () {
    const hueStorageKey = "asa-theme-hue";
    const partyModeStorageKey = "asa-theme-party-mode";
    const minimumHue = 0;
    const maximumHue = 359;
    const root = document.documentElement;

    function clampHue(value) {
        return Math.max(minimumHue, Math.min(maximumHue, value));
    }

    function normalizeHue(value, fallbackHue) {
        const parsedValue = Number.parseInt(`${value ?? ""}`, 10);
        const fallbackValue = Number.parseInt(`${fallbackHue ?? ""}`, 10);
        const resolvedFallback = Number.isFinite(fallbackValue) ? fallbackValue : 191;
        const resolvedValue = Number.isFinite(parsedValue) ? parsedValue : resolvedFallback;
        return clampHue(resolvedValue);
    }

    function applyHue(hue) {
        const resolvedHue = normalizeHue(hue, 191);
        root.style.setProperty("--theme-hue", `${resolvedHue}deg`);
        window.dispatchEvent(new CustomEvent("asa-theme-change", { detail: { hue: resolvedHue } }));
        return resolvedHue;
    }

    window.asaTheme = {
        init(defaultHue) {
            return this.getState(defaultHue).hue;
        },

        getState(defaultHue) {
            let storedHue = null;
            let storedPartyMode = null;

            try {
                storedHue = window.localStorage.getItem(hueStorageKey);
                storedPartyMode = window.localStorage.getItem(partyModeStorageKey);
            } catch {
                storedHue = null;
                storedPartyMode = null;
            }

            const resolvedHue = applyHue(normalizeHue(storedHue, defaultHue));
            const isPartyModeEnabled = storedPartyMode === "true";

            return {
                hue: resolvedHue,
                isPartyModeEnabled
            };
        },

        setHue(hue) {
            const resolvedHue = applyHue(hue);

            try {
                window.localStorage.setItem(hueStorageKey, `${resolvedHue}`);
            } catch {
            }
        },

        setPartyMode(isEnabled) {
            try {
                window.localStorage.setItem(partyModeStorageKey, isEnabled ? "true" : "false");
            } catch {
            }
        }
    };
})();
