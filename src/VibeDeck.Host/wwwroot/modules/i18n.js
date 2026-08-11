const SUPPORTED_LOCALES = ["zh-Hant", "en", "ja"];
const LOCALE_STORAGE_KEY = "vibedeckLocale";
const sourceTextByNode = new WeakMap();
const sourceAttrByElement = new WeakMap();
const listeners = new Set();

let currentLocale = "zh-Hant";
let currentCatalog = null;
let sourceCatalog = null;
let patterns = [];
let legacyFragments = [];
let catalogTextMap = new Map();
let observer = null;
let selectorBound = false;

function resolve(catalog, key) {
  if (!catalog || !key) return undefined;
  return String(key).split(".").reduce((value, part) => value?.[part], catalog);
}

function interpolate(value, values = {}) {
  return String(value).replace(/\{([\w.-]+)\}/g, (_, key) => {
    const result = resolve(values, key);
    return result == null ? `{${key}}` : String(result);
  });
}

function preserveWhitespace(original, translated) {
  const leading = original.match(/^\s*/)?.[0] || "";
  const trailing = original.match(/\s*$/)?.[0] || "";
  return `${leading}${translated}${trailing}`;
}

function escapeRegex(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function compilePatterns(catalog) {
  const sourcePatterns = sourceCatalog?.patterns || {};
  const translatedPatterns = catalog?.patterns || {};
  return Object.entries(sourcePatterns).map(([template, fallback]) => {
    const variables = [];
    const expression = String(template)
      .split(/(\{[\w.-]+\})/g)
      .map(part => {
        const match = /^\{([\w.-]+)\}$/.exec(part);
        if (!match) return escapeRegex(part);
        variables.push(match[1]);
        return "([\\s\\S]*?)";
      })
      .join("");
    return {
      regex: new RegExp(`^${expression}$`),
      variables,
      template,
      translated: translatedPatterns[template] || fallback || template,
    };
  });
}

function collectCatalogText(source, translated, result) {
  if (!source || !translated || typeof source !== "object" || typeof translated !== "object") return;
  for (const [key, sourceValue] of Object.entries(source)) {
    const translatedValue = translated[key];
    if (typeof sourceValue === "string" && typeof translatedValue === "string") {
      result.set(sourceValue, translatedValue);
    } else {
      collectCatalogText(sourceValue, translatedValue, result);
    }
  }
}

function rebuildTranslationIndexes() {
  patterns = compilePatterns(currentCatalog);
  catalogTextMap = new Map();
  // ui contains declarative static strings that do not need duplicate legacy entries.
  collectCatalogText(sourceCatalog?.ui, currentCatalog?.ui, catalogTextMap);
  legacyFragments = [
    ...Object.entries(currentCatalog?.legacy || {}),
    ...catalogTextMap.entries(),
  ]
    .filter(([from, to]) => from && to && from !== to && !from.includes("{"))
    .sort((left, right) => right[0].length - left[0].length);
}

export function normalizeLocale(value) {
  const raw = String(value || "").trim().toLowerCase();
  if (raw === "en" || raw.startsWith("en-")) return "en";
  if (raw === "ja" || raw.startsWith("ja-")) return "ja";
  if (raw === "zh-hant" || raw === "zh-tw" || raw === "zh-hk" || raw === "zh-mo") return "zh-Hant";
  return "";
}

function localeFromBrowser() {
  const languages = Array.isArray(navigator.languages) && navigator.languages.length
    ? navigator.languages
    : [navigator.language];
  for (const value of languages) {
    const normalized = normalizeLocale(value);
    if (normalized) return normalized;
  }
  return "zh-Hant";
}

function requestedLocale() {
  const query = normalizeLocale(new URLSearchParams(location.search).get("lang"));
  if (query) return query;
  try {
    const stored = normalizeLocale(localStorage.getItem(LOCALE_STORAGE_KEY));
    if (stored) return stored;
  } catch {
    // Storage may be blocked in a private browsing context.
  }
  return localeFromBrowser();
}

async function loadCatalog(locale) {
  const response = await fetch(`/locales/${encodeURIComponent(locale)}.json?v=3`, { cache: "no-store" });
  if (!response.ok) throw new Error(`Locale ${locale} failed to load (${response.status})`);
  return response.json();
}

function setStoredLocale(locale) {
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, locale);
  } catch {
    // Ignore storage errors; the current page still uses the selected locale.
  }
}

