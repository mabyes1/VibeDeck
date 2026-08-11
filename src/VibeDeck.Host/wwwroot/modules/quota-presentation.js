import { getProviderFamily } from "./quota-model.js?v=1";

const identity = value => value;

export function buildQuotaSummary(viewState, translateLegacy = identity) {
  const {
    activeTab,
    activeDefinition,
    hasUsable,
    agyAccounts = [],
    codexProviders = [],
    codexUsable,
    tabProviders = [],
  } = viewState || {};

  if (activeTab === "agy") {
    return agyAccounts.length
      ? `AGY · ${agyAccounts.length} 個帳號`
      : "AGY · 尚未導入帳號";
  }

  if (activeTab === "codex") {
    return codexUsable
      ? `Codex · ${codexProviders.length} 個帳號 · 已讀到額度`
      : "Codex · 等待本機 session";
  }

  if (tabProviders.length) {
    const pendingSource = tabProviders.some(provider =>
      String(provider?.State || provider?.state || "").toLowerCase() === "source-needed");
    return hasUsable
      ? `${activeDefinition?.label || "AI"} · ${tabProviders.length} 個帳號`
      : `${activeDefinition?.label || "AI"} · ${formatQuotaStateLabel(
          pendingSource ? "source-needed" : tabProviders[0]?.State || tabProviders[0]?.state,
          translateLegacy
        )}`;
  }

  return "目前沒有可用的額度來源。";
}

export function buildQuotaHelpSpec(tabId, state = {}, eink = false) {
  if (tabId === "codex") {
    if (state.codexUsable) {
      return {
        title: "Codex 資料來源",
        steps: eink
          ? [
              "目前使用中的帳號可從本機 Codex 活動取得額度；已保存帳號可直接更新 account usage。",
              "非使用中帳號若授權過期，可用「重新授權額度」更新，不會切換目前的 Codex 帳號。"
            ]
          : [
              "Active account: VibeDeck can read the newest local Codex rate-limit activity and keeps a normalized per-account snapshot.",
              "Saved accounts: ↻ refreshes the selected account directly from Codex account usage; it does not switch the active Codex login.",
              "Expired saved authorization: choose Re-authorize quota for that account. VibeDeck runs OAuth in-process and only updates its saved profile after identity verification.",
              "Cache: normalized quota rows remain per account under the Host quota store, so the last known figures survive temporary authorization or network failures."
            ]
      };
    }

    return {
      title: eink ? "Codex bind" : "Codex bind requirements",
      steps: eink
        ? [
            "先讓 VibeDeck 看過至少一個 Codex 帳號或額度快照。",
            "已知帳號可用「重新授權額度」建立或更新這台 Host 的額度授權。",
            "額度授權不會切換目前使用中的 Codex。"
          ]
        : [
            "Discovery: VibeDeck records Codex accounts it has seen locally and keeps per-account quota snapshots.",
            "Account-specific authorization: a known non-active account can use Re-authorize quota without replacing %USERPROFILE%\\.codex\\auth.json.",
            "Isolation: authorization does not launch, switch, or overwrite the active Codex login; VibeDeck only writes its saved profile after the returned account identity matches.",
            "Refresh: once authorized, ↻ updates the selected account directly instead of depending on whichever account is active on this PC."
          ]
    };
  }

  if (tabId === "claude-code") {
    return {
      title: "Claude Code 資料來源",
      steps: eink
        ? [
            "Source: account usage endpoint, same figures as /usage.",
            "Sign-in is read from local Claude Code state and is never renewed.",
            "Expired sign-in keeps the last known figures and marks the source offline."
          ]
        : [
            "來源：Host 讀取 Claude Code 寫在本機的登入狀態，呼叫與 /usage 同源的帳號用量端點。",
            "只讀不續期：Host 不會 refresh token，避免輪替衝突把 Claude Code CLI 登出。",
            "不留憑證：token 不會寫進額度快取，也不會出現在傳給 PAD 或手機的欄位。",
            "更新節奏：快取 5 分鐘，按 ↻ 會重新讀取額度。"
          ]
    };
  }

  if (state.agyCount > 0) {
    return {
      title: "AGY 資料來源",
      steps: eink
        ? [
            "Store: DPAPI-protected refresh tokens under the Host quotas/agy folder.",
            "Actions: + OAuth · ↻ token refresh + quota API · ⌫ drop local account store."
          ]
        : [
            "Credential store: Host data → quotas/agy/accounts (refresh_token_protected, DPAPI CurrentUser).",
            "Quota fetch: Host exchanges refresh → access token, then calls Antigravity quota endpoints (not Claude Code local config).",
            "Controls: + → POST /api/quotas/agy/oauth/start · ↻ → /api/quotas/refresh · ⌫ → /api/quotas/agy/account/delete."
          ]
    };
  }

  return {
    title: eink ? "AGY bind" : "AGY bind requirements",
    steps: eink
      ? [
          "Prerequisite: Google OAuth client on Host (env or secrets JSON).",
          "+ → loopback OAuth on PC browser (PKCE).",
          "↻ → refresh tokens + retrieveUserQuotaSummary."
        ]
      : [
          "Prerequisite: Google OAuth client on the Host — AGY_GOOGLE_CLIENT_ID / AGY_GOOGLE_CLIENT_SECRET, or Host secrets/agy-google-oauth.json.",
          "Bind: + issues POST /api/quotas/agy/oauth/start (PKCE, loopback redirect to Host); complete consent in the PC browser.",
          "Persist: refresh tokens land in Host quotas/agy/accounts; quota cache under quotas/agy/cache.",
          "Hydrate: ↻ refreshes access tokens and pulls Antigravity remaining-quota windows (Claude / Gemini buckets).",
          "Missing client credentials: oauth/start fails closed — expected, not a device-pairing fault."
        ]
  };
}

