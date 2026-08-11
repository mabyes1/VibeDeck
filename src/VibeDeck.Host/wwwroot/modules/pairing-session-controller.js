import { formatVerificationCode } from "./device-actions-controller.js?v=1";

export function resolvePairingDisplayName(baseName, model) {
  const normalizedModel = String(model || "").trim();
  if (!normalizedModel || normalizedModel.toUpperCase() === "K") return baseName;
  if (/^SM-/i.test(normalizedModel)) return `Samsung ${normalizedModel.toUpperCase()}`;
  if (/^(GoColor7|BOOXGoColor7)$/i.test(normalizedModel.replace(/\s+/g, ""))) return "BOOX Go Color 7";
  return normalizedModel;
}

export function hasPendingPairingRecord(value) {
  return Boolean(value?.requestId && value?.requestSecret);
}

export function createPairingSessionController({
  document,
  navigator,
  localStorage,
  history,
  location,
  fetch,
  t,
  tLegacy,
  parseJsonResponse,
  elements,
  isDeviceTrusted,
  isLocalRequest,
  isHostAuthenticated,
  isBooxPreview,
  isEinkClient,
  isIos,
  isStandaloneApp,
  getClientInstanceId,
  setPairingProgress,
  setTrustState,
  updatePairingGuide,
  persistDeviceCredentials,
  reloadTrustStatus,
}) {
  const { phonePairIntro, phonePairRequest, successBanner } = elements;
  let resolvedInfo = null;
  let approvalPollTimer = null;
  let successTimer = null;
  let autoPairFromConnectionCode = new URLSearchParams(location.search).get("source") === "connection-code" &&
    new URLSearchParams(location.search).get("autopair") === "1";

  function storageKey() {
    return `vibeDeckPendingApproval:${location.host}`;
  }

  function readStoredPending() {
    try {
      return JSON.parse(localStorage.getItem(storageKey()) || "null");
    } catch {
      return null;
    }
  }

  function hasStoredPending() {
    return hasPendingPairingRecord(readStoredPending());
  }

  function clearStoredPending() {
    localStorage.removeItem(storageKey());
  }

  function stopPolling() {
    if (approvalPollTimer) clearInterval(approvalPollTimer);
    approvalPollTimer = null;
  }

  function hasActivePoll() {
    return Boolean(approvalPollTimer);
  }

  function platform() {
    if (isEinkClient()) return "E-paper";
    if (isIos()) return "iOS";
    if (/Android/i.test(navigator.userAgent || "")) return "Android";
    return "Web";
  }

  function deviceName() {
    if (resolvedInfo?.name) return resolvedInfo.name;
    if (isBooxPreview()) return "BOOX Go Color 7";
    if (isEinkClient()) return "E-paper device";
    if (isIos()) return "iPhone";
    if (/Android/i.test(navigator.userAgent || "")) return "Android device";
    return navigator.platform || "Web device";
  }

  async function resolveDeviceInfo() {
    if (resolvedInfo) return resolvedInfo;
    let model = "";
    try {
      const data = await navigator.userAgentData?.getHighEntropyValues?.(["model", "platform", "platformVersion"]);
      model = String(data?.model || "").trim();
    } catch { }

    resolvedInfo = {
      name: resolvePairingDisplayName(deviceName(), model),
      model,
      platform: platform(),
      clientInstanceId: getClientInstanceId(),
    };
    return resolvedInfo;
  }

  function showSuccess(message) {
    if (!successBanner) return;
    const title = document.createElement("strong");
    title.textContent = t("pairingUx.successTitle");
    const detail = document.createElement("div");
    detail.textContent = message || "";
    const homeHint = document.createElement("div");
    homeHint.textContent = isIos() && !isStandaloneApp()
      ? t("pairingUx.successIosHomeHint")
      : t("pairingUx.successNextHint");
    successBanner.replaceChildren(title, detail, homeHint);
    successBanner.classList.add("show");
    if (successTimer) clearTimeout(successTimer);
    successTimer = setTimeout(hideSuccess, 6000);
  }

  function hideSuccess() {
    if (successBanner) successBanner.classList.remove("show");
  }

  async function request(options = {}) {
    if (isDeviceTrusted() || isLocalRequest() || isHostAuthenticated() || approvalPollTimer) return;
    const allowCreate = options.allowCreate === true;
    let pending = readStoredPending();

    if (!hasPendingPairingRecord(pending)) {
      if (!allowCreate) {
        setPairingProgress(null, t("pairingUx.readyToConnect"));
        setTrustState(tLegacy("請按「連接這台電腦」，再回 PC 按允許。"), false);
        if (phonePairIntro) phonePairIntro.textContent = t("pairingUx.phoneIntro");
        updatePairingGuide("pair");
        return false;
      }

      setPairingProgress(40, t("pairingUx.identifyingDevice"));
      const deviceInfo = await resolveDeviceInfo();
      const response = await fetch("/api/devices/pairing/request", {
        method: "POST",
        cache: "no-store",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(deviceInfo),
      });
      const result = parseJsonResponse(await response.text(), "/api/devices/pairing/request");
      if (!response.ok) throw new Error(result.error || result.message || "無法提出配對申請。");
      pending = {
        requestId: result.RequestId || result.requestId,
        requestSecret: result.RequestSecret || result.requestSecret,
        verificationCode: result.VerificationCode || result.verificationCode,
      };
      localStorage.setItem(storageKey(), JSON.stringify(pending));
    }

    const verificationCode = formatVerificationCode(pending.verificationCode);
    setPairingProgress(70, t("pairingUx.waitingForApprovalCode", { code: verificationCode }));
    setTrustState(t("pairingUx.waitingForApprovalCode", { code: verificationCode }), false);
    if (phonePairIntro) phonePairIntro.textContent = t("pairingUx.phoneWaitingApproval", { code: verificationCode });
    if (phonePairRequest) {
      phonePairRequest.disabled = true;
      phonePairRequest.textContent = t("pairingUx.requestSent");
    }

    const poll = async () => {
      try {
        const response = await fetch("/api/devices/pairing/poll", {
          method: "POST",
          cache: "no-store",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ requestId: pending.requestId, requestSecret: pending.requestSecret }),
        });
        const result = parseJsonResponse(await response.text(), "/api/devices/pairing/poll");
        const status = result.Status || result.status;
        if (response.ok && status === "approved") {
          setPairingProgress(90, t("pairingUx.savingPairing"));
          persistDeviceCredentials(result.DeviceToken || result.deviceToken, result.DeviceId || result.deviceId);
          clearStoredPending();
          stopPolling();
          const trust = await reloadTrustStatus();
          const trusted = Boolean(trust?.Trusted ?? trust?.trusted);
          if (!trusted) {
            setTrustState(t("pairingUx.tokenReadyReload"), true);
          }
          showSuccess(t("pairingUx.successDevice", {
            name: result.DeviceName || result.deviceName || deviceName(),
          }));
          setPairingProgress(100, (result.Continued ?? result.continued)
            ? t("pairingUx.resumedPairing")
            : t("pairingUx.pairingComplete"));
          setTimeout(() => {
            const url = new URL(location.href);
            url.searchParams.set("paired", Date.now().toString());
            location.replace(url.toString());
          }, 250);
        } else if (status === "denied" || status === "expired") {
          clearStoredPending();
          stopPolling();
          const denied = status === "denied";
          setPairingProgress(null, t(denied ? "pairingUx.denied" : "pairingUx.expired"));
          setTrustState(t(denied ? "pairingUx.deniedDetail" : "pairingUx.expiredDetail"), false);
          if (phonePairIntro) phonePairIntro.textContent = t(denied ? "pairingUx.deniedDetail" : "pairingUx.expiredDetail");
          if (phonePairRequest) {
            phonePairRequest.disabled = false;
            phonePairRequest.textContent = t("pairingUx.tryAgain");
          }
        }
      } catch { }
    };

    approvalPollTimer = setInterval(poll, 1500);
    poll();
    return true;
  }

  function consumeConnectionCodeAutoPairing() {
    if (!autoPairFromConnectionCode) return false;
    autoPairFromConnectionCode = false;
    const url = new URL(location.href);
    url.searchParams.delete("source");
    url.searchParams.delete("autopair");
    history.replaceState({}, "", url.pathname + url.search + url.hash);
    return true;
  }

  return {
    platform,
    deviceName,
    resolveDeviceInfo,
    request,
    hasStoredPending,
    clearStoredPending,
    stopPolling,
    hasActivePoll,
    consumeConnectionCodeAutoPairing,
    showSuccess,
    hideSuccess,
  };
}