function updateLocaleSelector() {
  const selector = document.getElementById("localeSelect");
  if (!selector) return;
  selector.value = currentLocale;
  selector.setAttribute("aria-label", t("ui.language"));
  if (!selectorBound) {
    selectorBound = true;
    selector.addEventListener("change", () => {
      const normalized = normalizeLocale(selector.value) || "zh-Hant";
      setStoredLocale(normalized);
      const url = new URL(location.href);
      url.searchParams.set("lang", normalized);
      location.assign(url.toString());
    });
  }
}

function updateLocalizedCss() {
  const root = document.documentElement;
  root.style.setProperty("--i18n-portrait-hint", JSON.stringify(translateText("直式適合看資訊板 / 額度；副螢幕建議橫向全螢幕。")));
  root.style.setProperty("--i18n-touch-hint", JSON.stringify(translateText("單指操作 PC 滑鼠 · 連點兩下放大畫面")));
}

function updateManifestLink() {
  const link = document.getElementById("manifestLink");
  if (!link) return;
  const target = currentLocale === "en" ? "/manifest.en.json" : currentLocale === "ja" ? "/manifest.ja.json" : "/manifest.json";
  link.href = `${target}?v=1`;
}

function patternTranslation(source) {
  for (const pattern of patterns) {
    const match = pattern.regex.exec(source);
    if (!match) continue;
    const values = Object.fromEntries(pattern.variables.map((name, index) => [name, match[index + 1]]));
    return interpolate(pattern.translated, values);
  }
  return "";
}

function fragmentTranslation(source) {
  if (currentLocale === "zh-Hant" || !/[\u3400-\u9fff]/.test(source)) return "";
  let result = source;
  for (const [from, to] of legacyFragments) {
    if (!from || from.length < 2 || from === to || !result.includes(from)) continue;
    result = result.split(from).join(to);
  }
  return result === source ? "" : result;
}

export function translateText(value) {
  if (value == null || typeof value !== "string") return value;
  const trimmed = value.trim();
  if (!trimmed) return value;
  const mapped = currentCatalog?.legacy?.[trimmed] ?? catalogTextMap.get(trimmed);
  if (mapped != null) return preserveWhitespace(value, mapped);
  const patterned = patternTranslation(trimmed);
  if (patterned) return preserveWhitespace(value, patterned);
  const fragmented = fragmentTranslation(trimmed);
  return fragmented ? preserveWhitespace(value, fragmented) : value;
}

export function t(key, values = {}) {
  const value = resolve(currentCatalog, key) ?? resolve(sourceCatalog, key);
  if (value == null) return translateText(String(key));
  return interpolate(value, values);
}

export function tPlural(key, count, values = {}) {
  const value = resolve(currentCatalog, key) ?? resolve(sourceCatalog, key);
  if (!value || typeof value !== "object") return t(key, { ...values, count });
  const branch = currentLocale === "en" && Number(count) === 1 ? "one" : "other";
  const template = value[branch] ?? value.other ?? value.one;
  return template == null ? t(key, { ...values, count }) : interpolate(template, { ...values, count });
}

export function tLegacy(source) {
  return translateText(source);
}

export function tApi(code, fallback = "") {
  const key = String(code || "").trim();
  const value = key ? resolve(currentCatalog, `api.${key}`) ?? resolve(sourceCatalog, `api.${key}`) : null;
  return typeof value === "string" ? value : translateText(fallback);
}

export function getLocale() {
  return currentLocale;
}

export function getIntlLocale() {
  return currentLocale === "zh-Hant" ? "zh-TW" : currentLocale;
}

