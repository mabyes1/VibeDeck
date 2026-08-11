import {
  escapeHtml,
  extractQuotaEmail,
  extractQuotaTier,
  formatQuotaWindowLabel,
  normalizeTierLabel,
  renderQuotaWindow,
  summarizeQuotaWindow,
} from "./quota-formatters.js?v=51";
import { getProviderFamily, providerContains } from "./quota-model.js?v=1";
import {
  buildQuotaTimestampState,
  formatCodexCreditBalance,
  formatQuotaProviderDetail,
  formatQuotaStateLabel,
} from "./quota-presentation.js?v=2";

export function buildQuotaSwitcherHtml(index, total, tabId) {
  if (total < 2) return `<span class="quota-account-switcher"></span>`;
  const dots = Array.from({ length: total }, (_, itemIndex) =>
    `<span class="quota-dot${itemIndex === index ? " active" : ""}"></span>`).join("");
  return `
    <div class="quota-account-switcher" data-tab-id="${tabId}">
      <button class="quota-page-button" type="button" aria-label="上一個帳號">‹</button>
      <span>${index + 1} / ${total}</span>
      <span class="quota-dots">${dots}</span>
      <button class="quota-page-button" type="button" aria-label="下一個帳號">›</button>
    </div>
  `;
}

export function createQuotaCardRenderer({
  document,
  t,
  tLegacy,
  getIntlLocale,
  actions,
  codexAccountManager,
  changeAccount,
}) {
  function wireSwitcher(card) {
    const switcher = card.querySelector(".quota-account-switcher");
    if (!switcher) return;
    const buttons = switcher.querySelectorAll("button");
    if (buttons.length < 2) return;
    buttons[0].addEventListener("click", () => changeAccount(switcher.dataset.tabId, -1));
    buttons[1].addEventListener("click", () => changeAccount(switcher.dataset.tabId, 1));
  }

  function renderProvider(title, provider) {
    const primary = provider.Primary || provider.primary || {};
    const secondary = provider.Secondary || provider.secondary || {};
    return `
      <section class="quota-provider">
        <strong class="quota-provider-title">${escapeHtml(title)}</strong>
        ${renderQuotaWindow(formatQuotaWindowLabel(primary, tLegacy("5 小時額度")), primary)}
        ${renderQuotaWindow(formatQuotaWindowLabel(secondary, tLegacy("週額度")), secondary)}
      </section>
    `;
  }

  function renderEinkUsage(provider, title) {
    const primary = provider.Primary || provider.primary || {};
    const secondary = provider.Secondary || provider.secondary || {};
    return `
      <section class="quota-eink-usage">
        <strong class="quota-provider-title">${escapeHtml(title)}</strong>
        ${renderQuotaWindow(formatQuotaWindowLabel(primary, tLegacy("5 小時額度")), primary)}
        ${renderQuotaWindow(formatQuotaWindowLabel(secondary, tLegacy("週額度")), secondary)}
      </section>
    `;
  }

  function applyTimestamp(card, providers, snapshot) {
    const element = card.querySelector(".quota-time");
    if (!element) return;
    const state = buildQuotaTimestampState(providers, snapshot, {
      locale: getIntlLocale(),
      translateLegacy: tLegacy,
    });
    element.classList.toggle("quota-stale", state.stale);
    element.textContent = state.text;
  }

  function renderAgyAccountCard(account, snapshot, pageInfo) {
    const providers = account.providers || [];
    const card = document.createElement("article");
    card.className = "quota-account-card";
    const email = account.email || extractQuotaEmail(providers) || "Antigravity Account";
    const tier = account.tier || extractQuotaTier(providers) || "PRO";
    card.dataset.accountId = account.id || "";
    card.dataset.accountEmail = email;
    card.dataset.quotaFamily = "agy";
    card.dataset.statusKey = `agy:${account.id || email}`;
    const updated = snapshot?.GeneratedAt || snapshot?.generatedAt;
    const updatedText = updated
      ? new Date(updated).toLocaleString(getIntlLocale(), {
          year: "numeric",
          month: "2-digit",
          day: "2-digit",
          hour: "2-digit",
          minute: "2-digit",
        })
      : "--";
    const claude = providers.find(provider => providerContains(provider, "claude")) || {};
    const gemini = providers.find(provider => providerContains(provider, "gemini")) || {};

    card.innerHTML = `
      <div class="quota-account-head">
        <span class="quota-check"></span>
        <span class="quota-account-email"></span>
        ${buildQuotaSwitcherHtml(pageInfo?.index || 0, pageInfo?.total || 1, pageInfo?.tabId || "agy")}
        <span class="quota-pill"></span>
      </div>
      <div class="quota-pair">
        ${renderProvider("Claude", claude)}
        ${renderProvider("Gemini", gemini)}
      </div>
      <div class="quota-credit-row">可用 AI 點數：<span>--</span></div>
      <div class="quota-footer">
        <span class="quota-time"></span>
        <span></span>
        <div class="quota-toolbox" aria-label="${escapeHtml(tLegacy("額度操作"))}">
          <button type="button" title="${escapeHtml(tLegacy("更新額度"))}" data-quota-action="refresh">↻ ${escapeHtml(tLegacy("更新"))}</button>
          <button class="is-danger" type="button" title="${escapeHtml(t("ui.agyRemoveAccount"))}" data-quota-action="agy-delete">⌫ ${escapeHtml(tLegacy("刪除"))}</button>
          <details class="quota-more-actions">
            <summary title="${escapeHtml(t("ui.moreActions"))}" aria-label="${escapeHtml(t("ui.moreActions"))}">⋯</summary>
            <div>
              <button type="button" title="${t("ui.agyCliOpenTitle")}" data-quota-action="agy-cli">▶ ${t("ui.agyCliOpen")}</button>
              <button type="button" title="${t("ui.agyAddQuotaAccountTitle")}" data-quota-action="agy-oauth">＋ ${t("ui.agyAddQuotaAccount")}</button>
            </div>
          </details>
        </div>
      </div>
      <div class="quota-action-status" aria-live="polite"></div>
    `;
    card.querySelector(".quota-account-email").textContent = email;
    card.querySelector(".quota-pill").textContent = normalizeTierLabel(tier);
    card.querySelector(".quota-time").textContent = updatedText;
    actions.applyCardStatus(card);
    wireSwitcher(card);
    actions.wireToolbox(card);
    return card;
  }

  function renderAgyAccountPage(account, accounts, index, snapshot) {
    const page = document.createElement("section");
    page.className = "quota-account-page";
    page.append(renderAgyAccountCard(account, snapshot, {
      index,
      total: accounts.length,
      tabId: "agy",
    }));
    return page;
  }

  function renderSingleQuotaPage(provider, snapshot, pageInfo) {
    const card = document.createElement("article");
    card.className = "quota-account-card";
    const label = provider.AccountEmail || provider.accountEmail || provider.Label || provider.label || provider.Id || provider.id || "AI";
    const state = provider.State || provider.state || "unknown";
    const family = getProviderFamily(provider);
    const accountId = provider.AccountId || provider.accountId || provider.Id || provider.id || "";
    const isActive = Boolean(provider.IsActive ?? provider.isActive);
    card.dataset.statusKey = `${family}:${label}`;
    card.dataset.quotaFamily = family;
    card.dataset.accountId = accountId;
    card.dataset.accountEmail = provider.AccountEmail || provider.accountEmail || "";
    const creditBalance = family === "codex" ? formatCodexCreditBalance(provider, getIntlLocale()) : "";
    const sourceDetail = formatQuotaProviderDetail(provider, tLegacy);
    card.innerHTML = `
      <div class="quota-account-head">
        <span class="quota-check"></span>
        <span class="quota-account-email"></span>
        ${isActive ? '<span class="quota-active-badge">目前使用中</span>' : ''}
        ${buildQuotaSwitcherHtml(pageInfo?.index || 0, pageInfo?.total || 1, pageInfo?.tabId || family)}
        <span class="quota-pill"></span>
      </div>
      <div class="quota-pair single">
        ${renderProvider(provider.Label || provider.label || label, provider)}
      </div>
      ${sourceDetail ? `<div class="quota-source-detail">${escapeHtml(sourceDetail)}</div>` : ""}
      ${creditBalance ? `<div class="quota-credit-row"><span>${escapeHtml(tLegacy("剩餘 ChatGPT Credits："))}</span><strong>${escapeHtml(creditBalance)}</strong></div>` : ""}
      <div class="quota-footer">
        <span class="quota-time"></span>
        <span></span>
        <div class="quota-toolbox" aria-label="${escapeHtml(tLegacy("額度操作"))}">
          <button type="button" title="${escapeHtml(tLegacy("更新額度"))}" data-quota-action="refresh">↻ ${escapeHtml(tLegacy("更新"))}</button>
          ${family === "codex" ? `<button class="is-danger" type="button" title="${escapeHtml(t("ui.codexCacheDelete"))}" data-quota-action="codex-delete">⌫ ${escapeHtml(tLegacy("刪除"))}</button>
          ${!isActive ? `<details class="quota-more-actions">
            <summary title="${escapeHtml(t("ui.moreActions"))}" aria-label="${escapeHtml(t("ui.moreActions"))}">⋯</summary>
            <div>
              <button type="button" title="${escapeHtml(t("ui.codexQuotaReauthTitle"))}" data-quota-action="codex-quota-reauth">${escapeHtml(t("ui.codexQuotaReauth"))}</button>
            </div>
          </details>` : ""}` : family === "claude-code"
            ? `<button class="is-danger" type="button" title="${escapeHtml(tLegacy("從 VibeDeck 移除帳號"))}" data-quota-action="claude-delete">⌫ ${escapeHtml(tLegacy("刪除"))}</button>`
            : ""}
        </div>
      </div>
      <div class="quota-action-status" aria-live="polite"></div>
    `;
    card.querySelector(".quota-account-email").textContent = label;
    const tier = provider.AccountTier || provider.accountTier;
    card.querySelector(".quota-pill").textContent = tier
      ? normalizeTierLabel(tier)
      : formatQuotaStateLabel(state, tLegacy);
    applyTimestamp(card, [provider], snapshot);
    actions.applyCardStatus(card);
    wireSwitcher(card);
    actions.wireToolbox(card);
    return card;
  }

  function renderEinkCodexOverview(provider, snapshot, pageInfo) {
    const card = document.createElement("article");
    card.className = "quota-account-card quota-eink-overview";
    const label = provider.AccountEmail || provider.accountEmail || provider.Label || provider.label || provider.Id || provider.id || "AI";
    const state = provider.State || provider.state || "unknown";
    const family = getProviderFamily(provider);
    const accountId = provider.AccountId || provider.accountId || provider.Id || provider.id || "";
    const isActive = Boolean(provider.IsActive ?? provider.isActive);
    const creditBalance = family === "codex" ? formatCodexCreditBalance(provider, getIntlLocale()) : "";

    card.dataset.statusKey = `${family}:${label}`;
    card.dataset.quotaFamily = family;
    card.dataset.accountId = accountId;
    card.dataset.accountEmail = provider.AccountEmail || provider.accountEmail || "";
    card.innerHTML = `
      <div class="quota-eink-overview-head">
        <div class="quota-account-head">
          <span class="quota-check"></span>
          <span class="quota-account-email"></span>
          <span class="quota-pill"></span>
        </div>
        ${buildQuotaSwitcherHtml(pageInfo?.index || 0, pageInfo?.total || 1, pageInfo?.tabId || family)}
      </div>
      ${renderEinkUsage(provider, provider.Label || provider.label || label)}
      ${creditBalance ? `<div class="quota-credit-row quota-eink-credit"><span>${escapeHtml(tLegacy("剩餘 ChatGPT Credits："))}</span><strong>${escapeHtml(creditBalance)}</strong></div>` : ""}
      <div class="quota-eink-overview-footer">
        <span class="quota-time"></span>
        <div class="quota-toolbox" aria-label="額度操作">
          <button type="button" title="${escapeHtml(tLegacy("更新額度"))}" data-quota-action="refresh">↻ ${escapeHtml(tLegacy("更新"))}</button>
          <button class="is-danger" type="button" title="${escapeHtml(t("ui.codexCacheDelete"))}" data-quota-action="codex-delete">⌫ ${escapeHtml(tLegacy("刪除"))}</button>
          ${!isActive ? `<button type="button" title="${escapeHtml(t("ui.codexQuotaReauthTitle"))}" data-quota-action="codex-quota-reauth">↻ ${escapeHtml(t("ui.codexQuotaReauthShort"))}</button>` : ""}
        </div>
      </div>
      <details class="quota-eink-account-manager">
        <summary></summary>
      </details>
      <div class="quota-action-status" aria-live="polite"></div>
    `;

    card.querySelector(".quota-account-email").textContent = label;
    const tier = provider.AccountTier || provider.accountTier;
    card.querySelector(".quota-pill").textContent = tier
      ? normalizeTierLabel(tier)
      : formatQuotaStateLabel(state, tLegacy);
    applyTimestamp(card, [provider], snapshot);
    const manager = card.querySelector(".quota-eink-account-manager");
    manager.querySelector("summary").textContent = t("ui.codexAccountSwitcher");
    manager.append(codexAccountManager.render({ embedded: true, statusCard: card }));
    if (isActive) card.classList.add("quota-eink-active-account");
    actions.applyCardStatus(card);
    wireSwitcher(card);
    actions.wireToolbox(card);
    return card;
  }

  function renderLocalQuotaCard(provider) {
    const card = document.createElement("article");
    card.className = "quota-local-card";
    const primary = provider.Primary || provider.primary || {};
    const secondary = provider.Secondary || provider.secondary || {};
    const label = provider.Label || provider.label || provider.Id || provider.id || "AI";
    const state = provider.State || provider.state || "unknown";
    const primaryText = summarizeQuotaWindow(primary);
    const secondaryText = summarizeQuotaWindow(secondary);
    card.innerHTML = `
      <strong></strong><b></b>
      <span></span><span></span>
    `;
    card.querySelector("strong").textContent = label;
    card.querySelector("b").textContent = formatQuotaStateLabel(state, tLegacy);
    const spans = card.querySelectorAll("span");
    spans[0].textContent = `${formatQuotaWindowLabel(primary, tLegacy("5 小時額度"))} ${primaryText}`;
    spans[1].textContent = `${formatQuotaWindowLabel(secondary, tLegacy("週額度"))} ${secondaryText}`;
    return card;
  }

  return {
    renderAgyAccountPage,
    renderSingleQuotaPage,
    renderEinkCodexOverview,
    renderLocalQuotaCard,
  };
}
