import { tLegacy } from "./i18n.js?v=4";

const STORAGE_KEY = "vibeDeckMobileOverview.v1";
const DEFAULT_SECTIONS = [
  { key: "status", label: "系統狀態", visible: true },
  { key: "metrics", label: "核心指標", visible: true },
  { key: "activity", label: "最新活動", visible: true },
  { key: "quota", label: "五小時額度", visible: true },
];

function byId(id) {
  return document.getElementById(id);
}

function textOf(id, fallback = "--") {
  const value = byId(id)?.textContent?.trim();
  return value || fallback;
}

function labelFor(item) {
  return tLegacy(item.label);
}

function copyBar(sourceId, targetId) {
  const source = byId(sourceId);
  const target = byId(targetId);
  if (target) target.style.width = source?.style?.width || "0%";
}

function cleanClone(node) {
  const clone = node.cloneNode(true);
  clone.removeAttribute?.("id");
  clone.querySelectorAll?.("[id]").forEach(child => child.removeAttribute("id"));
  return clone;
}

function normalizedPreferences() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || "null");
    if (!Array.isArray(saved)) throw new Error("invalid mobile layout");
    const known = new Map(DEFAULT_SECTIONS.map(item => [item.key, item]));
    const result = saved
      .filter(item => known.has(item?.key))
      .map(item => ({ ...known.get(item.key), visible: item.visible !== false }));
    for (const item of DEFAULT_SECTIONS) {
      if (!result.some(savedItem => savedItem.key === item.key)) result.push({ ...item });
    }
    return result;
  } catch {
    return DEFAULT_SECTIONS.map(item => ({ ...item }));
  }
}

