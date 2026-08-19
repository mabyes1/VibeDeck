import { getIntlLocale, t, tLegacy } from "./i18n.js?v=4";

const MINUTES_PER_HOUR = 60;
const MINUTES_PER_DAY = 24 * MINUTES_PER_HOUR;
const MINUTES_PER_WEEK = 7 * MINUTES_PER_DAY;

export function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

export function formatQuotaWindowLabel(windowData = {}, fallbackLabel = "") {
  const minutes = Number(windowData?.WindowMinutes ?? windowData?.windowMinutes);
  if (!Number.isFinite(minutes) || minutes <= 0) return fallbackLabel;

  const roundedMinutes = Math.round(minutes);
  if (roundedMinutes % MINUTES_PER_WEEK === 0) {
    const count = roundedMinutes / MINUTES_PER_WEEK;
    const translated = t("ui.quotaWindowWeeks", { count });
    if (translated !== "ui.quotaWindowWeeks") return translated;
    const locale = getIntlLocale().toLowerCase();
    return locale.startsWith("en") ? `${count} wk` : locale.startsWith("ja") ? `${count}週間` : `${count} 週`;
  }
  if (roundedMinutes % MINUTES_PER_DAY === 0) {
    const count = roundedMinutes / MINUTES_PER_DAY;
    const translated = t("ui.quotaWindowDays", { count });
    if (translated !== "ui.quotaWindowDays") return translated;
    const locale = getIntlLocale().toLowerCase();
    return locale.startsWith("en") ? `${count} d` : locale.startsWith("ja") ? `${count}日` : `${count} 天`;
  }
  if (roundedMinutes % MINUTES_PER_HOUR === 0) {
    const count = roundedMinutes / MINUTES_PER_HOUR;
    const translated = t("ui.quotaWindowHours", { count });
    if (translated !== "ui.quotaWindowHours") return translated;
    const locale = getIntlLocale().toLowerCase();
    return locale.startsWith("en") ? `${count} h` : locale.startsWith("ja") ? `${count}時間` : `${count} 小時`;
  }
  const translated = t("ui.quotaWindowMinutes", { count: roundedMinutes });
  if (translated !== "ui.quotaWindowMinutes") return translated;
  const locale = getIntlLocale().toLowerCase();
  return locale.startsWith("en") ? `${roundedMinutes} min` : locale.startsWith("ja") ? `${roundedMinutes}分` : `${roundedMinutes} 分鐘`;
}

export function renderQuotaWindow(label, windowData = {}) {
  const used = windowData.UsedPercent ?? windowData.usedPercent;
  const providedRemaining = windowData.RemainingPercent ?? windowData.remainingPercent;
  const remaining = Number.isFinite(providedRemaining)
    ? Math.max(0, Math.min(100, providedRemaining))
    : Number.isFinite(used)
      ? Math.max(0, Math.min(100, 100 - used))
      : Number.NaN;
  const reset = windowData.ResetsAt || windowData.resetsAt;
  const resetText = reset
    ? `${tLegacy("重置")} ${new Date(reset).toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit" })}`
    : `${tLegacy("重置")} --`;
  const value = Number.isFinite(remaining) ? `${Math.round(remaining)}%` : "--";
  const bar = Number.isFinite(remaining) ? remaining : 0;
  return `
    <div class="quota-window">
      <span>${escapeHtml(label)}</span>
      <b>${value}</b>
      <div class="quota-bar"><i style="width:${bar}%"></i></div>
      <small>${resetText}</small>
    </div>
  `;
}

export function summarizeQuotaWindow(windowData = {}) {
  const used = windowData.UsedPercent ?? windowData.usedPercent;
  const providedRemaining = windowData.RemainingPercent ?? windowData.remainingPercent;
  const remaining = Number.isFinite(providedRemaining)
    ? providedRemaining
    : Number.isFinite(used)
      ? 100 - used
      : Number.NaN;
  return Number.isFinite(remaining) ? `${Math.round(remaining)}%` : "--";
}

export function extractQuotaEmail(providers = []) {
  for (const provider of providers) {
    const detail = provider.Detail || provider.detail || "";
    const match = detail.match(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i);
    if (match) return match[0];
  }
  return null;
}

export function extractQuotaTier(providers = []) {
  const tierPattern = /^(?:free|pro|plus|ultra|max|team|business|enterprise)$/i;
  for (const provider of providers) {
    const detail = provider.Detail || provider.detail || "";
    const parts = detail.split("·").map(part => part.trim()).filter(Boolean);
    const tier = parts.find(part => tierPattern.test(part));
    if (tier) return tier;
  }
  return null;
}

export function normalizeTierLabel(tier) {
  const value = String(tier || "").toUpperCase();
  if (value.includes("PRO")) return "PRO";
  if (value.includes("ULTRA")) return "ULTRA";
  if (value.includes("FREE")) return "FREE";
  return value || "PRO";
}
