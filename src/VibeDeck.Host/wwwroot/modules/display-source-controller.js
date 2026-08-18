const DISPLAY_SOURCE_KEY = "vibeDeckDisplaySource.v1";

export function normalizeDisplay(display) {
  return {
    DeviceName: display?.DeviceName ?? display?.deviceName ?? "",
    FriendlyName: display?.FriendlyName ?? display?.friendlyName ?? "",
    DeviceId: display?.DeviceId ?? display?.deviceId ?? "",
    OutputIndex: Number(display?.OutputIndex ?? display?.outputIndex ?? 0),
    Width: Number(display?.Width ?? display?.width ?? 0),
    Height: Number(display?.Height ?? display?.height ?? 0),
    IsPrimary: Boolean(display?.IsPrimary ?? display?.isPrimary),
    IsVibeDeckDisplay: Boolean(display?.IsVibeDeckDisplay ?? display?.isVibeDeckDisplay),
  };
}

export function chooseDisplay(displays, stored = {}, selectedName = "") {
  const list = Array.isArray(displays) ? displays : [];
  return list.find(display => stored.deviceId && display.DeviceId === stored.deviceId)
    || list.find(display => stored.deviceName && display.DeviceName === stored.deviceName)
    || list.find(display => display.DeviceName === selectedName)
    || list.find(display => display.IsVibeDeckDisplay)
    || list.find(display => display.IsPrimary)
    || list[0]
    || null;
}

