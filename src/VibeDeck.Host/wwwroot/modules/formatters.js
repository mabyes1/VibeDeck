import { getIntlLocale, getLocale, tLegacy } from "./i18n.js?v=4";

export function formatPercent(value) {
  return Number.isFinite(value) ? `${Math.round(value)}%` : "--";
}

export function formatGb(value) {
  return Number.isFinite(value)
    ? `${value.toLocaleString(getIntlLocale(), { minimumFractionDigits: value >= 10 ? 0 : 1, maximumFractionDigits: value >= 10 ? 0 : 1 })}GB`
    : "--";
}

export function formatFileSize(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return "";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  const digits = value >= 10 || unitIndex === 0 ? 0 : 1;
  return `${value.toLocaleString(getIntlLocale(), { minimumFractionDigits: digits, maximumFractionDigits: digits })}${units[unitIndex]}`;
}

export function formatMbps(value) {
  return Number.isFinite(value) ? `${value.toFixed(value >= 10 ? 0 : 1)}` : "--";
}

export function formatTemperature(value) {
  return Number.isFinite(value) ? `${Math.round(value)}°C` : "N/A";
}

export function describeWeatherCode(code, fallback) {
  const value = Number(code);
  if (!Number.isFinite(value)) return fallback || "";
  if (value === 0) return tLegacy("晴朗");
  if (value === 1) return tLegacy("大致晴朗");
  if (value === 2) return tLegacy("局部多雲");
  if (value === 3) return tLegacy("多雲");
  if (value === 45 || value === 48) return tLegacy("有霧");
  if ([51, 53, 55].includes(value)) return tLegacy("毛毛雨");
  if ([56, 57].includes(value)) return tLegacy("凍毛毛雨");
  if ([61, 63, 65].includes(value)) return tLegacy("下雨");
  if ([66, 67].includes(value)) return tLegacy("凍雨");
  if ([71, 73, 75, 77].includes(value)) return tLegacy("下雪");
  if ([80, 81, 82].includes(value)) return tLegacy("陣雨");
  if ([85, 86].includes(value)) return tLegacy("陣雪");
  if (value === 95) return tLegacy("雷雨");
  if (value === 96 || value === 99) return tLegacy("雷雨冰雹");
  return fallback || "";
}

export function formatWeatherLocation(location) {
  const text = String(location || "").trim();
  if (!text || /^weather$/i.test(text)) return tLegacy("天氣");
  if (/^current location$/i.test(text)) return tLegacy("目前位置");
  const locale = getLocale();
  const country = locale === "ja" ? "台湾" : locale === "en" ? "Taiwan" : "台灣";
  const district = locale === "ja" ? "区" : locale === "en" ? "District" : "區";
  return text
    .replace(/\bTaiwan\b/i, country)
    .replace(/\bDistrict\b/i, district)
    .replace(/,\s*/g, locale === "en" ? ", " : "，");
}

export function formatSeconds(value) {
  if (!Number.isFinite(value)) return "--";
  const hours = Math.floor(value / 3600);
  const minutes = Math.floor((value % 3600) / 60);
  if (getLocale() === "ja") {
    if (hours >= 24) return `${Math.floor(hours / 24)}日 ${hours % 24}時間`;
    return `${hours}時間 ${minutes}分`;
  }
  if (hours >= 24) {
    return `${Math.floor(hours / 24)}d ${hours % 24}h`;
  }
  return getLocale() === "en" ? `${hours}h ${minutes}m` : `${hours}h ${minutes}m`;
}

export function averagePercent(values) {
  const valid = values.filter(Number.isFinite);
  if (!valid.length) return Number.NaN;
  return valid.reduce((sum, value) => sum + value, 0) / valid.length;
}