export function createMobileOverviewController() {
  const root = byId("mobileOverview");
  const sourceGrid = byId("systemSideboardPage");
  if (!root || !sourceGrid) return null;

  const activitySource = byId("activityFeedList");
  const activityPreview = byId("mobileActivityPreview");
  const activityList = byId("mobileActivityList");
  const activityPanel = byId("mobileActivityPanel");
  const detailsPanel = byId("mobileDetailsPanel");
  const layoutPanel = byId("mobileLayoutPanel");
  const layoutList = byId("mobileLayoutList");
  const customCardsSource = byId("customCardsGrid");
  let preferences = normalizedPreferences();
  let syncFrame = 0;

  function setText(id, value) {
    const node = byId(id);
    if (node) node.textContent = value;
  }

  function syncStatus() {
    const alert = byId("sideLoadAlert");
    const showingAlert = alert && !alert.hidden;
    setText("mobileSystemStatus", showingAlert ? textOf("sideLoadAlertTitle", tLegacy("系統壓力偏高")) : textOf("sideLoadStatus", tLegacy("系統狀態良好")));
    setText("mobileSystemReason", showingAlert ? textOf("sideLoadAlertReason", tLegacy("請查看詳細資訊")) : textOf("sideLoadStatusReason", tLegacy("目前沒有明顯瓶頸")));
    setText("mobileDetailHost", textOf("sideHost", tLegacy("主機 --")));
    setText("mobileDetailUptime", textOf("sideUptime", tLegacy("運行時間 --")));
    setText("mobileDetailHealth", textOf("sideHealth", tLegacy("等待資料")));

    const connection = document.querySelector("#sideboardView [data-eink-connection-state]");
    setText("mobileConnectionState", connection?.textContent?.trim() || tLegacy("正在連線"));
    byId("mobileConnectionState")?.classList.toggle("online", connection?.classList.contains("online"));
  }

  function syncMetrics() {
    [["sideCpu", "mobileCpu"], ["sideRam", "mobileRam"], ["sideGpu", "mobileGpu"], ["sideDisk", "mobileDisk"]]
      .forEach(([source, target]) => setText(target, textOf(source)));
    [["sideCpuBar", "mobileCpuBar"], ["sideRamBar", "mobileRamBar"], ["sideGpuBar", "mobileGpuBar"], ["sideDiskBar", "mobileDiskBar"]]
      .forEach(([source, target]) => copyBar(source, target));

    setText("mobileVram", textOf("sideVram"));
    setText("mobileVramSub", textOf("sideVramSub"));
    setText("mobileNetwork", textOf("sideNet"));
    setText("mobileNetworkSub", textOf("sideNetSub"));
    setText("mobileWeather", textOf("sideWeather", tLegacy("天氣 --")));
    setText("mobileWeatherSub", textOf("sideWeatherSub", tLegacy("體感 --")));
    setText("mobileDiskIo", textOf("sideDiskIo", tLegacy("磁碟 IO --")));

    const processTarget = byId("mobileProcessList");
    if (processTarget) {
      processTarget.replaceChildren(...[...(byId("sideProcessList")?.children || [])].map(cleanClone));
    }

    const customTarget = byId("mobileCustomCards");
    const customSection = byId("mobileCustomCardsSection");
    const customCards = [...(byId("customCardsGrid")?.children || [])].filter(node => !node.hidden);
    if (customTarget && customSection) {
      customTarget.replaceChildren(...customCards.map(card => {
        const article = document.createElement("article");
        article.textContent = card.innerText?.trim() || tLegacy("自訂卡片");
        return article;
      }));
      customSection.hidden = !customCards.length;
    }
  }

  function syncActivity() {
    const rows = [...(activitySource?.children || [])];
    if (activityPreview) {
      activityPreview.replaceChildren(...rows.slice(-2).map(cleanClone));
      if (!rows.length) {
        const empty = document.createElement("li");
        empty.textContent = tLegacy("目前沒有活動動態");
        activityPreview.append(empty);
      }
    }

    if (activityList) {
      const followLatest = activityPanel && !activityPanel.hidden &&
        activityList.scrollHeight - activityList.clientHeight - activityList.scrollTop < 36;
      activityList.replaceChildren(...rows.map(cleanClone));
      if (followLatest) requestAnimationFrame(() => { activityList.scrollTop = activityList.scrollHeight; });
    }

    const originalFilters = [...document.querySelectorAll("[data-activity-filter]")];
    document.querySelectorAll("[data-mobile-activity-filter]").forEach(button => {
      const active = originalFilters.find(item => item.dataset.activityFilter === button.dataset.mobileActivityFilter)?.getAttribute("aria-pressed") === "true";
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
  }

  function syncQuota() {
    const select = byId("quotaMiniSource");
    const selected = select?.selectedOptions?.[0]?.textContent?.trim();
    setText("mobileQuotaSource", selected || tLegacy("AI 額度"));
    setText("mobileQuotaValue", textOf("quotaMiniValue"));
    setText("mobileQuotaState", textOf("quotaMiniState", tLegacy("等待來源")));
    setText("mobileQuotaReset", `${textOf("quotaMiniReset", tLegacy("查看額度"))} ›`);
  }

  function sync() {
    syncFrame = 0;
    syncStatus();
    syncMetrics();
    syncActivity();
    syncQuota();
  }

  function scheduleSync() {
    if (!syncFrame) syncFrame = requestAnimationFrame(sync);
  }

  function openPanel(panel) {
    [activityPanel, detailsPanel, layoutPanel].forEach(item => { if (item) item.hidden = item !== panel; });
    document.body.classList.add("mobile-detail-open");
    if (panel === activityPanel) requestAnimationFrame(() => { activityList.scrollTop = activityList.scrollHeight; });
  }

  function closePanels() {
    [activityPanel, detailsPanel, layoutPanel].forEach(panel => { if (panel) panel.hidden = true; });
    document.body.classList.remove("mobile-detail-open");
  }

  function applyPreferences() {
    const sections = new Map([...root.querySelectorAll("[data-mobile-section]")].map(node => [node.dataset.mobileSection, node]));
    preferences.forEach((item, index) => {
      const section = sections.get(item.key);
      if (!section) return;
      section.style.order = String(index + 1);
      section.hidden = item.visible === false;
    });
  }

  function renderLayoutEditor() {
    if (!layoutList) return;
    layoutList.replaceChildren();
    preferences.forEach((item, index) => {
      const row = document.createElement("div");
      row.className = "mobile-layout-row";
      const label = document.createElement("label");
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.checked = item.visible !== false;
      checkbox.addEventListener("change", () => { item.visible = checkbox.checked; });
      label.append(checkbox, document.createTextNode(labelFor(item)));
      const actions = document.createElement("div");
      const up = document.createElement("button");
      up.type = "button";
      up.textContent = "↑";
      up.disabled = index === 0;
      up.addEventListener("click", () => {
        [preferences[index - 1], preferences[index]] = [preferences[index], preferences[index - 1]];
        renderLayoutEditor();
      });
      const down = document.createElement("button");
      down.type = "button";
      down.textContent = "↓";
      down.disabled = index === preferences.length - 1;
      down.addEventListener("click", () => {
        [preferences[index + 1], preferences[index]] = [preferences[index], preferences[index + 1]];
        renderLayoutEditor();
      });
      actions.append(up, down);
      row.append(label, actions);
      layoutList.append(row);
    });
  }

  byId("mobileActivityOpen")?.addEventListener("click", () => openPanel(activityPanel));
  byId("mobileDetailsOpen")?.addEventListener("click", () => openPanel(detailsPanel));
  byId("mobileCustomize")?.addEventListener("click", () => {
    preferences = normalizedPreferences();
    renderLayoutEditor();
    openPanel(layoutPanel);
  });
  byId("mobileFullscreen")?.addEventListener("click", () => byId("fullscreen")?.click());
  byId("mobileQuotaOpen")?.addEventListener("click", () => byId("quotaMode")?.click());
  root.querySelectorAll("[data-mobile-panel-close]").forEach(button => button.addEventListener("click", closePanels));
  root.querySelectorAll("[data-mobile-activity-filter]").forEach(button => button.addEventListener("click", () => {
    document.querySelector(`[data-activity-filter="${button.dataset.mobileActivityFilter}"]`)?.click();
    scheduleSync();
  }));
  byId("mobileLayoutSave")?.addEventListener("click", () => {
    if (!preferences.some(item => item.visible !== false)) preferences[0].visible = true;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
    applyPreferences();
    closePanels();
  });
  byId("mobileLayoutReset")?.addEventListener("click", () => {
    preferences = DEFAULT_SECTIONS.map(item => ({ ...item }));
    renderLayoutEditor();
  });

  const observer = new MutationObserver(scheduleSync);
  observer.observe(sourceGrid, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["class", "hidden", "style"] });
  if (customCardsSource) {
    observer.observe(customCardsSource, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["class", "hidden"] });
  }
  const connection = document.querySelector("#sideboardView [data-eink-connection-state]");
  if (connection) observer.observe(connection, { subtree: true, characterData: true, attributes: true, attributeFilter: ["class"] });
  applyPreferences();
  sync();

  return { sync, closePanels };
}