export function formatCodexCreditBalance(provider, locale) {
  const unlimited = provider?.CreditUnlimited ?? provider?.creditUnlimited;
  if (unlimited === true || String(unlimited).toLowerCase() === "true") return "∞";
  const balance = Number(provider?.CreditBalance ?? provider?.creditBalance);
  return Number.isFinite(balance)
    ? new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(balance)
    : "";
}

export function formatQuotaProviderDetail(provider, translateLegacy = identity) {
  const state = String(provider?.State || provider?.state || "").toLowerCase();
  if (["ok", "available"].includes(state)) return "";
  const family = getProviderFamily(provider);
  const detail = String(provider?.Detail || provider?.detail || "").trim();
  if (family === "claude-code" && /sign-in has expired/i.test(detail)) {
    return translateLegacy("Claude Code 登入已過期。請在這台 PC 執行一次 Claude Code，再按更新。");
  }
  if (state === "source-needed") {
    if (family === "claude-code") return translateLegacy("尚未讀到 Claude Code 額度。請確認這台 PC 已登入 Claude Code，再按更新。");
    if (family === "codex") return translateLegacy("尚未讀到 Codex 額度。請先使用一次 Codex，再按更新。");
    if (family === "agy") return translateLegacy("尚未取得 AGY 額度。請完成帳號連結後再按更新。");
    return translateLegacy("等待來源提供額度資料。");
  }
  return translateLegacy("額度來源暫時無法更新。請確認來源可用後再按更新。");
}

export function formatQuotaStateLabel(state, translateLegacy = identity) {
  const normalized = String(state || "").toLowerCase();
  if (["ok", "available"].includes(normalized)) return translateLegacy("來源可用");
  if (normalized === "source-needed") return translateLegacy("等待來源");
  return translateLegacy("來源不可用");
}

export function buildQuotaTimestampState(providers, snapshot, { locale, translateLegacy = identity } = {}) {
  const list = Array.isArray(providers) ? providers : [];
  const observed = list
    .map(provider => provider?.ObservedAt || provider?.observedAt || "")
    .filter(Boolean)
    .sort((left, right) => (Date.parse(right) || 0) - (Date.parse(left) || 0))[0];
  const unavailable = list.some(provider =>
    !["ok", "available"].includes(String(provider?.State || provider?.state || "").toLowerCase()));
  const updated = observed || (!unavailable ? snapshot?.GeneratedAt || snapshot?.generatedAt : "");
  const formatted = updated
    ? new Date(updated).toLocaleString(locale, {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit"
      })
    : unavailable ? translateLegacy("來源不可用") : "--";
  const stale = list.some(provider =>
    String(provider?.Freshness || provider?.freshness || "").toLowerCase() === "stale") || unavailable;

  return {
    stale,
    text: stale && updated ? `${formatted} · ${translateLegacy("來源離線")}` : formatted
  };
}