export function createDisplaySourceController({
  document,
  localStorage,
  t,
  elements,
  isLocalRequest,
  setDisplayAspectRatio,
  getDisplayInputController,
}) {
  const {
    displaySource,
    displaySourceKind,
    displayToolbar,
    displayToolbarToggle,
    remoteKeyboardButton,
    remoteKeyboardInput,
    displayEmptyState,
    displayEmptyTitle,
    displayEmptyMessage,
    installVirtualDisplay,
    openSideboardFromEmpty,
    modePreset,
    driverState,
  } = elements;

  let selectedDisplayName = "";
  let selectedDisplay = null;
  let availableDisplays = [];

  function loadStoredPreference() {
    try {
      const value = JSON.parse(localStorage.getItem(DISPLAY_SOURCE_KEY) || "null");
      return value && typeof value === "object" ? value : {};
    } catch {
      return {};
    }
  }

  function storePreference(display) {
    if (!display) return;
    try {
      localStorage.setItem(DISPLAY_SOURCE_KEY, JSON.stringify({
        deviceId: display.DeviceId,
        deviceName: display.DeviceName,
      }));
    } catch { }
  }

  function optionLabel(display) {
    const name = display.IsVibeDeckDisplay
      ? t("ui.virtualDisplaySource")
      : (display.FriendlyName || `${t("ui.display")} ${display.OutputIndex + 1}`);
    const primary = display.IsPrimary ? ` ${t("ui.primaryDisplaySuffix")}` : "";
    return `${name}${primary} · ${display.Width}×${display.Height}`;
  }

  function renderOptions() {
    if (!displaySource) return;
    displaySource.replaceChildren();
    const groups = [
      { label: t("ui.virtualDisplaySource"), values: availableDisplays.filter(display => display.IsVibeDeckDisplay) },
      { label: t("ui.physicalDisplaySource"), values: availableDisplays.filter(display => !display.IsVibeDeckDisplay) },
    ];
    groups.forEach(group => {
      if (!group.values.length) return;
      const optgroup = document.createElement("optgroup");
      optgroup.label = group.label;
      group.values.forEach(display => {
        const option = document.createElement("option");
        option.value = display.DeviceName;
        option.textContent = optionLabel(display);
        optgroup.append(option);
      });
      displaySource.append(optgroup);
    });
    displaySource.value = selectedDisplayName;
  }

  function isToolbarExpanded() {
    return Boolean(displayToolbar && !displayToolbar.classList.contains("is-collapsed"));
  }

  function setToolbarExpanded(expanded) {
    if (!displayToolbar) return;
    displayToolbar.classList.toggle("is-collapsed", !expanded);
    if (displayToolbarToggle) {
      displayToolbarToggle.setAttribute("aria-expanded", expanded ? "true" : "false");
    }
    if (!expanded) {
      getDisplayInputController()?.dismissKeyboard?.();
      remoteKeyboardInput?.blur();
    }
  }

  function toggleToolbarExpanded() {
    setToolbarExpanded(!isToolbarExpanded());
  }

  function isRemoteMobileClient() {
    return document.body.classList.contains("mobile-client")
      && !document.body.classList.contains("pc-console")
      && !isLocalRequest();
  }

  function syncEmptyActions() {
    const mobile = isRemoteMobileClient();
    if (openSideboardFromEmpty) {
      openSideboardFromEmpty.hidden = false;
      openSideboardFromEmpty.textContent = t("pairingUx.useSideboard");
      openSideboardFromEmpty.classList.toggle("secondary", !mobile);
    }
    if (installVirtualDisplay) {
      installVirtualDisplay.textContent = t("pairingUx.goToDeviceSetup");
      if (mobile) {
        installVirtualDisplay.hidden = true;
        installVirtualDisplay.disabled = true;
      }
    }
  }

  function setAvailability(available, title = "", message = "") {
    document.body.classList.toggle("display-unavailable", !available);
    if (displayToolbar) {
      const show = Boolean(available && availableDisplays.length > 0);
      displayToolbar.hidden = !show;
      if (!show) setToolbarExpanded(false);
    }
    if (remoteKeyboardButton) remoteKeyboardButton.disabled = !available;
    if (!available) {
      getDisplayInputController()?.dismissKeyboard?.();
      remoteKeyboardInput?.blur();
    }
    if (displayEmptyState) displayEmptyState.hidden = Boolean(available);
    if (displayEmptyTitle && title) displayEmptyTitle.textContent = title;
    if (displayEmptyMessage && (title || message)) {
      if (isRemoteMobileClient() && !available) {
        const pairingGate = /配對|pair|ペアリング/i.test(`${title} ${message}`);
        displayEmptyMessage.textContent = pairingGate
          ? (message || t("pairingUx.displayBenefit"))
          : t("pairingUx.displayInstallOnPc");
      } else if (message) {
        displayEmptyMessage.textContent = message;
      }
    }
    syncEmptyActions();
  }

  function applySelected(display, persist = true) {
    if (!display) return false;
    selectedDisplay = display;
    selectedDisplayName = display.DeviceName;
    if (persist) storePreference(display);
    if (displaySource) displaySource.value = display.DeviceName;
    if (displayToolbar) {
      displayToolbar.hidden = false;
      setToolbarExpanded(document.body.classList.contains("pc-console"));
    }
    document.body.classList.toggle("physical-display-source", !display.IsVibeDeckDisplay);
    if (displaySourceKind) {
      displaySourceKind.textContent = t(display.IsVibeDeckDisplay
        ? "ui.virtualDisplaySource"
        : "ui.physicalDisplaySource");
    }
    const virtualModePanel = modePreset?.closest("details");
    if (virtualModePanel) virtualModePanel.hidden = !display.IsVibeDeckDisplay;
    setDisplayAspectRatio(`${display.Width} / ${display.Height}`);
    driverState.classList.remove("good", "warn");
    driverState.textContent = display.IsVibeDeckDisplay
      ? `${t("ui.virtualDisplayReady")} · ${display.Width}×${display.Height}`
      : `${t("ui.currentDisplay")} · ${display.FriendlyName || display.DeviceName} · ${display.Width}×${display.Height}`;
    setAvailability(true);
    return true;
  }

  function setDisplays(displays) {
    availableDisplays = Array.isArray(displays)
      ? displays.map(normalizeDisplay).filter(display => display.DeviceName && display.Width > 0 && display.Height > 0)
      : [];
    return availableDisplays;
  }

  function chooseCurrent() {
    return chooseDisplay(availableDisplays, loadStoredPreference(), selectedDisplayName);
  }

  function findByName(name) {
    return availableDisplays.find(display => display.DeviceName === name) || null;
  }

  function clear({ resetDisplays = false, resetAspect = true, hideToolbar = true } = {}) {
    selectedDisplay = null;
    selectedDisplayName = "";
    if (resetDisplays) availableDisplays = [];
    if (hideToolbar && displayToolbar) displayToolbar.hidden = true;
    document.body.classList.remove("physical-display-source");
    if (resetAspect) setDisplayAspectRatio("16 / 9");
  }

  function resetSelectionName() {
    selectedDisplayName = "";
  }

  function refreshLocalizedUi() {
    syncEmptyActions();
    renderOptions();
    if (selectedDisplay) applySelected(selectedDisplay, false);
  }

  return {
    setDisplays,
    chooseCurrent,
    findByName,
    applySelected,
    clear,
    resetSelectionName,
    renderOptions,
    refreshLocalizedUi,
    isRemoteMobileClient,
    syncEmptyActions,
    setAvailability,
    isToolbarExpanded,
    setToolbarExpanded,
    toggleToolbarExpanded,
    getAvailableDisplays: () => availableDisplays,
    getSelectedDisplay: () => selectedDisplay,
    getSelectedName: () => selectedDisplayName,
    hasVibeDeckDisplay: () => availableDisplays.some(display => display.IsVibeDeckDisplay),
  };
}
