import { escapeHtml } from "./quota-formatters.js?v=51";

export function readDeviceField(device, pascalName, camelName) {
  return device?.[pascalName] ?? device?.[camelName] ?? "";
}

export function formatDeviceTime(value, locale) {
  if (!value) return "--";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "--";
  return date.toLocaleString(locale, {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function createDeviceManagementView({
  document,
  elements,
  t,
  getIntlLocale,
  startPairing,
}) {
  const {
    trustedDevicesPanel,
    newDeviceConnectPanel,
    diagnosticsPanel,
    trustedDeviceList,
    clearTrustedDevices,
  } = elements;
  let defaultApplied = false;

  function syncPanels(isLocal, deviceCount) {
    if (newDeviceConnectPanel) {
      newDeviceConnectPanel.hidden = !isLocal;
      if (!isLocal) {
        defaultApplied = false;
      } else if (!defaultApplied) {
        newDeviceConnectPanel.open = deviceCount === 0;
        if (deviceCount === 0) startPairing();
        defaultApplied = true;
      }
    }
    if (diagnosticsPanel) diagnosticsPanel.hidden = !isLocal;
  }

  function render(status, fallbackLocalRequest) {
    const isLocal = status
      ? Boolean(status.LocalRequest ?? status.localRequest)
      : fallbackLocalRequest;
    const devices = status?.Devices || status?.devices || [];
    syncPanels(isLocal, devices.length);
    trustedDevicesPanel.hidden = !isLocal;
    if (!isLocal) {
      clearTrustedDevices.disabled = true;
      trustedDeviceList.textContent = "";
      return;
    }

    clearTrustedDevices.disabled = !devices.length;
    trustedDeviceList.textContent = "";
    if (!devices.length) {
      const empty = document.createElement("span");
      empty.className = "trusted-devices-empty";
      empty.textContent = "尚未配對裝置。";
      trustedDeviceList.append(empty);
      return;
    }

    for (const device of devices) {
      const id = readDeviceField(device, "DeviceId", "deviceId");
      const name = readDeviceField(device, "Name", "name") || "Phone";
      const lastSeen = readDeviceField(device, "LastSeenAt", "lastSeenAt");
      const remote = readDeviceField(device, "LastRemoteAddress", "lastRemoteAddress") || "local";
      const connected = Boolean(device?.Connected ?? device?.connected);
      const row = document.createElement("div");
      row.className = `trusted-device-row${connected ? " is-connected" : ""}`;
      row.innerHTML = `
        <div class="trusted-device-main">
          <div class="trusted-device-name-row"><strong></strong><b class="trusted-device-status"></b></div>
          <span></span>
        </div>
        <details class="device-management-more trusted-device-more">
          <summary title="${escapeHtml(t("deviceManagement.moreActions"))}" aria-label="${escapeHtml(t("deviceManagement.moreActions"))}">⋯</summary>
          <div><button type="button" data-device-revoke="">${escapeHtml(t("deviceManagement.removePairing"))}</button></div>
        </details>
      `;
      row.querySelector("strong").textContent = name;
      row.querySelector(".trusted-device-status").textContent = connected ? "連線中" : "未連線";
      row.querySelector("span").textContent = t("deviceManagement.lastSeen", {
        time: formatDeviceTime(lastSeen, getIntlLocale()),
      });
      row.querySelector("span").title = t("deviceManagement.address", { address: remote });
      const button = row.querySelector("[data-device-revoke]");
      button.dataset.deviceRevoke = id;
      button.dataset.deviceName = name;
      trustedDeviceList.append(row);
    }
  }

  return { render, syncPanels };
}
