export function formatVerificationCode(value) {
  const normalized = String(value || "").replace(/\D/g, "");
  return normalized.length === 6
    ? `${normalized.slice(0, 3)} ${normalized.slice(3)}`
    : normalized || "------";
}

export function createDeviceActionsController({
  document,
  elements,
  t,
  tLegacy,
  fetchJsonOrThrow,
  confirmAction,
  isLocalRequest,
  setPairingUiActive,
  setPairingProgress,
  updatePairingGuide,
  setTrustState,
  persistDeviceCredentials,
  reloadTrustStatus,
}) {
  const { pendingPairingPanel, pendingPairingList, trustedDeviceList } = elements;

  function renderPending(result) {
    const requests = result?.Requests || result?.requests || [];
    pendingPairingPanel.hidden = !requests.length;
    if (requests.length) {
      setPairingUiActive(true);
      setPairingProgress(75, tLegacy("手機申請已送達，等待 PC 核准"));
      updatePairingGuide("approve");
    }
    pendingPairingList.replaceChildren();
    for (const request of requests) {
      const row = document.createElement("div");
      row.className = "pending-pairing-row";
      const requestId = request.RequestId || request.requestId;
      row.innerHTML = `<div><strong></strong><span></span><b></b></div><button data-pair-approve></button><button data-pair-deny></button>`;
      row.querySelector("strong").textContent = request.Name || request.name || t("pairingUx.newDeviceDefault");
      const platform = request.Platform || request.platform || "";
      const remoteAddress = request.RemoteAddress || request.remoteAddress || "";
      row.querySelector("span").textContent = platform;
      row.querySelector("span").title = remoteAddress
        ? t("deviceManagement.address", { address: remoteAddress })
        : "";
      row.querySelector("b").textContent = t("pairingUx.verificationCode", {
        code: formatVerificationCode(request.VerificationCode || request.verificationCode),
      });
      row.querySelector("[data-pair-approve]").textContent = t("pairingUx.allow");
      row.querySelector("[data-pair-deny]").textContent = t("pairingUx.deny");
      row.querySelector("[data-pair-approve]").dataset.requestId = requestId;
      row.querySelector("[data-pair-deny]").dataset.requestId = requestId;
      pendingPairingList.append(row);
    }
  }

  async function loadPending() {
    if (!isLocalRequest()) return;
    try {
      renderPending(await fetchJsonOrThrow("/api/devices/pairing/pending", { method: "POST" }));
    } catch { }
  }

  async function actOnPending(requestId, approve) {
    await fetchJsonOrThrow(`/api/devices/pairing/${approve ? "approve" : "deny"}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ requestId }),
    });
    await loadPending();
    await reloadTrustStatus();
  }

  async function revoke(deviceId, deviceName, button) {
    if (!deviceId) return;
    if (!await confirmAction({
      title: t("deviceManagement.removePairing"),
      message: t("deviceManagement.revokeConfirm", { device: deviceName || "--" }),
      confirmLabel: t("deviceManagement.removePairing"),
      cancelLabel: tLegacy("取消"),
      tone: "danger",
    })) return;

    const originalText = button?.textContent || "";
    if (button) {
      button.disabled = true;
      button.textContent = "…";
    }

    try {
      const result = await fetchJsonOrThrow("/api/devices/revoke", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ deviceId }),
      });
      setTrustState(result.Message || result.message || t("deviceManagement.revokeSuccess"), true);
      await reloadTrustStatus();
    } catch (error) {
      setTrustState(error.message || t("deviceManagement.revokeFailed"), false);
    } finally {
      if (button) {
        button.disabled = false;
        button.textContent = originalText;
      }
    }
  }

  async function clear(button) {
    const deviceCount = trustedDeviceList.querySelectorAll(".trusted-device-row").length;
    if (!await confirmAction({
      title: t("deviceManagement.clearAll"),
      message: t("deviceManagement.clearAllConfirm", { count: deviceCount }),
      confirmLabel: t("deviceManagement.clearAll"),
      cancelLabel: tLegacy("取消"),
      tone: "danger",
    })) return;

    const originalText = button?.textContent || "";
    if (button) {
      button.disabled = true;
      button.textContent = t("deviceManagement.clearBusy");
    }

    try {
      const result = await fetchJsonOrThrow("/api/devices/clear", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });
      persistDeviceCredentials("", "");
      setTrustState(result.Message || result.message || t("deviceManagement.clearSuccess"), true);
      await reloadTrustStatus();
    } catch (error) {
      setTrustState(error.message || t("deviceManagement.clearFailed"), false);
    } finally {
      if (button) {
        button.disabled = false;
        button.textContent = originalText;
      }
    }
  }

  return { renderPending, loadPending, actOnPending, revoke, clear };
}
