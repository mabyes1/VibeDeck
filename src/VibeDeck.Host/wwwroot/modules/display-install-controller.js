export function readDisplayInstallField(status, name, fallback = null) {
  return status?.[name] ?? status?.[name.charAt(0).toLowerCase() + name.slice(1)] ?? fallback;
}

export function buildDisplayInstallView(status, tApi) {
  const state = readDisplayInstallField(status, "State", "ready");
  const message = readDisplayInstallField(status, "Message", "");
  const code = readDisplayInstallField(status, "Code", "");
  const canInstall = Boolean(readDisplayInstallField(status, "CanInstall", false));
  const feedbackState = state === "failed"
    ? "error"
    : state === "installing" || state === "finishing"
      ? "working"
      : state === "installed"
        ? "success"
        : ["restart-required", "console-required", "repair-ready"].includes(state)
          ? "warning"
          : "info";

  return {
    state,
    message,
    code,
    localizedMessage: tApi(code, message),
    canInstall,
    feedbackState,
  };
}

export function createDisplayInstallController({
  elements,
  t,
  tApi,
  tLegacy,
  applyFeedbackState,
  fetchJsonOrThrow,
  isLocalRequest,
  isPhoneClient,
  syncEmptyActions,
  setAvailability,
  reloadDisplays,
  hasVibeDeckDisplay,
  connectVideo,
}) {
  const {
    displayInstallDetail,
    setupDisplayInstallDetail,
    installVirtualDisplay,
    setupInstallVirtualDisplay,
  } = elements;
  let pollTimer = null;

  function render(status) {
    const view = buildDisplayInstallView(status, tApi);
    const localRequest = isLocalRequest();

    if (displayInstallDetail) {
      applyFeedbackState(displayInstallDetail, {
        message: localRequest
          ? view.localizedMessage
          : "請到這台 PC 的 VibeDeck 頁面建立虛擬螢幕；手機不能遠端觸發管理員安裝。",
        state: localRequest ? view.feedbackState : "info",
      });
    }
    if (setupDisplayInstallDetail) {
      applyFeedbackState(setupDisplayInstallDetail, {
        message: view.localizedMessage || "等待虛擬螢幕狀態。",
        state: view.feedbackState,
      });
    }

    if (installVirtualDisplay) {
      const phone = isPhoneClient();
      installVirtualDisplay.hidden = phone
        || !localRequest
        || view.state === "installed"
        || view.state === "finishing"
        || view.state === "console-required";
      installVirtualDisplay.disabled = phone;
      installVirtualDisplay.textContent = t("pairingUx.goToDeviceSetup");
    }
    syncEmptyActions();
    if (!setupInstallVirtualDisplay) return view.state;

    setupInstallVirtualDisplay.hidden = !localRequest
      || view.state === "installed"
      || view.state === "finishing"
      || view.state === "console-required";
    setupInstallVirtualDisplay.disabled = !view.canInstall || view.state === "installing";
    setupInstallVirtualDisplay.textContent = view.state === "installing"
      ? "正在建立…"
      : view.state === "failed"
        ? "再試一次"
        : view.state === "repair-ready"
          ? "修復虛擬螢幕"
          : view.state === "restart-required"
            ? "需要重新開機"
            : "建立虛擬螢幕";

    if (view.state === "installing") {
      setAvailability(false, "正在建立虛擬螢幕", "請在 Windows 管理員確認視窗按「是」，完成前不要關閉 VibeDeck。");
    } else if (view.state === "finishing" || view.state === "installed") {
      setAvailability(false, "正在完成虛擬螢幕", "驅動已安裝，正在等待 Windows 顯示新的延伸桌面。");
    } else if (view.state === "restart-required") {
      setAvailability(false, "需要重新開機", "Windows 必須重新開機才能完成虛擬螢幕安裝。");
    } else if (view.state === "console-required") {
      setAvailability(false, "請在本機 Windows 桌面啟動", "遠端桌面會把實體與虛擬顯示器換成 RDP 畫面，因此 VibeDeck 無法在這個工作階段接收延伸桌面。");
    } else if (view.state === "failed") {
      setAvailability(false, "虛擬螢幕沒有建立成功", "可以再試一次；原本的螢幕與資訊板不受影響。");
    }

    return view.state;
  }

  async function load() {
    try {
      const status = await fetchJsonOrThrow("/api/display/install/status");
      return { status, state: render(status) };
    } catch (error) {
      const message = tLegacy("無法取得安裝狀態。");
      const detail = error.message || "";
      applyFeedbackState(displayInstallDetail, { message, detail, state: "error" });
      applyFeedbackState(setupDisplayInstallDetail, { message, detail, state: "error" });
      return { status: null, state: "error" };
    }
  }

  function stopPolling() {
    clearTimeout(pollTimer);
    pollTimer = null;
  }

  function schedulePoll() {
    stopPolling();
    pollTimer = setTimeout(async () => {
      const { state } = await load();
      if (state === "installing" || state === "finishing" || state === "installed") {
        await reloadDisplays();
        if (!hasVibeDeckDisplay()) schedulePoll();
        else connectVideo();
      }
    }, 1500);
  }

  async function install() {
    if (!isLocalRequest() || !setupInstallVirtualDisplay) return;
    setupInstallVirtualDisplay.disabled = true;
    setupInstallVirtualDisplay.textContent = "等待 Windows 確認…";
    const waitingMessage = "請在這台 PC 跳出的管理員確認視窗按「是」。";
    applyFeedbackState(displayInstallDetail, { message: waitingMessage, state: "working" });
    applyFeedbackState(setupDisplayInstallDetail, { message: waitingMessage, state: "working" });

    try {
      render(await fetchJsonOrThrow("/api/display/install", { method: "POST" }));
      schedulePoll();
    } catch (error) {
      render({
        State: "failed",
        CanInstall: true,
        Message: error.message || "虛擬螢幕安裝失敗。",
      });
    }
  }

  return { render, load, install, schedulePoll, stopPolling };
}
