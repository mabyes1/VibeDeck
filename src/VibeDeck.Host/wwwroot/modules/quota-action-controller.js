export function normalizeQuotaAccountTarget(target = {}) {
  return {
    accountId: target?.accountId || "",
    email: target?.email || "",
  };
}

export function getQuotaActionMessage(result, fallback) {
  return result?.Message || result?.message || fallback;
}

export function quotaCardMatchesTarget(card, target = {}) {
  const accountId = String(target?.accountId || "").toLowerCase();
  const email = String(target?.email || "").toLowerCase();
  const cardEmail = String(card?.dataset?.accountEmail || "").toLowerCase();
  const cardId = String(card?.dataset?.accountId || "").toLowerCase();
  return email && cardEmail
    ? email === cardEmail
    : Boolean(accountId && cardId && accountId === cardId);
}

export function createQuotaActionController({
  document,
  window,
  t,
  tApi,
  tLegacy,
  applyFeedbackState,
  fetchJsonOrThrow,
  refreshQuotas,
  getQuotaSnapshot,
  quotaDataFingerprint,
  confirmAction,
  isLocalHost,
  startOAuthPolling,
}) {
  const statusByKey = new Map();

  function applyCardStatus(card) {
    if (!card) return;
    const status = card.querySelector(".quota-action-status");
    if (!status) return;
    const state = card.dataset.statusKey ? statusByKey.get(card.dataset.statusKey) : null;
    status.classList.add("feedback-status");
    applyFeedbackState(status, {
      message: state?.message || "",
      state: state?.level || "info",
      detail: state?.detail || "",
    });
  }

  function setCardStatus(card, message, level = "", detail = "") {
    if (!card) return;
    const statusKey = card.dataset.statusKey || "";
    const normalized = {
      message: message || "",
      level: level || "",
      detail: detail || "",
    };

    if (statusKey) {
      if (normalized.message) {
        statusByKey.set(statusKey, normalized);
      } else {
        statusByKey.delete(statusKey);
      }

      document.querySelectorAll(".quota-account-card").forEach(item => {
        if (item.dataset.statusKey === statusKey) applyCardStatus(item);
      });
      return;
    }

    const status = card.querySelector(".quota-action-status");
    if (!status) return;
    status.classList.add("feedback-status");
    applyFeedbackState(status, {
      message: normalized.message,
      state: normalized.level || "info",
      detail: normalized.detail,
    });
  }

  function setCardActionsDisabled(card, disabled) {
    if (!card) return;
    card.querySelectorAll(".quota-toolbox button").forEach(item => {
      if (disabled) {
        item.dataset.actionWasDisabled = item.disabled ? "1" : "0";
        item.disabled = true;
      } else if (item.dataset.actionWasDisabled !== undefined) {
        item.disabled = item.dataset.actionWasDisabled === "1";
        delete item.dataset.actionWasDisabled;
      }
    });
  }

  function getCurrentStatusCard(previousCard) {
    const statusKey = previousCard?.dataset?.statusKey;
    if (statusKey) {
      const matchingCard = Array.from(document.querySelectorAll(".quota-account-card"))
        .find(item => item.dataset.statusKey === statusKey);
      if (matchingCard) return matchingCard;
    }
    return document.querySelector(".quota-account-card") || previousCard;
  }

  async function runButton(button, action, options = {}) {
    if (!button) return null;
    const card = button.closest(".quota-account-card");
    const originalText = button.textContent;
    setCardStatus(card, options.pending || tLegacy("處理中..."), "working");
    setCardActionsDisabled(card, true);
    button.textContent = "…";
    try {
      const result = await action();
      const statusCard = getCurrentStatusCard(card);
      setCardStatus(statusCard, getQuotaActionMessage(result, options.success || tLegacy("完成。")), "success");
      return result;
    } catch (error) {
      const errorMessage = typeof options.error === "function"
        ? options.error(error)
        : options.error || tLegacy("額度操作失敗。");
      setCardStatus(
        getCurrentStatusCard(card),
        errorMessage,
        "error",
        error.message || ""
      );
      return null;
    } finally {
      button.textContent = originalText;
      setCardActionsDisabled(card, false);
    }
  }

  function postAccountTarget(url, target) {
    return fetchJsonOrThrow(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(normalizeQuotaAccountTarget(target)),
    });
  }

  function postCardAccount(url, card) {
    return postAccountTarget(url, {
      accountId: card.dataset.accountId || "",
      email: card.dataset.accountEmail || "",
    });
  }

  function findCodexCard(target) {
    return Array.from(document.querySelectorAll('.quota-account-card[data-quota-family="codex"]'))
      .find(card => quotaCardMatchesTarget(card, target)) || null;
  }

  async function reauthorizeCodex(button, target, statusCard) {
    if (!button || (!target?.accountId && !target?.email)) {
      setCardStatus(statusCard, t("ui.codexProfileRequired"), "error");
      return null;
    }

    const originalText = button.textContent;
    setCardStatus(statusCard, t("ui.codexQuotaReauthOpening"), "working");
    setCardActionsDisabled(statusCard, true);
    button.textContent = "…";
    button.setAttribute("aria-busy", "true");
    try {
      const started = await postAccountTarget("/api/quotas/codex/reauth", target);
      const sessionId = started.SessionId || started.sessionId;
      if (!sessionId) throw new Error(t("ui.codexQuotaReauthStartFailed"));
      setCardStatus(statusCard, t("ui.codexQuotaReauthWaiting"), "working");

      const expiresAt = Date.parse(started.ExpiresAt || started.expiresAt || "");
      const deadline = Number.isFinite(expiresAt) ? expiresAt + 5000 : Date.now() + (12 * 60 * 1000);
      let completed = null;
      while (Date.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, 1500));
        const polled = await fetchJsonOrThrow(
          `/api/quotas/codex/reauth/status?session=${encodeURIComponent(sessionId)}`,
          { method: "POST" }
        );
        const state = String(polled.State || polled.state || "pending").toLowerCase();
        if (state === "complete") {
          completed = polled;
          break;
        }
      }
      if (!completed) throw new Error(t("ui.codexQuotaReauthExpired"));

      setCardStatus(statusCard, t("ui.codexQuotaReauthRefreshing"), "working");
      await postAccountTarget("/api/quotas/codex/refresh", target);
      await refreshQuotas({ throwOnError: true });
      const currentCard = findCodexCard(target) || getCurrentStatusCard(statusCard);
      setCardStatus(currentCard, t("ui.codexQuotaReauthSuccess"), "success");
      return completed;
    } catch (error) {
      const currentCard = findCodexCard(target) || getCurrentStatusCard(statusCard);
      setCardStatus(
        currentCard,
        tApi(error?.code, error.message || t("ui.codexQuotaReauthFailed")),
        "error",
        error?.message || ""
      );
      return null;
    } finally {
      button.textContent = originalText;
      button.removeAttribute("aria-busy");
      setCardActionsDisabled(statusCard, false);
    }
  }

  function wireToolbox(card) {
    const refreshButton = card.querySelector('[data-quota-action="refresh"]');
    if (refreshButton) refreshButton.addEventListener("click", async () => {
      await runButton(refreshButton, async () => {
        if (card.dataset.quotaFamily === "codex") {
          const result = await postCardAccount("/api/quotas/codex/refresh", card);
          await refreshQuotas({ throwOnError: true });
          result.Message = tLegacy("額度已更新。");
          return result;
        }

        const previous = quotaDataFingerprint(getQuotaSnapshot());
        const snapshot = await refreshQuotas({ force: true, throwOnError: true });
        const unchanged = previous && previous === quotaDataFingerprint(snapshot);
        return {
          Message: unchanged
            ? tLegacy("已重新讀取，來源沒有新額度資料。")
            : tLegacy("額度已更新。"),
        };
      }, {
        pending: tLegacy("正在更新額度..."),
        success: tLegacy("額度已更新。"),
        error: error => card.dataset.quotaFamily === "codex"
          ? tApi(error?.code, tLegacy("無法更新這個 Codex 帳號。"))
          : tLegacy("額度操作失敗。"),
      });
    });

    const codexQuotaReauthButton = card.querySelector('[data-quota-action="codex-quota-reauth"]');
    if (codexQuotaReauthButton) codexQuotaReauthButton.addEventListener("click", async () => {
      await reauthorizeCodex(codexQuotaReauthButton, {
        accountId: card.dataset.accountId || "",
        email: card.dataset.accountEmail || "",
      }, card);
    });

    const cliButton = card.querySelector('[data-quota-action="agy-cli"]');
    if (cliButton) cliButton.addEventListener("click", async () => {
      await runButton(cliButton, async () => {
        const result = await fetchJsonOrThrow("/api/quotas/agy/cli/open", { method: "POST" });
        result.Message = t("ui.agyCliOpenSuccess");
        return result;
      }, {
        pending: t("ui.agyCliOpenPending"),
        success: t("ui.agyCliOpenSuccess"),
      });
    });

    const oauthButton = card.querySelector('[data-quota-action="agy-oauth"]');
    if (oauthButton) oauthButton.addEventListener("click", async () => {
      await runButton(oauthButton, async () => {
        const result = await fetchJsonOrThrow("/api/quotas/agy/oauth/start", { method: "POST" });
        const authUrl = result.AuthUrl || result.authUrl;
        const opened = result.Opened ?? result.opened;
        if (!opened && authUrl && isLocalHost()) {
          window.open(authUrl, "_blank", "noopener");
        }
        startOAuthPolling();
        return {
          message: opened ? t("ui.agyOauthOpened") : t("ui.agyOauthReady"),
        };
      }, {
        pending: t("ui.agyOauthPending"),
      });
    });

    const deleteButton = card.querySelector('[data-quota-action="agy-delete"]');
    if (deleteButton) deleteButton.addEventListener("click", async () => {
      const email = card.dataset.accountEmail || "this AGY account";
      if (!await confirmAction({
        title: t("ui.agyRemoveAccount"),
        message: t("ui.agyRemoveConfirm", { account: email }),
        confirmLabel: t("ui.agyRemoveAccount"),
        cancelLabel: tLegacy("取消"),
        tone: "danger",
      })) return;
      await runButton(deleteButton, async () => {
        const result = await postCardAccount("/api/quotas/agy/account/delete", card);
        await refreshQuotas({ force: true });
        return result;
      }, {
        pending: tLegacy("正在刪除 AGY 帳號..."),
        success: tLegacy("AGY 帳號已刪除。"),
      });
    });

    const codexDeleteButton = card.querySelector('[data-quota-action="codex-delete"]');
    if (codexDeleteButton) codexDeleteButton.addEventListener("click", async () => {
      const label = card.dataset.accountEmail || "this Codex profile";
      if (!await confirmAction({
        title: t("ui.codexCacheDelete"),
        message: t("ui.codexCacheDeleteConfirm", { account: label }),
        confirmLabel: t("ui.codexCacheDelete"),
        cancelLabel: tLegacy("取消"),
        tone: "danger",
      })) return;
      await runButton(codexDeleteButton, async () => {
        const result = await postCardAccount("/api/quotas/codex/account/delete", card);
        await refreshQuotas({ force: true });
        return result;
      }, {
        pending: tLegacy("正在刪除 Codex Profile..."),
        success: tLegacy("Codex Profile 已刪除。"),
      });
    });

    const claudeDeleteButton = card.querySelector('[data-quota-action="claude-delete"]');
    if (claudeDeleteButton) claudeDeleteButton.addEventListener("click", async () => {
      const label = card.dataset.accountEmail || "this Claude account";
      if (!await confirmAction({
        title: tLegacy("移除 Claude 帳號"),
        message: tLegacy(`要從 VibeDeck 額度清單移除 ${label} 嗎？不會刪除 Claude Code 本身的登入資料。`),
        confirmLabel: tLegacy("移除"),
        cancelLabel: tLegacy("取消"),
        tone: "danger",
      })) return;
      await runButton(claudeDeleteButton, async () => {
        const result = await postCardAccount("/api/quotas/claude/account/delete", card);
        await refreshQuotas({ force: true });
        return result;
      }, {
        pending: tLegacy("正在移除 Claude 帳號..."),
        success: tLegacy("Claude 帳號已從 VibeDeck 移除。"),
      });
    });
  }

  return {
    applyCardStatus,
    setCardStatus,
    runButton,
    reauthorizeCodex,
    wireToolbox,
  };
}
