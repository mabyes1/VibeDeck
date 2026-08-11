import { getIntlLocale, tLegacy } from "./i18n.js?v=4";
import { confirmAction } from "./ui-confirm.js?v=1";
import { applyFeedbackState } from "./feedback-state.js?v=1";

const CARD_TYPE_LABELS = {
  "message-feed": "即時訊息",
  status: "狀態",
  metric: "數值",
  "key-value": "資料清單",
};

const SEVERITIES = ["info", "success", "warning", "error"];

export function createCustomCardsController({
  elements,
  fetchJsonOrThrow,
  getActiveMode,
  isLocalConsole,
  isTrustRequiredError,
  isEinkClient,
  onWindowsNotifications,
}) {
  // 直接用 id 取 DOM，避免上層傳入 null / 舊引用導致監聽器沒掛上
  const byId = id => document.getElementById(id);
  const customSideboardPage = elements.customSideboardPage || byId("customSideboardPage");
  const systemSideboardPage = elements.systemSideboardPage || byId("systemSideboardPage");
  const customPageTabs = elements.customPageTabs || byId("sideboardPageTabs");
  const customCardsGrid = elements.customCardsGrid || byId("customCardsGrid");
  const customCardsStatus = elements.customCardsStatus || byId("customCardsStatus");
  const customRefresh = elements.customRefresh || byId("customRefreshCards");
  const customSettingsButton = elements.customSettingsButton || byId("customSettingsButton");
  const customCardSettingsPanel = elements.customCardSettingsPanel || byId("customCardSettingsPanel");
  const customSettingsClose = elements.customSettingsClose || byId("customSettingsClose");
  const customCardSettingsForm = elements.customCardSettingsForm || byId("customCardSettingsForm");
  const customSettingsCard = elements.customSettingsCard || byId("customSettingsCard");
  const customSettingsMaxItems = elements.customSettingsMaxItems || byId("customSettingsMaxItems");
  const customSettingsStreamEnabled = elements.customSettingsStreamEnabled || byId("customSettingsStreamEnabled");
  const customSettingsStreamDelay = elements.customSettingsStreamDelay || byId("customSettingsStreamDelay");
  const customSettingsHint = elements.customSettingsHint || byId("customSettingsHint");
  const customSettingsSave = elements.customSettingsSave || byId("customSettingsSave");
  const customSettingsClear = elements.customSettingsClear || byId("customSettingsClear");
  const customManageButton = elements.customManageButton || byId("customManageButton");
  const customSourcesManager = elements.customSourcesManager || byId("customSourcesManager");
  const customSourceList = elements.customSourceList || byId("customSourceList");
  const customSourceForm = elements.customSourceForm || byId("customSourceForm");
  const customSourceFormTitle = elements.customSourceFormTitle || byId("customSourceFormTitle");
  const customSourceKey = elements.customSourceKey || byId("customSourceKey");
  const customSourceDisplayName = elements.customSourceDisplayName || byId("customSourceDisplayName");
  const customCardType = elements.customCardType || byId("customCardType");
  const customCardTitle = elements.customCardTitle || byId("customCardTitle");
  const customCardPosition = elements.customCardPosition || byId("customCardPosition");
  const customStaleAfter = elements.customStaleAfter || byId("customStaleAfter");
  const customDefaultTtl = elements.customDefaultTtl || byId("customDefaultTtl");
  const customMaxItems = elements.customMaxItems || byId("customMaxItems");
  const customSourceFormSubmit = elements.customSourceFormSubmit || byId("customSourceFormSubmit");
  const customSourceCancel = elements.customSourceCancel || byId("customSourceCancel");
  const customCredentialPanel = elements.customCredentialPanel || byId("customCredentialPanel");
  const customCredentialText = elements.customCredentialText || byId("customCredentialText");
  const customCredentialCopy = elements.customCredentialCopy || byId("customCredentialCopy");
  const customCredentialClose = elements.customCredentialClose || byId("customCredentialClose");
  const customAddSource = elements.customAddSource || byId("customAddSource");
  const customManagerAdd = elements.customManagerAdd || byId("customManagerAdd");
  const customManagerClose = elements.customManagerClose || byId("customManagerClose");
  const windowsNotificationControl = elements.windowsNotificationControl || byId("windowsNotificationControl");
  const windowsNotificationStatus = elements.windowsNotificationStatus || byId("windowsNotificationStatus");
  const windowsNotificationMessage = elements.windowsNotificationMessage || byId("windowsNotificationMessage");
  const windowsNotificationEnable = elements.windowsNotificationEnable || byId("windowsNotificationEnable");
  const windowsNotificationDisable = elements.windowsNotificationDisable || byId("windowsNotificationDisable");

  let page = "system";
  let latestSources = [];
  let latestCards = [];
  let settingsCardId = "";
  let credentialValue = "";
  let companionLaunchAttempted = false;
  const streamStates = new Map();

  function setStatus(message, level = "", detail = "") {
    if (!customCardsStatus) return;
    applyFeedbackState(customCardsStatus, { message, state: level || "info", detail });
  }

  function getStreamState(cardId) {
    if (!streamStates.has(cardId)) {
      streamStates.set(cardId, {
        initialized: false,
        knownKeys: new Set(),
        itemsByKey: new Map(),
        displayTextByKey: new Map(),
        nodesByKey: new Map(),
        queue: [],
        streamingKeys: new Set(),
        running: false,
        streamEnabled: true,
        streamCharDelayMs: 28,
        listNode: null,
        scrollTop: 0,
        autoFollow: true,
        autoScrollPending: true,
      });
    }
    return streamStates.get(cardId);
  }

  function getItemKey(item) {
    return String(item?.id || `${item?.receivedAt || ""}|${item?.from || ""}|${item?.text || ""}`);
  }

  function prepareStream(card) {
    const state = getStreamState(card.cardId);
    const items = card.type === "message-feed" ? (card.content?.items || []) : [];
    const hasNewItems = !state.initialized || items.some(item => !state.knownKeys.has(getItemKey(item)));
    const currentKeys = new Set();
    const itemsByKey = new Map();
    state.streamEnabled = card.streamEnabled !== false;
    state.streamCharDelayMs = Number(card.streamCharDelayMs) || 28;
    if (hasNewItems) {
      state.autoFollow = true;
      state.autoScrollPending = true;
    }

    for (const item of items) {
      const key = getItemKey(item);
      currentKeys.add(key);
      itemsByKey.set(key, item);
      const text = item.text || "";
      if (!state.initialized) {
        state.displayTextByKey.set(key, text);
      } else if (!state.knownKeys.has(key)) {
        state.displayTextByKey.set(key, state.streamEnabled ? "" : text);
        if (state.streamEnabled && !state.queue.some(job => job.key === key)) {
          state.queue.push({ key });
        }
      } else if (!state.queue.some(job => job.key === key) && !state.streamingKeys.has(key)) {
        state.displayTextByKey.set(key, text);
      }
    }

    if (!state.streamEnabled) {
      state.queue.length = 0;
      for (const [key, item] of itemsByKey) state.displayTextByKey.set(key, item.text || "");
    }

    state.itemsByKey = itemsByKey;
    state.knownKeys = currentKeys;
    state.initialized = true;
    for (const key of [...state.displayTextByKey.keys()]) {
      if (!currentKeys.has(key) && !state.queue.some(job => job.key === key)) {
        state.displayTextByKey.delete(key);
      }
    }
  }

  function delay(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  function isFeedAtLatest(list) {
    return list.scrollHeight - list.clientHeight - list.scrollTop <= 20;
  }

  function scrollFeedToLatest(state) {
    const list = state.listNode;
    if (!list) return;
    list.scrollTop = Math.max(0, list.scrollHeight - list.clientHeight);
    state.scrollTop = list.scrollTop;
    state.autoFollow = true;
    state.autoScrollPending = false;
  }

  function captureFeedScrollPositions() {
    for (const state of streamStates.values()) {
      const list = state.listNode;
      if (!list?.isConnected) continue;
      state.scrollTop = list.scrollTop;
      state.autoFollow = isFeedAtLatest(list);
    }
  }

  function settleFeedScroll(card) {
    if (card.type !== "message-feed") return;
    const state = getStreamState(card.cardId);
    requestAnimationFrame(() => {
      const list = state.listNode;
      if (!list) return;
      if (state.autoScrollPending || state.autoFollow) {
        scrollFeedToLatest(state);
        return;
      }
      const maxScrollTop = Math.max(0, list.scrollHeight - list.clientHeight);
      list.scrollTop = Math.min(state.scrollTop, maxScrollTop);
    });
  }

  async function processStreamQueue(card) {
    const state = getStreamState(card.cardId);
    if (state.running) return;
    state.running = true;
    try {
      while (state.queue.length) {
        const job = state.queue.shift();
        const item = state.itemsByKey.get(job.key);
        if (!item) continue;
        const text = item.text || "";
        const characters = Array.from(text);
        let rendered = Array.from(state.displayTextByKey.get(job.key) || "");
        if (rendered.length > characters.length || characters.slice(0, rendered.length).join("") !== rendered.join("")) {
          rendered = [];
        }
        state.streamingKeys.add(job.key);
        for (let index = rendered.length; index < characters.length; index++) {
          if (!state.streamEnabled) {
            rendered = characters;
            break;
          }
          rendered.push(characters[index]);
          const value = rendered.join("");
          state.displayTextByKey.set(job.key, value);
          const node = state.nodesByKey.get(job.key);
          if (node) {
            node.textContent = value;
            node.classList.add("is-streaming");
          }
          if (state.autoFollow) scrollFeedToLatest(state);
          await delay(state.streamCharDelayMs);
        }
        state.displayTextByKey.set(job.key, rendered.join(""));
        const node = state.nodesByKey.get(job.key);
        if (node) {
          node.textContent = rendered.join("");
          node.classList.remove("is-streaming");
        }
        if (state.autoFollow) {
          scrollFeedToLatest(state);
          requestAnimationFrame(() => {
            if (state.autoFollow) scrollFeedToLatest(state);
          });
        }
        state.streamingKeys.delete(job.key);
      }
    } finally {
      state.running = false;
    }
  }

  function clearStreamState(cardId) {
    const state = getStreamState(cardId);
    state.initialized = true;
    state.knownKeys.clear();
    state.itemsByKey.clear();
    state.displayTextByKey.clear();
    state.nodesByKey.clear();
    state.queue.length = 0;
    state.listNode = null;
    state.scrollTop = 0;
    state.autoFollow = true;
    state.autoScrollPending = true;
  }

  function renderWindowsNotificationStatus(status) {
    if (!windowsNotificationControl) return;
    const localConsole = isLocalConsole();
    setPanelOpen(windowsNotificationControl, localConsole);
    if (!localConsole) return;
    if (!status) {
      windowsNotificationStatus.textContent = tLegacy("無法取得狀態");
      windowsNotificationMessage.textContent = tLegacy("Windows 通知狀態讀取失敗。");
      return;
    }
    const listening = Boolean(status.listening);
    const enabled = Boolean(status.enabled);
    windowsNotificationStatus.textContent = listening ? tLegacy("監聽中") : enabled ? tLegacy("已設定但未連線") : tLegacy("未啟用");
    windowsNotificationStatus.className = listening ? "windows-notification-ok" : enabled ? "windows-notification-warn" : "";
    windowsNotificationMessage.textContent = status.message || tLegacy("把這台 PC 的新通知轉成「Windows 通知」訊息卡片。");
    if (status.companionRequired && enabled && !listening) {
      windowsNotificationMessage.textContent += ` ${tLegacy("Companion 會在登入使用者的 Windows 工作階段取得通知權限。")}`;
    } else if (status.packaged === false && enabled && !listening) {
      windowsNotificationMessage.textContent += ` ${tLegacy("目前 Host 是非封裝模式，請使用含通知權限的 MSIX 版本。")}`;
    }
    windowsNotificationEnable.hidden = listening;
    windowsNotificationDisable.hidden = !enabled;
  }

  async function loadWindowsNotificationStatus() {
    if (!isLocalConsole()) {
      renderWindowsNotificationStatus(null);
      return;
    }
    try {
      const status = await fetchJsonOrThrow("/api/windows-notifications/status");
      renderWindowsNotificationStatus(status);
      if (status?.listening) companionLaunchAttempted = false;
      if (status?.enabled && !status?.listening && status?.activationUri && !companionLaunchAttempted) {
        companionLaunchAttempted = true;
        const launcher = document.createElement("a");
        launcher.href = status.activationUri;
        launcher.hidden = true;
        document.body.append(launcher);
        launcher.click();
        setTimeout(() => launcher.remove(), 1000);
        setTimeout(() => loadWindowsNotificationStatus(), 1800);
      }
    } catch (error) {
      renderWindowsNotificationStatus(null);
      if (error?.message) setStatus(tLegacy("Windows 通知狀態讀取失敗。"), "error", error.message);
    }
  }

  async function setWindowsNotificationEnabled(nextEnabled) {
    const button = nextEnabled ? windowsNotificationEnable : windowsNotificationDisable;
    if (button) button.disabled = true;
    try {
      const path = nextEnabled ? "/api/windows-notifications/enable" : "/api/windows-notifications/disable";
      const status = await fetchJsonOrThrow(path, { method: "POST" });
      renderWindowsNotificationStatus(status);
      if (nextEnabled && status.activationUri) {
        const launcher = document.createElement("a");
        launcher.href = status.activationUri;
        launcher.hidden = true;
        document.body.append(launcher);
        launcher.click();
        setTimeout(() => launcher.remove(), 1000);
        setTimeout(() => loadWindowsNotificationStatus(), 1800);
      }
      await refresh();
    } catch (error) {
      setStatus(tLegacy("Windows 通知設定失敗"), "error", error.message || "");
      await loadWindowsNotificationStatus();
    } finally {
      if (button) button.disabled = false;
    }
  }

  function setPage(nextPage, shouldRefresh = true) {
    page = nextPage === "custom" ? "custom" : "system";
    systemSideboardPage.hidden = false;
    customSideboardPage.hidden = page !== "custom";
    customPageTabs?.querySelectorAll("button").forEach(button => {
      button.classList.toggle("active", button.dataset.sideboardPage === page);
      button.setAttribute("aria-selected", button.dataset.sideboardPage === page ? "true" : "false");
    });
    if (page === "custom" && shouldRefresh && getActiveMode() === "sideboard") {
      refresh();
      if (isLocalConsole()) {
        loadSources();
        loadWindowsNotificationStatus();
      }
    }
  }

  function createText(tag, className, text) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text != null) element.textContent = text;
    if (/custom-(feed-text|card-title|status-value|status-detail|metric-value|key-value|source-name)/.test(className || "")) {
      element.dataset.userContent = "true";
    }
    return element;
  }

  function formatTime(value) {
    if (!value) return "--";
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? "--"
      : date.toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  }

  function renderMessageFeed(card, content) {
    const state = getStreamState(card.cardId);
    state.nodesByKey.clear();
    const list = createText("ul", "custom-feed-list");
    state.listNode = list;
    list.addEventListener("scroll", () => {
      state.scrollTop = list.scrollTop;
      state.autoFollow = isFeedAtLatest(list);
    }, { passive: true });
    // The API is newest-first; a scrollable feed reads naturally oldest → latest.
    for (const item of [...(content?.items || [])].reverse()) {
      const row = document.createElement("li");
      row.className = `custom-feed-item severity-${SEVERITIES.includes(item.severity) ? item.severity : "info"}`;
      const key = getItemKey(item);
      const meta = document.createElement("div");
      meta.className = "custom-feed-meta";
      meta.append(
        createText("span", "custom-feed-from", item.from || card.sourceKey || tLegacy("來源")),
        createText("time", "custom-feed-time", formatTime(item.occurredAt || item.receivedAt)),
      );
      const text = createText("div", "custom-feed-text", state.displayTextByKey.get(key) ?? item.text ?? "");
      if (state.queue.some(job => job.key === key) || state.streamingKeys.has(key)) text.classList.add("is-streaming");
      state.nodesByKey.set(key, text);
      row.append(meta, text);
      list.append(row);
    }
    if (!list.children.length) list.append(createText("li", "custom-card-empty", tLegacy("等待第一筆訊息")));
    return list;
  }

  function renderStatus(content) {
    const wrapper = createText("div", "custom-status-content");
    wrapper.append(
      createText("strong", "custom-status-value", content?.status || tLegacy("等待資料")),
      createText("span", "custom-status-detail", content?.detail || ""),
    );
    return wrapper;
  }

  function renderMetric(content) {
    const wrapper = createText("div", "custom-metric-content");
    const value = createText("strong", "custom-metric-value", content?.value == null ? "--" : `${content.value}`);
    if (content?.unit) value.append(createText("span", "custom-metric-unit", content.unit));
    wrapper.append(value);
    if (content?.progress != null) {
      const bar = createText("div", "custom-progress");
      const fill = createText("span");
      fill.style.width = `${Math.max(0, Math.min(100, Number(content.progress) || 0))}%`;
      bar.append(fill);
      wrapper.append(bar);
    }
    if (content?.detail) wrapper.append(createText("span", "custom-status-detail", content.detail));
    return wrapper;
  }

  function renderKeyValue(content) {
    const list = createText("dl", "custom-key-value-list");
    for (const item of (content?.items || [])) {
      list.append(createText("dt", "", item.label || "--"), createText("dd", "", item.value || "--"));
    }
    if (!list.children.length) list.append(createText("dd", "custom-card-empty", tLegacy("等待第一筆資料")));
    return list;
  }

  function renderCard(card) {
    const element = createText("article", `custom-card custom-card-${card.type || "unknown"} freshness-${card.freshness || "empty"}`);
    element.dataset.dashboardKey = card.sourceKey === "windows-notifications"
      ? "windows-notifications"
      : `custom:${card.cardId}`;
    element.dataset.dashboardTitle = card.title || tLegacy("自訂卡片");
    const header = createText("header", "custom-card-header");
    const titleBlock = createText("div", "custom-card-title-block");
    titleBlock.append(
      createText("strong", "custom-card-title", card.title || tLegacy("自訂卡片")),
      createText("span", "custom-card-source", `${card.sourceKey === "windows-notifications" ? tLegacy("Windows 通知") : (card.sourceKey || "source")} · ${CARD_TYPE_LABELS[card.type] ? tLegacy(CARD_TYPE_LABELS[card.type]) : card.type || tLegacy("資料")}`),
    );
    const freshness = card.freshness === "stale" ? tLegacy("資料過期") : card.freshness === "empty" ? tLegacy("等待資料") : tLegacy("已更新");
    header.append(titleBlock, createText("span", `custom-card-freshness freshness-${card.freshness || "empty"}`, freshness));
    element.append(header);
    if (card.type === "message-feed") element.append(renderMessageFeed(card, card.content));
    else if (card.type === "status") element.append(renderStatus(card.content));
    else if (card.type === "metric") element.append(renderMetric(card.content));
    else if (card.type === "key-value") element.append(renderKeyValue(card.content));
    else element.append(createText("p", "custom-card-empty", tLegacy("不支援的卡片類型")));
    element.append(createText("footer", "custom-card-footer", `${tLegacy("更新")} ${formatTime(card.lastReceivedAt)} · revision ${card.revision ?? 0}`));
    return element;
  }

  function renderSnapshot(snapshot) {
    captureFeedScrollPositions();
    customCardsGrid.replaceChildren();
    const cards = snapshot?.cards || [];
    latestCards = cards;
    const windowsCard = cards.find(card => card.sourceKey === "windows-notifications") || null;
    onWindowsNotifications?.(windowsCard);
    const displayCards = cards.filter(card => card.sourceKey !== "windows-notifications");
    if (!cards.length) {
      setStatus(tLegacy("沒有自訂卡片"), "muted");
      renderSettingsCardOptions();
      document.dispatchEvent(new CustomEvent("dashboard:cards-changed"));
      return;
    }
    displayCards.forEach(card => {
      prepareStream(card);
      customCardsGrid.append(renderCard(card));
      settleFeedScroll(card);
    });
    displayCards.forEach(card => processStreamQueue(card));
    renderSettingsCardOptions();
    setStatus(`${tLegacy("最後同步")} ${formatTime(snapshot.generatedAt)}`, "muted");
    document.dispatchEvent(new CustomEvent("dashboard:cards-changed"));
  }

  async function refresh() {
    if (getActiveMode() !== "sideboard") return;
    try {
      renderSnapshot(await fetchJsonOrThrow("/api/custom-cards"));
    } catch (error) {
      if (isTrustRequiredError(error)) {
        customCardsGrid.replaceChildren(createText("div", "custom-empty-state", tLegacy("自訂卡片已鎖定，請先配對手機。")));
        setStatus(tLegacy("需要信任裝置"), "error");
        return;
      }
      setStatus(tLegacy("自訂卡片讀取失敗"), "error", error.message || "");
    }
  }

  function isPanelOpen(element) {
    if (!element) return false;
    return element.classList.contains("is-open") || element.style.display === "grid";
  }

  // 用 inline + important 控顯示，不再跟 stylesheet 搶 hidden/display
  function setPanelOpen(element, open, displayWhenOpen = "grid") {
    if (!element) return false;
    element.classList.toggle("is-open", open);
    if (open) {
      element.hidden = false;
      element.removeAttribute("hidden");
      element.style.setProperty("display", displayWhenOpen, "important");
    } else {
      element.hidden = true;
      element.setAttribute("hidden", "");
      element.style.setProperty("display", "none", "important");
    }
    return open;
  }

  function setToolbarButtonVisible(button, visible) {
    if (!button) return;
    if (visible) {
      button.hidden = false;
      button.removeAttribute("hidden");
      button.style.removeProperty("display");
    } else {
      button.hidden = true;
      button.setAttribute("hidden", "");
      button.style.setProperty("display", "none", "important");
    }
  }

  function setManagerVisible(visible) {
    if (!customSourcesManager) return;
    setPanelOpen(customSourcesManager, visible);
    customManageButton?.classList.toggle("active", visible);
    customManageButton?.setAttribute("aria-expanded", visible ? "true" : "false");
    if (visible) {
      loadSources();
    } else {
      hideForm();
      hideCredential();
    }
  }

  function showForm(source = null) {
    setManagerVisible(true);
    setPanelOpen(customSourceForm, true);
    if (!customSourceForm) return;
    customSourceForm.dataset.editSource = source?.sourceKey || "";
    if (customSourceFormTitle) customSourceFormTitle.textContent = source ? `${tLegacy("編輯")} ${source.displayName}` : tLegacy("新增資料來源");
    if (customSourceKey) customSourceKey.value = source?.sourceKey || "";
    if (customSourceDisplayName) customSourceDisplayName.value = source?.displayName || "";
    if (customCardType) customCardType.value = source?.card?.type || "message-feed";
    if (customCardTitle) customCardTitle.value = source?.card?.title || "";
    if (customCardPosition) customCardPosition.value = source?.card?.position ?? "";
    if (customStaleAfter) customStaleAfter.value = source?.card?.staleAfterSeconds ?? 300;
    if (customDefaultTtl) customDefaultTtl.value = source?.card?.defaultTtlSeconds ?? 0;
    if (customMaxItems) customMaxItems.value = source?.card?.maxItems ?? 20;
    if (customSourceKey) customSourceKey.disabled = Boolean(source);
    if (customCardType) customCardType.disabled = Boolean(source);
    if (customSourceFormSubmit) customSourceFormSubmit.textContent = source ? tLegacy("儲存變更") : tLegacy("建立來源");
    customSourceKey?.focus();
  }

  function hideForm() {
    setPanelOpen(customSourceForm, false);
    if (customSourceForm) customSourceForm.dataset.editSource = "";
    if (customSourceKey) customSourceKey.disabled = false;
    if (customCardType) customCardType.disabled = false;
  }

  function numberOr(value, fallback) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  async function submitForm(event) {
    event.preventDefault();
    const editSource = customSourceForm.dataset.editSource || "";
    const sourceKey = customSourceKey.value.trim();
    const displayName = customSourceDisplayName.value.trim();
    const card = {
      title: customCardTitle.value.trim(),
      position: numberOr(customCardPosition.value, 100),
      staleAfterSeconds: numberOr(customStaleAfter.value, 300),
      defaultTtlSeconds: numberOr(customDefaultTtl.value, 0),
      maxItems: numberOr(customMaxItems.value, 20),
    };
    customSourceFormSubmit.disabled = true;
    setStatus(editSource ? tLegacy("正在儲存來源設定…") : tLegacy("正在建立來源…"), "working");
    try {
      let result;
      if (editSource) {
        result = await fetchJsonOrThrow(`/api/custom-sources/${encodeURIComponent(editSource)}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ displayName, card }),
        });
      } else {
        result = await fetchJsonOrThrow("/api/custom-sources", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            sourceKey,
            displayName,
            card: { ...card, type: customCardType.value },
          }),
        });
      }
      hideForm();
      if (result?.ingest?.token) showCredential(result.ingest);
      await refreshAll();
      setStatus(editSource ? tLegacy("來源設定已儲存") : tLegacy("來源已建立"), "success");
    } catch (error) {
      setStatus(tLegacy("來源儲存失敗"), "error", error.message || "");
    } finally {
      customSourceFormSubmit.disabled = false;
    }
  }

  function showCredential(ingest) {
    setManagerVisible(true);
    credentialValue = [
      `Endpoint: ${ingest.endpointUrl}`,
      `Local endpoint: ${ingest.localEndpointUrl}`,
      `Authorization: Bearer ${ingest.token}`,
      "",
      "PowerShell:",
      `$headers = @{ Authorization = \"Bearer ${ingest.token}\" }`,
      `$body = @{ id = \"msg-123\"; from = \"Source\"; text = \"Hello from VibeDeck\" } | ConvertTo-Json`,
      `Invoke-RestMethod -Method Post -Uri \"${ingest.endpointUrl}\" -Headers $headers -ContentType \"application/json\" -Body $body`,
    ].join("\n");
    customCredentialText.value = credentialValue;
    setPanelOpen(customCredentialPanel, true);
    customCredentialText.focus();
    customCredentialText.select();
  }

  async function copyCredential() {
    try {
      await navigator.clipboard.writeText(credentialValue);
      setStatus(tLegacy("連線資訊已複製"), "ok");
    } catch {
      customCredentialText.focus();
      customCredentialText.select();
      setStatus(tLegacy("請使用 Ctrl+C 複製連線資訊"), "muted");
    }
  }

  function hideCredential() {
    credentialValue = "";
    if (customCredentialText) customCredentialText.value = "";
    setPanelOpen(customCredentialPanel, false);
  }

  function getSettingsCard() {
    return latestCards.find(card => card.cardId === settingsCardId) || latestCards[0] || null;
  }

  function applySettingsCardForm() {
    const card = getSettingsCard();
    if (!card) {
      if (customSettingsMaxItems) customSettingsMaxItems.disabled = true;
      if (customSettingsStreamEnabled) customSettingsStreamEnabled.disabled = true;
      if (customSettingsStreamDelay) customSettingsStreamDelay.disabled = true;
      if (customSettingsSave) customSettingsSave.disabled = true;
      if (customSettingsClear) customSettingsClear.disabled = true;
      if (customSettingsHint) customSettingsHint.textContent = tLegacy("目前沒有可設定的卡片。");
      return;
    }

    const isFeed = card.type === "message-feed";
    const maxItems = Math.max(5, Math.min(30, Number(card.maxItems) || 5));
    if (customSettingsMaxItems) {
      customSettingsMaxItems.value = String(maxItems);
      customSettingsMaxItems.disabled = !isFeed;
    }
    if (customSettingsStreamEnabled) {
      customSettingsStreamEnabled.checked = card.streamEnabled !== false;
      customSettingsStreamEnabled.disabled = !isFeed;
    }
    if (customSettingsStreamDelay) {
      customSettingsStreamDelay.value = String(card.streamCharDelayMs || 28);
      customSettingsStreamDelay.disabled = !isFeed;
    }
    if (customSettingsSave) customSettingsSave.disabled = false;
    if (customSettingsClear) customSettingsClear.disabled = false;
    if (customSettingsHint) {
      customSettingsHint.textContent = isFeed
        ? tLegacy("新通知會由完整資料即時推送，再在畫面上逐字出現。")
        : tLegacy("逐字串流目前只套用在即時訊息卡片。");
    }
  }

  function renderSettingsCardOptions() {
    if (!customSettingsCard) return;
    const previous = settingsCardId || customSettingsCard.value;
    customSettingsCard.replaceChildren();
    for (const card of latestCards) {
      const option = document.createElement("option");
      option.value = card.cardId;
      option.textContent = `${card.title || tLegacy("自訂卡片")} · ${card.sourceKey === "windows-notifications" ? tLegacy("Windows 通知") : (card.sourceKey || tLegacy("來源"))}`;
      customSettingsCard.append(option);
    }
    if (!latestCards.length) {
      settingsCardId = "";
      applySettingsCardForm();
      return;
    }
    settingsCardId = latestCards.some(card => card.cardId === previous) ? previous : latestCards[0].cardId;
    customSettingsCard.value = settingsCardId;
    applySettingsCardForm();
  }

  function setSettingsPanelVisible(visible) {
    if (!customCardSettingsPanel) return;
    setPanelOpen(customCardSettingsPanel, visible);
    customSettingsButton?.classList.toggle("active", visible);
    customSettingsButton?.setAttribute("aria-expanded", visible ? "true" : "false");
    if (visible) renderSettingsCardOptions();
  }

  async function saveCardSettings(event) {
    event.preventDefault();
    const card = getSettingsCard();
    if (!card) return;
    if (customSettingsSave) customSettingsSave.disabled = true;
    setStatus(tLegacy("正在儲存卡片設定…"), "working");
    try {
      const result = await fetchJsonOrThrow(`/api/custom-cards/${encodeURIComponent(card.cardId)}/settings`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          maxItems: Number(customSettingsMaxItems?.value || 5),
          streamEnabled: Boolean(customSettingsStreamEnabled?.checked),
          streamCharDelayMs: Number(customSettingsStreamDelay?.value || 28),
        }),
      });
      latestCards = latestCards.map(item => item.cardId === card.cardId
        ? { ...item, maxItems: result.maxItems, streamEnabled: result.streamEnabled, streamCharDelayMs: result.streamCharDelayMs }
        : item);
      await refresh();
      setStatus(tLegacy("卡片設定已保存"), "success");
    } catch (error) {
      setStatus(tLegacy("卡片設定保存失敗"), "error", error.message || "");
    } finally {
      applySettingsCardForm();
    }
  }

  async function clearSelectedCard() {
    const card = getSettingsCard();
    if (!card || !await confirmAction({
      title: tLegacy("清空卡片"),
      message: `${tLegacy("清空")}「${card.title || tLegacy("這張卡片")}」${tLegacy("目前的通知？")}`,
      confirmLabel: tLegacy("清空"),
      cancelLabel: tLegacy("取消"),
      tone: "danger"
    })) return;
    if (customSettingsClear) customSettingsClear.disabled = true;
    setStatus(tLegacy("正在清空通知…"), "working");
    try {
      await fetchJsonOrThrow(`/api/custom-cards/${encodeURIComponent(card.cardId)}/clear`, { method: "POST" });
      clearStreamState(card.cardId);
      await refresh();
      setStatus(tLegacy("目前通知已清空"), "success");
    } catch (error) {
      setStatus(tLegacy("通知清空失敗"), "error", error.message || "");
    } finally {
      applySettingsCardForm();
    }
  }

  function syncAccess() {
    const localConsole = isLocalConsole();
    setToolbarButtonVisible(customManageButton, localConsole);
    setToolbarButtonVisible(customAddSource, localConsole);
    setToolbarButtonVisible(customSettingsButton, localConsole);
    // 本機才顯示 Windows 通知控制；不要在每次 sync 時強制關設定/管理面板
    if (!localConsole) {
      setPanelOpen(windowsNotificationControl, false);
      setManagerVisible(false);
      setSettingsPanelVisible(false);
      return;
    }
    setPanelOpen(windowsNotificationControl, true);
    loadWindowsNotificationStatus();
  }

  function renderSourceRow(source, index) {
    const row = createText("article", "custom-source-row");
    const summary = createText("div", "custom-source-summary");
    summary.append(
      createText("strong", "custom-source-name", source.displayName || source.sourceKey),
      createText("span", "custom-source-meta", `${source.sourceKey} · ${CARD_TYPE_LABELS[source.card?.type] ? tLegacy(CARD_TYPE_LABELS[source.card?.type]) : source.card?.type || "card"}`),
      createText("span", `custom-source-health health-${source.health || "waiting"}`, source.health || "waiting"),
    );
    const actions = createText("div", "custom-source-actions");
    const addAction = (label, title, handler, disabled = false) => {
      const button = createText("button", "custom-source-action", label);
      button.type = "button";
      button.title = title;
      button.disabled = disabled;
      button.addEventListener("click", () => handler(button));
      actions.append(button);
    };
    addAction(tLegacy("編輯"), tLegacy("編輯來源設定"), () => showForm(source));
    addAction(source.enabled ? tLegacy("停用") : tLegacy("啟用"), tLegacy("切換來源啟用狀態"), async button => {
      button.disabled = true;
      await runSourceAction(() => fetchJsonOrThrow(`/api/custom-sources/${encodeURIComponent(source.sourceKey)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ enabled: !source.enabled }),
      }));
    });
    addAction(tLegacy("輪替"), tLegacy("立即撤銷舊 Token 並產生新 Token"), async button => {
      if (!await confirmAction({
        title: tLegacy("輪替 Token"),
        message: `${tLegacy("輪替")} ${source.displayName} ${tLegacy("的 Token？舊 Token 會立即失效。")}`,
        confirmLabel: tLegacy("輪替"),
        cancelLabel: tLegacy("取消"),
        tone: "danger"
      })) return;
      button.disabled = true;
      await runSourceAction(async () => {
        const result = await fetchJsonOrThrow(`/api/custom-sources/${encodeURIComponent(source.sourceKey)}/rotate-token`, { method: "POST" });
        showCredential(result.ingest);
      });
    });
    addAction("↑", tLegacy("往前移動"), () => moveSource(index, -1), index === 0);
    addAction("↓", tLegacy("往後移動"), () => moveSource(index, 1), index === latestSources.length - 1);
    addAction(tLegacy("刪除"), tLegacy("刪除來源與目前資料"), async button => {
      if (!await confirmAction({
        title: tLegacy("刪除來源"),
        message: `${tLegacy("刪除")} ${source.displayName}？${tLegacy("這會清除目前卡片資料。")}`,
        confirmLabel: tLegacy("刪除"),
        cancelLabel: tLegacy("取消"),
        tone: "danger"
      })) return;
      button.disabled = true;
      await runSourceAction(() => fetchJsonOrThrow(`/api/custom-sources/${encodeURIComponent(source.sourceKey)}`, { method: "DELETE" }));
    });
    row.append(summary, actions);
    return row;
  }

  function renderSources(sources) {
    latestSources = [...(sources || [])].sort((a, b) => (a.card?.position || 0) - (b.card?.position || 0));
    customSourceList.replaceChildren();
    if (!latestSources.length) {
      customSourceList.append(createText("p", "custom-manager-empty", tLegacy("還沒有資料來源。")));
      return;
    }
    latestSources.forEach((source, index) => customSourceList.append(renderSourceRow(source, index)));
  }

  async function runSourceAction(action) {
    setStatus(tLegacy("正在處理來源…"), "working");
    try {
      await action();
      await refreshAll();
      setStatus(tLegacy("來源已更新"), "success");
    } catch (error) {
      setStatus(tLegacy("來源操作失敗"), "error", error.message || "");
    }
  }

  async function moveSource(index, delta) {
    const targetIndex = index + delta;
    if (targetIndex < 0 || targetIndex >= latestSources.length) return;
    const current = latestSources[index];
    const target = latestSources[targetIndex];
    await runSourceAction(async () => {
      await fetchJsonOrThrow(`/api/custom-sources/${encodeURIComponent(current.sourceKey)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ card: { position: target.card.position } }),
      });
      await fetchJsonOrThrow(`/api/custom-sources/${encodeURIComponent(target.sourceKey)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ card: { position: current.card.position } }),
      });
    });
  }

  async function loadSources() {
    if (!isLocalConsole()) return;
    try {
      const result = await fetchJsonOrThrow("/api/custom-sources");
      renderSources(result.sources || result.Sources || []);
    } catch (error) {
      setStatus(tLegacy("資料來源管理讀取失敗"), "error", error.message || "");
    }
  }

  async function refreshAll() {
    await refresh();
    await loadSources();
    await loadWindowsNotificationStatus();
  }

  function onClick(element, handler) {
    if (!element) return;
    element.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      handler(event);
    });
  }

  customPageTabs?.querySelectorAll("button").forEach(button => {
    button.addEventListener("click", () => setPage(button.dataset.sideboardPage));
  });
  onClick(customRefresh, () => refreshAll());
  onClick(customSettingsButton, () => {
    const opening = !isPanelOpen(customCardSettingsPanel);
    setSettingsPanelVisible(opening);
    if (opening) setManagerVisible(false);
  });
  onClick(customSettingsClose, () => setSettingsPanelVisible(false));
  customCardSettingsForm?.addEventListener("submit", saveCardSettings);
  customSettingsCard?.addEventListener("change", () => {
    settingsCardId = customSettingsCard.value;
    applySettingsCardForm();
  });
  onClick(customSettingsClear, () => clearSelectedCard());
  onClick(customManageButton, () => {
    const opening = !isPanelOpen(customSourcesManager);
    setManagerVisible(opening);
    if (opening) setSettingsPanelVisible(false);
  });
  onClick(customManagerClose, () => setManagerVisible(false));
  onClick(customAddSource, () => {
    setSettingsPanelVisible(false);
    showForm();
  });
  onClick(customManagerAdd, () => showForm());
  customSourceForm?.addEventListener("submit", submitForm);
  onClick(customSourceCancel, () => hideForm());
  onClick(customCredentialCopy, () => copyCredential());
  onClick(customCredentialClose, () => hideCredential());
  onClick(windowsNotificationEnable, () => setWindowsNotificationEnabled(true));
  onClick(windowsNotificationDisable, () => setWindowsNotificationEnabled(false));

  // 啟動時強制預設收合（含 inline style，避免 CSS 殘留）
  setPanelOpen(customCardSettingsPanel, false);
  setPanelOpen(customSourcesManager, false);
  setPanelOpen(customSourceForm, false);
  setPanelOpen(customCredentialPanel, false);
  setPanelOpen(windowsNotificationControl, false);

  syncAccess();
  if (isEinkClient()) customCardsGrid?.classList.add("custom-cards-eink");
  setPage("system", false);

  return {
    refresh,
    refreshAll,
    loadSources,
    setPage,
    syncAccess,
    getPage: () => page,
    setSettingsPanelVisible,
    setManagerVisible,
    showForm,
  };
}
