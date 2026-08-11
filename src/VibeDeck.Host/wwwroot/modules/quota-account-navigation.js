export function quotaAccountKey(item, tabId = "") {
  if (!item) return "";
  if (tabId === "agy") {
    return String(item.id || item.accountId || item.email || "").toLowerCase();
  }
  return String(
    item.AccountId || item.accountId ||
    item.AccountEmail || item.accountEmail ||
    item.Id || item.id || ""
  ).toLowerCase();
}

export function sortQuotaAccountsByRecentUse(items) {
  return [...(items || [])].sort((left, right) => {
    const leftObserved = Date.parse(left?.ObservedAt || left?.observedAt || "") || 0;
    const rightObserved = Date.parse(right?.ObservedAt || right?.observedAt || "") || 0;
    if (leftObserved !== rightObserved) return rightObserved - leftObserved;
    const leftLabel = left?.AccountEmail || left?.accountEmail || left?.Label || left?.label || left?.Id || left?.id || "";
    const rightLabel = right?.AccountEmail || right?.accountEmail || right?.Label || right?.label || right?.Id || right?.id || "";
    return String(leftLabel).localeCompare(String(rightLabel));
  });
}

export function resolveQuotaAccountSelection(items, tabId, currentIndex = 0, selectedKey = "") {
  const list = Array.isArray(items) ? items : [];
  if (!list.length) return { index: 0, key: "" };

  if (selectedKey) {
    const matched = list.findIndex(item => quotaAccountKey(item, tabId) === selectedKey);
    if (matched >= 0) return { index: matched, key: selectedKey };
  }

  const normalized = ((Number(currentIndex) || 0) % list.length + list.length) % list.length;
  return { index: normalized, key: quotaAccountKey(list[normalized], tabId) };
}

export function moveQuotaAccountSelection(items, tabId, currentIndex, selectedKey, delta) {
  const list = Array.isArray(items) ? items : [];
  if (!list.length) return { index: 0, key: "" };
  const current = resolveQuotaAccountSelection(list, tabId, currentIndex, selectedKey);
  const next = ((current.index + delta) % list.length + list.length) % list.length;
  return { index: next, key: quotaAccountKey(list[next], tabId) };
}

export function createQuotaAccountNavigator() {
  const indexByTab = Object.create(null);
  const keyByTab = Object.create(null);

  function getIndex(tabId, items) {
    const selection = resolveQuotaAccountSelection(
      items,
      tabId,
      indexByTab[tabId] || 0,
      keyByTab[tabId] || ""
    );
    indexByTab[tabId] = selection.index;
    keyByTab[tabId] = selection.key;
    return selection.index;
  }

  function move(tabId, items, delta) {
    const next = moveQuotaAccountSelection(
      items,
      tabId,
      indexByTab[tabId] || 0,
      keyByTab[tabId] || "",
      delta
    );
    indexByTab[tabId] = next.index;
    keyByTab[tabId] = next.key;
    return next.index;
  }

  return { getIndex, move };
}
