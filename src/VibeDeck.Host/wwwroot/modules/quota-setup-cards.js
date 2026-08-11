const identity = value => value;

export function buildQuotaSetupSpec(family, eink, t, tLegacy = identity) {
  if (family === "agy") {
    return {
      statusKey: "agy:setup",
      title: eink ? "AGY · unbound" : "AGY · no bound account",
      lines: eink
        ? [
            "Host OAuth client required (env / secrets JSON).",
            "<b>+</b> → PKCE loopback on PC browser.",
            "<b>↻</b> → token refresh + quota API pull.",
          ]
        : [
            "No passive filesystem discovery for AGY — bind via OAuth only.",
            "Client credentials must exist on Host before <b>+</b> (see pipeline notes above).",
            "Successful consent writes DPAPI-protected refresh tokens under the Host quotas/agy folder.",
          ],
      footer: eink ? "oauth/start" : "POST /api/quotas/agy/oauth/start",
      actions: [
        {
          action: "agy-oauth",
          title: t("ui.agyAddQuotaAccountTitle"),
          text: `＋ ${t("ui.agyAddQuotaAccount")}`,
        },
        { action: "refresh", title: tLegacy("更新額度"), text: `↻ ${tLegacy("更新")}` },
      ],
    };
  }

  if (family === "codex") {
    return {
      statusKey: "codex:setup",
      title: tLegacy("Codex 資料來源"),
      lines: eink
        ? [
            tLegacy("先讓 VibeDeck 看過至少一個 Codex 帳號或額度快照。"),
            tLegacy("已知帳號可用「重新授權額度」建立或更新這台 Host 的額度授權。"),
            tLegacy("額度授權不會切換目前使用中的 Codex。"),
          ]
        : [
            tLegacy("先讓 VibeDeck 看過至少一個 Codex 帳號或額度快照。"),
            tLegacy("已知帳號可用「重新授權額度」建立或更新這台 Host 的額度授權。"),
            tLegacy("額度授權不會切換目前使用中的 Codex。"),
          ],
      footer: eink ? "OAuth + account usage" : "OAuth PKCE · account usage",
      actions: [{ action: "refresh", title: tLegacy("更新額度"), text: `↻ ${tLegacy("更新")}` }],
    };
  }

  if (family === "claude-code") {
    return {
      statusKey: "claude-code:setup",
      title: eink ? "Claude · no usage source" : tLegacy("Claude Code · 尚未讀到額度"),
      lines: eink
        ? [
            "Sign in to Claude Code on this PC.",
            "Host reads the sign-in; it never renews it.",
            "Figures come from the account usage endpoint.",
          ]
        : [
            tLegacy("在這台 PC 上登入 Claude Code，Host 會讀取它寫下的登入狀態。"),
            tLegacy("Host 只讀不續期；登入過期時請執行一次 Claude Code，由 CLI 自己更新。"),
            tLegacy("百分比與 Claude Code 的 /usage 同源，VibeDeck 不會保存 token。"),
          ],
      footer: eink ? "account usage" : "POST /api/quotas/refresh",
      actions: [{ action: "refresh", title: tLegacy("更新額度"), text: `↻ ${tLegacy("更新")}` }],
    };
  }

  throw new Error(`Unsupported quota setup family: ${family}`);
}

export function createQuotaSetupCardRenderer({
  document,
  t,
  tLegacy,
  isEinkQuotaClient,
  applyQuotaCardStatus,
  wireQuotaToolbox,
}) {
  function renderSetup(family) {
    const spec = buildQuotaSetupSpec(family, isEinkQuotaClient(), t, tLegacy);
    const card = document.createElement("article");
    card.className = "quota-account-card quota-setup-card";
    card.dataset.statusKey = spec.statusKey;
    card.innerHTML = `
      <div class="quota-setup-title">${spec.title}</div>
      <ul class="quota-setup-list">
        ${spec.lines.map(line => `<li>${line}</li>`).join("")}
      </ul>
      <div class="quota-footer">
        <span class="quota-time">${spec.footer}</span>
        <span></span>
        <div class="quota-toolbox" aria-label="額度操作">
          ${spec.actions.map(action =>
            `<button type="button" title="${action.title}" data-quota-action="${action.action}">${action.text}</button>`
          ).join("")}
        </div>
      </div>
      <div class="quota-action-status" aria-live="polite"></div>
    `;
    applyQuotaCardStatus(card);
    wireQuotaToolbox(card);
    return card;
  }

  function renderEmpty(title, lines) {
    const card = document.createElement("article");
    card.className = "quota-account-card offline quota-setup-card";
    const head = document.createElement("div");
    head.className = "quota-setup-title";
    head.textContent = title;
    card.append(head);
    const list = document.createElement("ul");
    list.className = "quota-setup-list";
    for (const line of lines) {
      const item = document.createElement("li");
      item.textContent = line;
      list.append(item);
    }
    card.append(list);
    return card;
  }

  return { renderSetup, renderEmpty };
}
