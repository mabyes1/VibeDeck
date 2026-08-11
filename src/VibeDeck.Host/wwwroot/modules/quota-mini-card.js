import { getIntlLocale, t, tLegacy } from "./i18n.js?v=4";
import { formatQuotaWindowLabel } from "./quota-formatters.js?v=51";
import { sortQuotaAccountsByRecentUse } from "./quota-account-navigation.js?v=1";

const SOURCE_STORAGE_KEY = "vibeDeckDashboardQuotaSource.v1";

export function quotaMiniFamilyOf(provider) {
  const family = String(provider?.Family || provider?.family || "").toLowerCase();
  if (family) return family;
  const id = String(provider?.Id || provider?.id || "").toLowerCase();
  return id.startsWith("agy")
    ? "agy"
    : id.startsWith("codex")
      ? "codex"
      : id.startsWith("claude")
        ? "claude-code"
        : id;
}

export function resolveQuotaMiniDefaultProvider(providers = []) {
  const sorted = sortQuotaAccountsByRecentUse(providers);
  return sorted.find(provider =>
    quotaMiniFamilyOf(provider) === "codex" && Boolean(provider?.IsActive ?? provider?.isActive))
    || sorted.find(provider => Boolean(provider?.IsActive ?? provider?.isActive))
    || sorted[0]
    || null;
}

export function createQuotaMiniCardController({ elements, fetchJsonOrThrow }) {
  const { select, value, bar, reset, state, credits } = elements;
  let snapshot = null;
  let selectedKey = "";
  let manuallySelected = false;

  // The previous mini card persisted its source indefinitely. That meant an
  // old account could keep winning after the active Codex login changed.
  // Default to active Codex for each page session; explicit choices remain
  // sticky only until this page is reloaded.
  try { localStorage.removeItem(SOURCE_STORAGE_KEY); } catch {}

  function familyOf(provider) {
    return quotaMiniFamilyOf(provider);
  }

  function providerKey(provider) {
    return [
      familyOf(provider),
      provider?.AccountId || provider?.accountId || provider?.AccountEmail || provider?.accountEmail || "",
      provider?.Id || provider?.id || "",
    ].join(":");
  }

  function providerLabel(provider) {
    const familyId = familyOf(provider);
    const family = familyId === "codex"
      ? "CODEX"
      : familyId === "claude-code"
        ? "CLAUDE"
        : familyId.toUpperCase() || "AI";
    const providerName = String(provider?.Label || provider?.label || "").trim();
    const email = String(provider?.AccountEmail || provider?.accountEmail || "").trim();
    const details = [family];
    if (family === "AGY" && providerName) details.push(providerName);
    if (email) details.push(email);
    return details.join(" · ");
  }

  function remainingPercent(windowData) {
    const remaining = windowData?.RemainingPercent ?? windowData?.remainingPercent;
    const used = windowData?.UsedPercent ?? windowData?.usedPercent;
    if (Number.isFinite(remaining)) return Math.max(0, Math.min(100, remaining));
    if (Number.isFinite(used)) return Math.max(0, Math.min(100, 100 - used));
    return Number.NaN;
  }

  function formatCodexCreditBalance(provider) {
    const unlimited = provider?.CreditUnlimited ?? provider?.creditUnlimited;
    if (unlimited === true || String(unlimited).toLowerCase() === "true") return "∞";
    const balance = Number(provider?.CreditBalance ?? provider?.creditBalance);
    return Number.isFinite(balance)
      ? new Intl.NumberFormat(getIntlLocale(), { maximumFractionDigits: 2 }).format(balance)
      : "";
  }

  function render() {
    const providers = snapshot?.Providers || snapshot?.providers || [];
    const sorted = sortQuotaAccountsByRecentUse(providers);
    select.replaceChildren();
    for (const provider of sorted) {
      const option = document.createElement("option");
      option.value = providerKey(provider);
      option.textContent = providerLabel(provider);
      select.append(option);
    }

    const selectedStillExists = sorted.some(provider => providerKey(provider) === selectedKey);
    if (!manuallySelected || !selectedStillExists) {
      if (!selectedStillExists) manuallySelected = false;
      selectedKey = providerKey(resolveQuotaMiniDefaultProvider(sorted) || {});
    }
    select.value = selectedKey;
    const selected = sorted.find(provider => providerKey(provider) === selectedKey);
    const primary = selected?.Primary || selected?.primary || null;
    const remaining = remainingPercent(primary);
    const windowLabel = formatQuotaWindowLabel(primary, tLegacy("5 小時額度"));
    const creditBalance = familyOf(selected) === "codex" ? formatCodexCreditBalance(selected) : "";
    value.textContent = Number.isFinite(remaining) ? `${Math.round(remaining)}%` : "--";
    bar.style.width = `${Number.isFinite(remaining) ? remaining : 0}%`;
    credits.hidden = !creditBalance;
    credits.textContent = creditBalance ? `${tLegacy("剩餘 ChatGPT Credits：")} ${creditBalance}` : "";
    const resetsAt = primary?.ResetsAt || primary?.resetsAt;
    reset.textContent = resetsAt
      ? `${tLegacy("重置")} ${new Date(resetsAt).toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit" })}`
      : selected ? t("ui.noQuotaWindowData") : tLegacy("尚無額度來源");
    state.textContent = selected ? t("ui.quotaWindowRemaining", { period: windowLabel }) : tLegacy("等待來源");
    select.disabled = !sorted.length;
  }

  select.addEventListener("change", () => {
    selectedKey = select.value;
    manuallySelected = true;
    render();
  });

  return {
    renderSnapshot(nextSnapshot) {
      snapshot = nextSnapshot || {};
      render();
    },
    async refresh() {
      try {
        const nextSnapshot = await fetchJsonOrThrow("/api/quotas");
        snapshot = nextSnapshot || {};
        render();
        return nextSnapshot;
      } catch (error) {
        value.textContent = "--";
        bar.style.width = "0%";
        credits.hidden = true;
        credits.textContent = "";
        reset.textContent = error?.message || tLegacy("額度讀取失敗");
        state.textContent = tLegacy("來源離線");
        return null;
      }
    },
  };
}
