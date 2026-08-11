export function buildProductUpdateView(result, t) {
  const state = String(result?.State || result?.state || "idle").toLowerCase();
  const code = String(result?.Code || result?.code || state).toLowerCase();
  const latestVersion = String(result?.LatestVersion || result?.latestVersion || "");
  const downloadPercent = Number(result?.DownloadPercent ?? result?.downloadPercent ?? 0);
  const canStart = Boolean(result?.CanStart ?? result?.canStart);
  const busy = ["checking", "downloading", "ready", "launching"].includes(state);

  const message = (() => {
    switch (code) {
      case "checking": return t("updates.checking");
      case "current": return t("updates.current");
      case "available": return t("updates.available", { version: latestVersion || "?" });
      case "downloading": return t("updates.downloading", { percent: downloadPercent || 0 });
      case "verified": return t("updates.verified");
      case "installer_started": return t("updates.installerStarted");
      case "not_published": return t("updates.notPublished");
      case "installed_product_required": return t("updates.installedOnly");
      case "idle": return "";
      default: return t("updates.failed");
    }
  })();

  const statusMessage = (() => {
    switch (code) {
      case "checking": return t("updates.statusChecking");
      case "current": return t("updates.statusCurrent");
      case "available": return t("updates.statusAvailable", { version: latestVersion || "?" });
      case "downloading": return t("updates.statusDownloading", { percent: downloadPercent || 0 });
      case "verified": return t("updates.statusVerified");
      case "installer_started": return t("updates.statusInstallerStarted");
      case "not_published": return t("updates.statusNotPublished");
      case "installed_product_required": return t("updates.statusInstalledOnly");
      case "idle": return "";
      default: return t("updates.statusFailed");
    }
  })();

  const feedbackState = state === "failed"
    ? "error"
    : busy
      ? "working"
      : state === "current" || code === "installer_started"
        ? "success"
        : ["not_published", "installed_product_required"].includes(code)
          ? "warning"
          : message
            ? "info"
            : "muted";

  const buttonText = state === "available" && canStart
    ? t("updates.install", { version: latestVersion || "?" })
    : busy
      ? t("updates.updateInProgress")
      : state === "current"
        ? t("updates.checkAgain")
        : state === "failed"
          ? t("updates.tryAgain")
          : t("updates.check");

  return {
    state,
    code,
    latestVersion,
    downloadPercent,
    canStart,
    busy,
    message,
    statusMessage,
    feedbackState,
    buttonText,
  };
}

export function createProductUpdateController({
  button,
  status,
  t,
  tLegacy,
  applyFeedbackState,
  fetchJsonOrThrow,
  confirmAction,
  isLocalConsole,
}) {
  let snapshot = null;
  let pollTimer = null;

  function render(result) {
    if (!button) return;
    snapshot = result || null;
    const view = buildProductUpdateView(result, t);

    button.dataset.updateState = view.state;
    button.disabled = view.busy;
    button.title = view.message;
    button.textContent = view.buttonText;

    if (status) {
      applyFeedbackState(status, {
        message: view.statusMessage,
        detail: view.message,
        state: view.feedbackState,
      });
      status.hidden = !isLocalConsole() || !view.statusMessage;
    }
  }

  function renderCurrent() {
    render(snapshot);
  }

  function stopPolling() {
    if (pollTimer) clearTimeout(pollTimer);
    pollTimer = null;
  }

  function setLocalConsole(localConsole) {
    if (!button) return;
    button.hidden = !localConsole;
    if (localConsole) button.removeAttribute("hidden");
    else button.setAttribute("hidden", "");
    if (!localConsole && status) status.hidden = true;
    if (!localConsole) stopPolling();
    else renderCurrent();
  }

  function scheduleStatusPoll() {
    stopPolling();
    const poll = async () => {
      if (!isLocalConsole()) return;
      try {
        const result = await fetchJsonOrThrow("/api/product-update/status");
        render(result);
        const state = String(result?.State || result?.state || "").toLowerCase();
        if (["checking", "downloading", "ready"].includes(state)) {
          pollTimer = setTimeout(poll, 700);
        }
      } catch { }
    };
    pollTimer = setTimeout(poll, 350);
  }

  async function checkOrInstall() {
    if (!isLocalConsole() || !button) return;
    const view = buildProductUpdateView(snapshot, t);
    if (view.state === "available" && view.canStart) {
      if (!await confirmAction({
        title: t("updates.install", { version: view.latestVersion || "?" }),
        message: t("updates.confirm", { version: view.latestVersion || "?" }),
        confirmLabel: t("updates.install", { version: view.latestVersion || "?" }),
        cancelLabel: tLegacy("取消"),
      })) return;
      const result = await fetchJsonOrThrow("/api/product-update/start", { method: "POST" });
      render(result);
      scheduleStatusPoll();
      return;
    }

    render({ state: "checking", code: "checking" });
    try {
      render(await fetchJsonOrThrow("/api/product-update/check", { method: "POST" }));
    } catch (error) {
      renderFailure(error);
    }
  }

  function renderFailure(error) {
    render({ state: "failed", code: "update_failed" });
    if (status) {
      applyFeedbackState(status, {
        message: t("updates.failed"),
        detail: error?.message || "",
        state: "error",
      });
    }
  }

  return {
    render,
    renderCurrent,
    setLocalConsole,
    checkOrInstall,
    renderFailure,
    stopPolling,
  };
}
