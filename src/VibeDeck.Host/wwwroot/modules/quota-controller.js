import { tLegacy } from "./i18n.js?v=4";

export function createQuotaController({
  elements,
  getActiveMode,
  fetchJsonOrThrow,
  isTrustRequiredError,
  renderSnapshot,
  renderErrorHelp,
  onConnectionChange,
}) {
  let oauthPollTimer = null;

  function renderFailure(error, requiresTrust) {
    const card = document.createElement("article");
    card.className = "quota-account-card quota-setup-card quota-error-card";
    if (error?.message) card.title = error.message;

    const title = document.createElement("div");
    title.className = "quota-setup-title";
    title.textContent = tLegacy("無法讀取 AI 額度");

    const list = document.createElement("ul");
    list.className = "quota-setup-list";
    const lines = requiresTrust
      ? [tLegacy("完成手機配對後即可查看這台電腦的 AI 額度。")]
      : [
          tLegacy("VibeDeck 暫時無法從這台電腦取得額度資料。"),
          tLegacy("請確認 Host 正常執行，再重新嘗試。"),
        ];
    for (const line of lines) {
      const item = document.createElement("li");
      item.textContent = line;
      list.append(item);
    }

    const footer = document.createElement("div");
    footer.className = "quota-footer";
    const spacer = document.createElement("span");
    const toolbox = document.createElement("div");
    toolbox.className = "quota-toolbox";
    const retry = document.createElement("button");
    retry.type = "button";
    retry.textContent = tLegacy("重新嘗試");
    retry.addEventListener("click", () => {
      retry.disabled = true;
      retry.textContent = tLegacy("正在讀取 AI 額度…");
      void refresh({ force: true });
    });
    toolbox.append(retry);
    footer.append(spacer, toolbox);
    card.append(title, list, footer);
    elements.quotaGrid.replaceChildren(card);
  }

  async function refresh(options = {}) {
    const quotaVisible = getActiveMode() === "quota";
    if (!quotaVisible && !options.background) return null;

    if (quotaVisible && !elements.quotaGrid.childElementCount) {
      elements.quotaSummary.textContent = tLegacy("正在讀取 AI 額度…");
      elements.quotaUpdated.textContent = "";
    }

    try {
      const endpoint = options.force ? "/api/quotas/refresh" : "/api/quotas";
      const init = options.force ? { method: "POST" } : undefined;
      const snapshot = await fetchJsonOrThrow(endpoint, init);
      renderSnapshot(snapshot);
      onConnectionChange?.("online");
      return snapshot;
    } catch (error) {
      onConnectionChange?.("offline");
      const requiresTrust = isTrustRequiredError(error);
      if (quotaVisible) {
        elements.quotaSummary.textContent = requiresTrust
          ? tLegacy("請先配對手機，才能查看 AI 額度。")
          : tLegacy("無法讀取 AI 額度");
        elements.quotaUpdated.textContent = "";
        if (elements.quotaHelp) {
          elements.quotaHelp.replaceChildren();
          elements.quotaHelp.append(renderErrorHelp(error, requiresTrust));
        }
        renderFailure(error, requiresTrust);
      }
      if (options.throwOnError) throw error;
      return null;
    }
  }

  function startOAuthPolling() {
    if (oauthPollTimer) clearInterval(oauthPollTimer);

    let attempts = 0;
    oauthPollTimer = setInterval(async () => {
      attempts += 1;
      if (getActiveMode() !== "quota" || attempts > 40) {
        clearInterval(oauthPollTimer);
        oauthPollTimer = null;
        return;
      }

      await refresh({ force: true });
    }, 3000);
  }

  function stopOAuthPolling() {
    if (!oauthPollTimer) return;
    clearInterval(oauthPollTimer);
    oauthPollTimer = null;
  }

  return {
    refresh,
    startOAuthPolling,
    stopOAuthPolling,
  };
}
