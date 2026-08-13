export function buildCodexProfileOption(profile = {}, t) {
  const accountId = profile.AccountId || profile.accountId || "";
  const email = profile.Email || profile.email || "";
  const tier = profile.Tier || profile.tier || "";
  const active = Boolean(profile.IsActive ?? profile.isActive);
  const parts = [email || accountId || t("ui.unknown")];
  if (tier) parts.push(tier);
  if (active) parts.push(t("ui.codexActive"));
  return {
    accountId,
    email,
    tier,
    active,
    value: accountId || email,
    label: parts.join(" · "),
  };
}

export function buildCodexAccountActionState(target = {}) {
  const hasTarget = Boolean(target.accountId || target.email);
  return {
    switchDisabled: !hasTarget || Boolean(target.active),
    reauthDisabled: !hasTarget || Boolean(target.active),
    deleteDisabled: !hasTarget,
  };
}

export function createCodexAccountManager({
  document,
  t,
  tLegacy,
  fetchJsonOrThrow,
  confirmAction,
  runQuotaButton,
  refreshQuotas,
  reauthorizeCodexQuota,
  setQuotaCardStatus,
  applyQuotaCardStatus,
}) {
  function render(options = {}) {
    const embedded = Boolean(options.embedded);
    const card = document.createElement(embedded ? "div" : "article");
    card.className = embedded
      ? "quota-eink-account-controls"
      : "quota-account-card quota-codex-switcher";
    if (!embedded) card.dataset.statusKey = "codex:switcher";

    const row = document.createElement("div");
    row.className = "quota-codex-switch-row";
    const select = document.createElement("select");
    select.className = "quota-codex-select";
    select.setAttribute("aria-label", t("ui.codexSelectAccount"));
    const loadingOption = document.createElement("option");
    loadingOption.value = "";
    loadingOption.textContent = t("ui.codexProfilesLoading");
    select.append(loadingOption);

    const toolbox = document.createElement("div");
    toolbox.className = "quota-toolbox";
    toolbox.setAttribute("aria-label", t("ui.codexAccountActions"));
    const makeButton = (action, text, titleText) => {
      const button = document.createElement("button");
      button.type = "button";
      button.dataset.quotaAction = action;
      button.textContent = text;
      button.title = titleText;
      button.setAttribute("aria-label", titleText);
      return button;
    };
    toolbox.append(
      makeButton("codex-switch", `▶ ${t("ui.codexSwitch")}`, t("ui.codexSwitchTitle")),
      makeButton("codex-reauth", `↻ ${t("ui.codexQuotaReauth")}`, t("ui.codexQuotaReauthTitle")),
      makeButton("codex-profile-delete", `⌫ ${t("ui.codexDelete")}`, t("ui.codexDeleteTitle"))
    );
    row.append(select, toolbox);

    if (!embedded) {
      const manager = document.createElement("details");
      manager.className = "quota-account-manager";
      const summary = document.createElement("summary");
      summary.textContent = t("ui.codexManageAccounts");
      manager.append(summary, row);
      card.append(manager);
    } else {
      card.append(row);
    }
    if (!embedded) {
      const status = document.createElement("div");
      status.className = "quota-action-status";
      status.setAttribute("aria-live", "polite");
      card.append(status);
      applyQuotaCardStatus(card);
    }
    wire(card, options.statusCard || card);
    return card;
  }

  function wire(card, statusCard = card) {
    const select = card.querySelector(".quota-codex-select");
    const switchButton = card.querySelector('[data-quota-action="codex-switch"]');
    const reauthButton = card.querySelector('[data-quota-action="codex-reauth"]');
    const deleteButton = card.querySelector('[data-quota-action="codex-profile-delete"]');

    function selectedAccount() {
      const option = select?.selectedOptions?.[0];
      return {
        accountId: option?.dataset.accountId || "",
        email: option?.dataset.email || "",
        label: option?.textContent || "",
        active: option?.dataset.active === "1",
      };
    }

    function syncAccountActions() {
      const target = selectedAccount();
      const state = buildCodexAccountActionState(target);
      if (switchButton) switchButton.disabled = state.switchDisabled;
      if (reauthButton) {
        reauthButton.disabled = state.reauthDisabled;
        reauthButton.title = target.active
          ? t("ui.codexQuotaReauthActiveHint")
          : t("ui.codexQuotaReauthTitle");
      }
      if (deleteButton) deleteButton.disabled = state.deleteDisabled;
    }

    async function populate() {
      try {
        const profiles = await fetchJsonOrThrow("/api/quotas/codex/profiles");
        select.replaceChildren();
        if (!Array.isArray(profiles) || !profiles.length) {
          const option = document.createElement("option");
          option.value = "";
          option.textContent = t("ui.codexNoProfiles");
          select.append(option);
          if (switchButton) switchButton.disabled = true;
          if (reauthButton) reauthButton.disabled = true;
          if (deleteButton) deleteButton.disabled = true;
          return;
        }

        for (const profile of profiles) {
          const view = buildCodexProfileOption(profile, t);
          const option = document.createElement("option");
          option.dataset.accountId = view.accountId;
          option.dataset.email = view.email;
          option.dataset.active = view.active ? "1" : "0";
          option.value = view.value;
          option.textContent = view.label;
          option.selected = view.active;
          select.append(option);
        }
        syncAccountActions();
      } catch (error) {
        select.replaceChildren();
        const option = document.createElement("option");
        option.value = "";
        option.textContent = t("ui.codexProfilesUnavailable");
        select.append(option);
        if (switchButton) switchButton.disabled = true;
        if (reauthButton) reauthButton.disabled = true;
        if (deleteButton) deleteButton.disabled = true;
        setQuotaCardStatus(statusCard, t("ui.codexProfilesFailed"), "error", error.message || "");
      }
    }

    select?.addEventListener("change", syncAccountActions);

    if (switchButton) switchButton.addEventListener("click", async () => {
      const target = selectedAccount();
      if (!target.accountId && !target.email) {
        setQuotaCardStatus(statusCard, t("ui.codexProfileRequired"), "error");
        return;
      }
      if (!await confirmAction({
        title: t("ui.codexAccountSwitcher"),
        message: t("ui.codexSwitchConfirm", { account: target.label || target.email || target.accountId }),
        confirmLabel: t("ui.codexSwitch"),
        cancelLabel: tLegacy("取消"),
      })) return;
      await runQuotaButton(switchButton, async () => {
        const result = await fetchJsonOrThrow("/api/quotas/codex/switch", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(target),
        });
        await refreshQuotas({ force: true });
        return result;
      }, {
        pending: t("ui.codexSwitchPending"),
        success: t("ui.codexSwitchSuccess"),
      });
    });

    if (reauthButton) reauthButton.addEventListener("click", async () => {
      await reauthorizeCodexQuota(reauthButton, selectedAccount(), statusCard);
    });

    if (deleteButton) deleteButton.addEventListener("click", async () => {
      const target = selectedAccount();
      if (!target.accountId && !target.email) {
        setQuotaCardStatus(statusCard, t("ui.codexProfileRequired"), "error");
        return;
      }
      if (!await confirmAction({
        title: t("ui.codexDelete"),
        message: t("ui.codexDeleteConfirm", { account: target.label || target.email || target.accountId }),
        confirmLabel: t("ui.codexDelete"),
        cancelLabel: tLegacy("取消"),
        tone: "danger",
      })) return;
      await runQuotaButton(deleteButton, async () => {
        const result = await fetchJsonOrThrow("/api/quotas/codex/profile/delete", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(target),
        });
        await populate();
        return result;
      }, {
        pending: t("ui.codexDeletePending"),
        success: t("ui.codexDeleteSuccess"),
      });
    });

    populate();
  }

  return { render };
}
