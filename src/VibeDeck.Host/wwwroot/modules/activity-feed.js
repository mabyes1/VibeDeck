import { getIntlLocale, tLegacy } from "./i18n.js?v=4";

const FILTER_STORAGE_KEY = "vibeDeckActivityFilter.v1";

export function createActivityFeedController({ elements }) {
  const { card, list, filters, select } = elements;
  let workItems = [];
  let notificationItems = [];
  let activeFilter = localStorage.getItem(FILTER_STORAGE_KEY) || "all";
  let renderedKeys = new Set();
  let autoFollow = true;
  let scrollTop = 0;

  function itemText(item) {
    if (typeof item === "string") return item.trim();
    return String(item?.text || item?.title || item?.summary || item?.message || item?.name || "").trim();
  }

  function itemTime(item, fallback = null) {
    return item?.occurredAt || item?.receivedAt || item?.timestamp || item?.updatedAt || item?.time || fallback;
  }

  function parsedTime(value) {
    const result = Date.parse(value || "");
    return Number.isFinite(result) ? result : 0;
  }

  function makeKey(type, item, text, index) {
    const identity = item?.id || item?.key || [itemTime(item) || "", item?.from || "", text].join("|");
    return `${type}:${identity || index}`;
  }

  function normalizeWorkPulse(workPulse) {
    const candidates = [
      ...(workPulse?.timeline || []),
      ...(workPulse?.recent || []),
      ...(workPulse?.focus || []),
      ...((workPulse?.todos?.open || []).map(text => ({ text }))),
      ...((workPulse?.todos?.done || []).map(text => ({ text }))),
    ];
    const seen = new Set();
    return candidates.flatMap((item, index) => {
      const text = itemText(item);
      if (!text || seen.has(text)) return [];
      seen.add(text);
      const time = itemTime(item);
      return [{
        key: makeKey("task", item, text, index),
        type: "task",
        source: tLegacy("Codex 任務"),
        from: "",
        text,
        time,
        order: parsedTime(time) || index + 1,
      }];
    });
  }

  function normalizeNotifications(cardData) {
    return (cardData?.content?.items || []).map((item, index) => {
      const text = itemText(item);
      const time = itemTime(item, cardData?.lastReceivedAt);
      return {
        key: makeKey("notification", item, text, index),
        type: "notification",
        source: tLegacy("Windows 通知"),
        from: String(item?.from || "").trim(),
        text,
        time,
        order: parsedTime(time) || index + 1,
        severity: ["success", "warning", "error"].includes(item?.severity) ? item.severity : "info",
      };
    }).filter(item => item.text);
  }

  function formatTime(value) {
    const date = new Date(value || "");
    return Number.isNaN(date.getTime())
      ? ""
      : date.toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit" });
  }

  function isAtLatest() {
    return list.scrollHeight - list.clientHeight - list.scrollTop <= 24;
  }

  function scrollToLatest() {
    list.scrollTop = Math.max(0, list.scrollHeight - list.clientHeight);
    scrollTop = list.scrollTop;
    autoFollow = true;
  }

  function filteredItems() {
    const merged = [...workItems, ...notificationItems]
      .filter(item => activeFilter === "all" || item.type === activeFilter)
      .sort((left, right) => left.order - right.order || left.key.localeCompare(right.key));
    return merged.slice(-40);
  }

  function render({ forceLatest = false } = {}) {
    const previousAutoFollow = autoFollow || isAtLatest();
    const previousScrollTop = list.scrollTop;
    const items = filteredItems();
    const nextKeys = new Set(items.map(item => item.key));
    const hasNewEvent = items.some(item => !renderedKeys.has(item.key));
    list.replaceChildren();

    for (const item of items) {
      const row = document.createElement("li");
      row.className = `activity-feed-item activity-${item.type} severity-${item.severity || "info"}`;
      const meta = document.createElement("div");
      meta.className = "activity-feed-meta";
      const source = document.createElement("span");
      source.className = "activity-source";
      source.textContent = item.source;
      meta.append(source);
      if (item.from) {
        const from = document.createElement("span");
        from.className = "activity-from";
        from.textContent = item.from;
        meta.append(from);
      }
      const timeText = formatTime(item.time);
      if (timeText) {
        const time = document.createElement("time");
        time.textContent = timeText;
        meta.append(time);
      }
      const text = document.createElement("div");
      text.className = "activity-feed-text";
      text.dataset.userContent = "";
      text.textContent = item.text;
      row.append(meta, text);
      list.append(row);
    }

    if (!items.length) {
      const empty = document.createElement("li");
      empty.className = "activity-feed-empty";
      empty.textContent = activeFilter === "task"
        ? tLegacy("目前沒有任務動態")
        : activeFilter === "notification"
          ? tLegacy("目前沒有 Windows 通知")
          : tLegacy("目前沒有活動動態");
      list.append(empty);
    }

    renderedKeys = nextKeys;
    requestAnimationFrame(() => {
      if (forceLatest || hasNewEvent || previousAutoFollow) {
        scrollToLatest();
      } else {
        const max = Math.max(0, list.scrollHeight - list.clientHeight);
        list.scrollTop = Math.min(previousScrollTop || scrollTop, max);
      }
    });
  }

  function setFilter(filter) {
    activeFilter = ["task", "notification"].includes(filter) ? filter : "all";
    localStorage.setItem(FILTER_STORAGE_KEY, activeFilter);
    syncFilterControls();
    render({ forceLatest: true });
  }

  function syncFilterControls() {
    filters.forEach(button => {
      const active = button.dataset.activityFilter === activeFilter;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
    if (select) select.value = activeFilter;
  }

  list.addEventListener("scroll", () => {
    scrollTop = list.scrollTop;
    autoFollow = isAtLatest();
  }, { passive: true });
  filters.forEach(button => button.addEventListener("click", () => setFilter(button.dataset.activityFilter)));
  select?.addEventListener("change", () => setFilter(select.value));
  setFilter(activeFilter);
  card?.classList.add("activity-feed-card");

  return {
    setWorkPulse(workPulse) {
      workItems = normalizeWorkPulse(workPulse);
      // A persisted task-only filter must not hide a healthy notification feed
      // when this machine currently has no task events.
      if (activeFilter === "task" && !workItems.length && notificationItems.length) {
        activeFilter = "all";
        localStorage.setItem(FILTER_STORAGE_KEY, activeFilter);
        syncFilterControls();
      }
      render();
    },
    setWindowsNotifications(cardData) {
      notificationItems = normalizeNotifications(cardData);
      // Notifications may arrive after the work-pulse refresh. If the user
      // previously left the feed on 任務, automatically reveal the available
      // notification stream instead of leaving an empty card on screen.
      if (activeFilter === "task" && !workItems.length && notificationItems.length) {
        activeFilter = "all";
        localStorage.setItem(FILTER_STORAGE_KEY, activeFilter);
        syncFilterControls();
      }
      render();
    },
  };
}
