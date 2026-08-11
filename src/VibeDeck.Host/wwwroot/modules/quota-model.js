import { sortQuotaAccountsByRecentUse } from "./quota-account-navigation.js?v=1";
import { extractQuotaEmail, extractQuotaTier } from "./quota-formatters.js?v=51";

const QUOTA_FAMILY_ORDER = ["agy", "codex", "claude-code"];
const QUOTA_FAMILY_LABELS = {
  agy: "AGY",
  codex: "Codex",
  "claude-code": "Claude"
};
const QUOTA_FAMILIES_ALWAYS_SHOWN = ["agy", "codex"];

export function getProviderFamily(provider = {}) {
  const family = provider.Family || provider.family || "";
  if (family) return family;

  const id = String(provider.Id || provider.id || "").toLowerCase();
  if (id.startsWith("agy")) return "agy";
  if (id.startsWith("codex")) return "codex";
  if (id.startsWith("claude")) return "claude-code";
  return id || "unknown";
}

export function providerContains(provider = {}, text = "") {
  const value = [
    provider.Id || provider.id,
    provider.Label || provider.label
  ].join(" ").toLowerCase();
  return value.includes(String(text).toLowerCase());
}

export function quotaDataFingerprint(snapshot) {
  const providers = snapshot?.Providers || snapshot?.providers || [];
  return JSON.stringify(providers.map(provider => ({
    id: provider.Id || provider.id || "",
    family: provider.Family || provider.family || "",
    accountId: provider.AccountId || provider.accountId || "",
    state: provider.State || provider.state || "",
    observedAt: provider.ObservedAt || provider.observedAt || "",
    primary: provider.Primary || provider.primary || null,
    secondary: provider.Secondary || provider.secondary || null
  })).sort((left, right) => `${left.family}:${left.accountId}:${left.id}`.localeCompare(`${right.family}:${right.accountId}:${right.id}`)));
}

export function buildQuotaTabs(providers = []) {
  const present = new Set(providers.map(getProviderFamily).filter(Boolean));
  const ordered = QUOTA_FAMILY_ORDER
    .filter(id => present.has(id) || QUOTA_FAMILIES_ALWAYS_SHOWN.includes(id));
  for (const id of present) {
    if (!ordered.includes(id)) ordered.push(id);
  }
  return ordered.map(id => ({ id, label: QUOTA_FAMILY_LABELS[id] || id }));
}

export function groupAgyAccounts(providers = []) {
  const accounts = new Map();
  for (const provider of providers.filter(item => getProviderFamily(item) === "agy")) {
    const accountId = provider.AccountId || provider.accountId || provider.Source || provider.source || "agy";
    if (!accounts.has(accountId)) {
      accounts.set(accountId, {
        id: accountId,
        email: provider.AccountEmail || provider.accountEmail || extractQuotaEmail([provider]) || "Antigravity Account",
        tier: provider.AccountTier || provider.accountTier || extractQuotaTier([provider]) || "PRO",
        providers: []
      });
    }
    const account = accounts.get(accountId);
    account.email = account.email || provider.AccountEmail || provider.accountEmail;
    account.tier = account.tier || provider.AccountTier || provider.accountTier;
    account.providers.push(provider);
  }

  return Array.from(accounts.values())
    .sort((left, right) => String(left.email).localeCompare(String(right.email)));
}

export function groupSingleProviderAccounts(providers = [], tabId) {
  return sortQuotaAccountsByRecentUse(
    providers.filter(item => getProviderFamily(item) === tabId)
  );
}

export function buildQuotaViewState(snapshot, requestedTab = "agy") {
  const providers = snapshot?.Providers || snapshot?.providers || [];
  const tabs = buildQuotaTabs(providers);
  const activeTab = tabs.some(tab => tab.id === requestedTab)
    ? requestedTab
    : tabs[0]?.id || "agy";
  const activeDefinition = tabs.find(tab => tab.id === activeTab) || null;
  const hasUsable = providers.some(provider => {
    const family = getProviderFamily(provider);
    if (family !== activeTab) return false;
    const state = String(provider.State || provider.state || "").toLowerCase();
    return state === "ok" || family === "agy";
  });
  const agyAccounts = groupAgyAccounts(providers);
  const codexProviders = groupSingleProviderAccounts(providers, "codex");
  const codexUsable = codexProviders.some(provider =>
    String(provider.State || provider.state || "").toLowerCase() === "ok");
  const tabProviders = activeTab === "agy"
    ? []
    : groupSingleProviderAccounts(providers, activeTab);

  return {
    providers,
    tabs,
    activeTab,
    activeDefinition,
    hasUsable,
    agyAccounts,
    codexProviders,
    codexUsable,
    tabProviders,
  };
}
