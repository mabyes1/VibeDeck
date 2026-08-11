export function buildTurnSettingsView(settings = {}) {
  const configured = Boolean(settings.configured ?? settings.Configured);
  const keyId = String(settings.keyId || settings.KeyId || "");
  const updatedAt = String(settings.updatedAt || settings.UpdatedAt || "");
  return { configured, keyId, updatedAt };
}

export function buildTurnDiagnosticsText(snapshot = {}) {
  const sessions = snapshot.webRtc?.activeSessions || snapshot.WebRtc?.ActiveSessions || [];
  if (!Array.isArray(sessions) || sessions.length === 0) {
    return "目前沒有活動中的 Host WebRTC 連線。從外部手機打開顯示器後，連線狀態會顯示於此。";
  }
  const session = sessions[0] || {};
  const state = session.connectionState || session.ConnectionState || "unknown";
  const ice = session.iceState || session.IceState || "unknown";
  const plan = session.transportPlan || session.TransportPlan || "direct-stun";
  return `Host WebRTC：${state} · ICE ${ice} · ${plan}`;
}

export function createTurnSettingsController({
  elements,
  t,
  tLegacy,
  fetchJsonOrThrow,
  confirmAction,
  isLocalRequest,
}) {
  const {
    turnKeyId,
    turnApiToken,
    turnSettingsStatus,
    turnDiagnosticsStatus,
    saveTurnSettings,
    testTurnSettings,
    clearTurnSettings,
  } = elements;

  function renderSettings(settings = {}) {
    const view = buildTurnSettingsView(settings);
    if (turnKeyId) {
      turnKeyId.value = "";
      turnKeyId.placeholder = view.configured && view.keyId ? `已設定：${view.keyId}` : "Cloudflare TURN Key ID";
    }
    if (turnApiToken) turnApiToken.value = "";
    if (turnSettingsStatus) {
      turnSettingsStatus.textContent = view.configured
        ? `TURN 已設定（${view.keyId || "Key 已隱藏"}）。API Token 已使用 Windows 加密保存${view.updatedAt ? ` · ${new Date(view.updatedAt).toLocaleString()}` : ""}。`
        : "未設定：維持 STUN 直連與 JPEG 備援。";
    }
    if (clearTurnSettings) clearTurnSettings.disabled = !view.configured;
    if (testTurnSettings) testTurnSettings.disabled = !view.configured;
  }

  function renderDiagnostics(snapshot = {}) {
    if (turnDiagnosticsStatus) turnDiagnosticsStatus.textContent = buildTurnDiagnosticsText(snapshot);
  }

  async function load() {
    if (!isLocalRequest()) return;
    const [settings, diagnostics] = await Promise.all([
      fetchJsonOrThrow("/api/stream/turn/settings"),
      fetchJsonOrThrow("/api/stream/diagnostics"),
    ]);
    renderSettings(settings);
    renderDiagnostics(diagnostics);
  }

  async function save() {
    const keyId = turnKeyId?.value?.trim() || "";
    const apiToken = turnApiToken?.value?.trim() || "";
    if (!keyId || !apiToken) {
      if (turnSettingsStatus) turnSettingsStatus.textContent = "請同時貼上 Cloudflare TURN Key ID 與 API Token。";
      return;
    }
    saveTurnSettings.disabled = true;
    try {
      const settings = await fetchJsonOrThrow("/api/stream/turn/settings", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ keyId, apiToken }),
      });
      renderSettings(settings);
      if (turnSettingsStatus) turnSettingsStatus.textContent += ` ${tLegacy("可以按「測試憑證」確認 Cloudflare 設定。")}`;
    } catch (error) {
      if (turnSettingsStatus) turnSettingsStatus.textContent = error.message || "TURN 設定儲存失敗。";
    } finally {
      saveTurnSettings.disabled = false;
    }
  }

  async function test() {
    if (!testTurnSettings) return;
    testTurnSettings.disabled = true;
    const oldText = testTurnSettings.textContent;
    testTurnSettings.textContent = "測試中…";
    try {
      const result = await fetchJsonOrThrow("/api/stream/turn/test", { method: "POST" });
      const urls = Array.isArray(result.urls || result.Urls) ? (result.urls || result.Urls) : [];
      if (turnSettingsStatus) turnSettingsStatus.textContent = `TURN 憑證已取得。候選：${urls.join(" · ") || "已就緒"}`;
    } catch (error) {
      if (turnSettingsStatus) turnSettingsStatus.textContent = error.message || "TURN 憑證測試失敗。";
    } finally {
      testTurnSettings.disabled = false;
      testTurnSettings.textContent = oldText;
    }
  }

  async function clear() {
    if (!await confirmAction({
      title: t("ui.turnRemove"),
      message: t("ui.turnRemoveConfirm"),
      confirmLabel: t("ui.turnRemove"),
      cancelLabel: tLegacy("取消"),
      tone: "danger",
    })) return;
    try {
      renderSettings(await fetchJsonOrThrow("/api/stream/turn/settings", { method: "DELETE" }));
      renderDiagnostics({});
    } catch (error) {
      if (turnSettingsStatus) turnSettingsStatus.textContent = error.message || "TURN 設定移除失敗。";
    }
  }

  function showLoadError(error) {
    if (turnSettingsStatus) turnSettingsStatus.textContent = error?.message || "無法讀取 TURN 設定。";
  }

  return { renderSettings, renderDiagnostics, load, save, test, clear, showLoadError };
}