export function onLocaleChange(listener) {
  if (typeof listener !== "function") return () => {};
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function isIgnored(node) {
  const element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
  return Boolean(element?.closest?.("[data-i18n-ignore], [data-user-content], script, style, code, pre"));
}

function translateTextNode(node) {
  if (!node || node.nodeType !== Node.TEXT_NODE || isIgnored(node)) return;
  if (!sourceTextByNode.has(node)) sourceTextByNode.set(node, node.nodeValue || "");
  const source = sourceTextByNode.get(node);
  const translated = translateText(source);
  if (node.nodeValue !== translated) node.nodeValue = translated;
}

function translateAttributes(element) {
  if (!element || element.nodeType !== Node.ELEMENT_NODE) return;
  const key = element.getAttribute("data-i18n");
  if (key) {
    element.textContent = t(key);
  }

  const attrSpec = element.getAttribute("data-i18n-attr");
  if (attrSpec) {
    for (const entry of attrSpec.split(";").map(value => value.trim()).filter(Boolean)) {
      const separator = entry.indexOf(":");
      if (separator <= 0) continue;
      const attr = entry.slice(0, separator).trim();
      const attrKey = entry.slice(separator + 1).trim();
      element.setAttribute(attr, t(attrKey));
    }
  }

  const translatableAttrs = ["title", "aria-label", "aria-description", "alt", "placeholder", "data-dashboard-title"];
  let originalAttrs = sourceAttrByElement.get(element);
  if (!originalAttrs) {
    originalAttrs = new Map();
    sourceAttrByElement.set(element, originalAttrs);
  }
  for (const attr of translatableAttrs) {
    if (!element.hasAttribute(attr) || attrSpec?.includes(`${attr}:`)) continue;
    if (!originalAttrs.has(attr)) originalAttrs.set(attr, element.getAttribute(attr));
    const source = originalAttrs.get(attr);
    const translated = translateText(source);
    if (translated != null && element.getAttribute(attr) !== translated) element.setAttribute(attr, translated);
  }
}

function walk(root) {
  if (!root) return;
  if (root.nodeType === Node.TEXT_NODE) {
    translateTextNode(root);
    return;
  }
  if (root.nodeType !== Node.ELEMENT_NODE && root.nodeType !== Node.DOCUMENT_NODE) return;
  translateAttributes(root);
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  let current;
  while ((current = walker.nextNode())) {
    if (current.nodeType === Node.TEXT_NODE) translateTextNode(current);
    else translateAttributes(current);
  }
}

export function applyTranslations(root = document) {
  walk(root);
  updateLocaleSelector();
  updateLocalizedCss();
}

function observe() {
  if (observer || !document.body) return;
  observer = new MutationObserver(records => {
    for (const record of records) {
      for (const node of record.addedNodes) walk(node);
    }
  });
  observer.observe(document.body, { childList: true, subtree: true });
}

export async function initLocale() {
  const selected = requestedLocale();
  sourceCatalog = sourceCatalog || await loadCatalog("zh-Hant");
  try {
    currentCatalog = selected === "zh-Hant" ? sourceCatalog : await loadCatalog(selected);
    currentLocale = SUPPORTED_LOCALES.includes(selected) ? selected : "zh-Hant";
  } catch {
    currentCatalog = sourceCatalog;
    currentLocale = "zh-Hant";
  }
  rebuildTranslationIndexes();
  document.documentElement.lang = currentCatalog?.meta?.code || currentLocale;
  document.documentElement.dir = currentCatalog?.meta?.dir || "ltr";
  setStoredLocale(currentLocale);
  applyTranslations();
  observe();
  updateManifestLink();
  window.dispatchEvent(new CustomEvent("vibedeck:localechange", { detail: { locale: currentLocale } }));
  for (const listener of listeners) {
    try { listener(currentLocale); } catch { /* one listener must not break the UI */ }
  }
  return currentLocale;
}

export async function setLocale(locale) {
  const normalized = normalizeLocale(locale) || "zh-Hant";
  if (normalized === currentLocale && currentCatalog) {
    setStoredLocale(normalized);
    updateLocaleSelector();
    return normalized;
  }
  if (!sourceCatalog) sourceCatalog = await loadCatalog("zh-Hant");
  try {
    currentCatalog = normalized === "zh-Hant" ? sourceCatalog : await loadCatalog(normalized);
    currentLocale = normalized;
  } catch {
    currentCatalog = sourceCatalog;
    currentLocale = "zh-Hant";
  }
  rebuildTranslationIndexes();
  document.documentElement.lang = currentCatalog?.meta?.code || currentLocale;
  document.documentElement.dir = currentCatalog?.meta?.dir || "ltr";
  setStoredLocale(currentLocale);
  applyTranslations();
  updateManifestLink();
  window.dispatchEvent(new CustomEvent("vibedeck:localechange", { detail: { locale: currentLocale } }));
  for (const listener of listeners) {
    try { listener(currentLocale); } catch { /* ignore individual UI refresh errors */ }
  }
  return currentLocale;
}
