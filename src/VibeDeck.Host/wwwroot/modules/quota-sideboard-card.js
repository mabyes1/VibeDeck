import { quotaDataFingerprint } from "./quota-model.js?v=2";
import {
  hasActiveSecondaryCardInteraction,
  onSecondaryCardInteractionEnd,
} from "./secondary-card-dialog.js?v=3";

export function createQuotaSideboardCardController({
  document,
  host,
  tLegacy,
  buildViewState,
  cards,
  getActiveTab,
  setActiveTab,
  getAccountIndex,
}) {
  let snapshot = {};
  let renderedFingerprint = "";
  let renderDeferred = false;

  function deferRenderUntilInteractionEnds() {
    if (renderDeferred) return;
    renderDeferred = onSecondaryCardInteractionEnd(host, () => {
      renderDeferred = false;
      if (hasActiveSecondaryCardInteraction(host, document)) {
        deferRenderUntilInteractionEnds();
        return;
      }
      render();
    });
  }

  function emptyCard(message) {
    const card = document.createElement("article");
    card.className = "quota-account-card quota-sideboard-account-card quota-sideboard-empty";
    const title = document.createElement("strong");
    title.textContent = tLegacy("AI 額度");
    const copy = document.createElement("span");
    copy.textContent = message;
    card.append(title, copy);
    return card;
  }

  function addFamilySwitcher(card, state) {
    const select = document.createElement("select");
    select.className = "quota-sideboard-family-select";
    select.setAttribute("aria-label", tLegacy("切換額度來源"));
    for (const tab of state.tabs) {
      const option = document.createElement("option");
      option.value = tab.id;
      option.textContent = tab.label;
      option.selected = tab.id === state.activeTab;
      select.append(option);
    }
    select.addEventListener("change", () => {
      setActiveTab(select.value);
      render();
    });

    const toolbar = card.querySelector(".quota-card-toolbar");
    if (toolbar) toolbar.prepend(select);
    else card.prepend(select);
  }

  function render() {
    if (!host) return;
    const state = buildViewState(snapshot, getActiveTab());
    if (state.activeTab !== getActiveTab()) setActiveTab(state.activeTab);
    let card = null;

    if (state.activeTab === "agy" && state.agyAccounts.length) {
      const index = getAccountIndex("agy", state.agyAccounts);
      const page = cards.renderAgyAccountPage(
        state.agyAccounts[index], state.agyAccounts, index, snapshot);
      card = page.firstElementChild;
    } else if (state.tabProviders.length) {
      const index = getAccountIndex(state.activeTab, state.tabProviders);
      card = cards.renderSingleQuotaPage(state.tabProviders[index], snapshot, {
        index,
        total: state.tabProviders.length,
        tabId: state.activeTab,
      });
    } else {
      card = emptyCard(tLegacy("尚無額度來源"));
    }

    card.classList.add("quota-sideboard-account-card");
    addFamilySwitcher(card, state);
    host.replaceChildren(card);
    renderedFingerprint = quotaDataFingerprint(snapshot);
  }

  return {
    renderSnapshot(nextSnapshot) {
      snapshot = nextSnapshot || {};
      const nextFingerprint = quotaDataFingerprint(snapshot);
      if (nextFingerprint === renderedFingerprint) return;
      if (hasActiveSecondaryCardInteraction(host, document)) {
        deferRenderUntilInteractionEnds();
        return;
      }
      render();
    },
    render,
  };
}
