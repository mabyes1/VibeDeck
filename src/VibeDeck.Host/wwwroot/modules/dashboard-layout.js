import { tLegacy } from "./i18n.js?v=4";
import { confirmAction } from "./ui-confirm.js?v=1";
import { applyFeedbackState } from "./feedback-state.js?v=1";

const SYSTEM_CARD_TITLES = {
  "system-load": "系統狀態",
  cpu: "CPU",
  ram: "RAM",
  gpu: "GPU",
  vram: "VRAM",
  disk: "磁碟",
  network: "網路",
  "weather-io": "天氣與 IO",
  processes: "記憶體排行",
  "activity-feed": "活動動態",
  "quota-mini": "AI 額度",
};

export function createDashboardLayoutController({
  fetchJsonOrThrow,
  isEinkClient,
  openCardSettings,
  openSourceManager,
  openSourceForm,
  closeConfig,
}) {
  const grid = document.getElementById("systemSideboardPage");
  const shell = document.getElementById("sideboardShell");
  const editToggle = document.getElementById("dashboardEditToggle");
  const editBar = document.getElementById("dashboardEditBar");
  const selectionControls = document.getElementById("dashboardSelectionControls");
  const editStatus = document.getElementById("dashboardEditStatus");
  const addCard = document.getElementById("dashboardAddCard");
  const cardLibrary = document.getElementById("dashboardCardLibrary");
  const saveButton = document.getElementById("dashboardSaveLayout");
  const resetButton = document.getElementById("dashboardResetLayout");
  const compactButton = document.getElementById("dashboardCompactLayout");
  const settingsButton = document.getElementById("customSettingsButton");
  const managerButton = document.getElementById("customManageButton");
  const addSourceButton = document.getElementById("customAddSource");
  const configClose = document.getElementById("dashboardConfigClose");

  let profile = getProfile();
  let items = [];
  let revision = 0;
  let editing = false;
  let saving = false;
  let autoSaveTimer = 0;
  let autoSaveQueued = false;
  let lastSavedSignature = "";
  let resizeTimer = 0;
  let selectedKey = "";
  let loadGeneration = 0;
  let layoutLoaded = false;

  function getProfile() {
    const bounds = shell.getBoundingClientRect();
    const width = bounds.width || window.innerWidth;
    const height = bounds.height || window.innerHeight;
    if (isEinkClient()) return width >= height ? "eink-landscape" : "eink-portrait";
    return "default";
  }

  function maxRows() {
    return profile.endsWith("portrait") ? 10 : 6;
  }

  function cardNodes() {
    return [...grid.querySelectorAll("[data-dashboard-key]")];
  }

  function itemFor(key) {
    return items.find(item => item.key === key);
  }

  function cloneItem(item) {
    return {
      key: String(item.key),
      visible: item.visible !== false,
      column: Number(item.column) || 0,
      row: Number(item.row) || 0,
      width: Math.max(1, Number(item.width) || 2),
      height: Math.max(1, Number(item.height) || 2),
    };
  }

  function layoutSignature(source = items) {
    return JSON.stringify(source
      .map(cloneItem)
      .sort((left, right) => left.key.localeCompare(right.key)));
  }

  function addMissingCards() {
    let added = false;
    for (const node of cardNodes()) {
      const key = node.dataset.dashboardKey;
      if (itemFor(key)) continue;
      items.push({
        key,
        visible: false,
        column: 0,
        row: Math.max(0, maxRows() - 2),
        width: key === "activity-feed" ? 8 : 4,
        height: 2,
      });
      added = true;
    }
    return added;
  }

  function titleFor(key, node = null) {
    return tLegacy(node?.dataset.dashboardTitle || SYSTEM_CARD_TITLES[key] || "自訂卡片");
  }

  function overlaps(a, b) {
    return a.column < b.column + b.width &&
      a.column + a.width > b.column &&
      a.row < b.row + b.height &&
      a.row + a.height > b.row;
  }

  function canPlace(candidate, ignoreKey) {
    if (candidate.column < 0 || candidate.row < 0 || candidate.column + candidate.width > 12 || candidate.row + candidate.height > maxRows()) return false;
    return !items.some(item => item.visible && item.key !== ignoreKey && overlaps(candidate, item));
  }

  function canPlaceExcluding(candidate, ignoredKeys) {
    if (candidate.column < 0 || candidate.row < 0 || candidate.column + candidate.width > 12 || candidate.row + candidate.height > maxRows()) return false;
    return !items.some(item => item.visible && !ignoredKeys.has(item.key) && overlaps(candidate, item));
  }

  function findNearestSlot(item, preferredColumn = item.column, preferredRow = item.row) {
    const candidates = [];
    for (let row = 0; row <= maxRows() - item.height; row += 1) {
      for (let column = 0; column <= 12 - item.width; column += 1) {
        candidates.push({
          column,
          row,
          distance: Math.abs(column - preferredColumn) + Math.abs(row - preferredRow) * 2,
        });
      }
    }
    candidates.sort((a, b) => a.distance - b.distance || a.row - b.row || a.column - b.column);
    const slot = candidates.find(candidate => canPlace({ ...item, ...candidate }, item.key));
    return slot || null;
  }

  function removeEditorChrome(node) {
    node.querySelector(":scope > .dashboard-card-editor")?.remove();
  }

  function makeButton(label, title, handler, className = "") {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    button.title = title;
    button.setAttribute("aria-label", title);
    if (className) button.className = className;
    button.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      handler();
    });
    return button;
  }

  function makeSizeControl(label, decrementTitle, incrementTitle, decrement, increment) {
    const group = document.createElement("div");
    group.className = "dashboard-size-control";
    const caption = document.createElement("span");
    caption.textContent = tLegacy(label);
    group.append(
      caption,
      makeButton("−", tLegacy(decrementTitle), decrement),
      makeButton("＋", tLegacy(incrementTitle), increment),
    );
    return group;
  }

  function beginDrag(event, handle, node, item) {
      if (!editing || event.isPrimary === false || event.button > 0) return;
      event.preventDefault();
      event.stopPropagation();
      handle.setPointerCapture?.(event.pointerId);
      const startX = event.clientX;
      const startY = event.clientY;
      const original = { column: item.column, row: item.row };
      node.classList.add("dashboard-card-dragging");

      const move = moveEvent => {
        const rect = grid.getBoundingClientRect();
        const style = getComputedStyle(grid);
        const gap = Number.parseFloat(style.columnGap) || 8;
        const columnWidth = Math.max(1, (rect.width - gap * 11) / 12);
        const rowHeight = Math.max(1, (rect.height - gap * (maxRows() - 1)) / maxRows());
        item.column = Math.max(0, Math.min(12 - item.width, original.column + Math.round((moveEvent.clientX - startX) / (columnWidth + gap))));
        item.row = Math.max(0, Math.min(maxRows() - item.height, original.row + Math.round((moveEvent.clientY - startY) / (rowHeight + gap))));
        applyNodeLayout(node, item);
      };

      const end = () => {
        document.removeEventListener("pointermove", move, true);
        document.removeEventListener("pointerup", end, true);
        document.removeEventListener("pointercancel", cancel, true);
        if (handle.hasPointerCapture?.(event.pointerId)) handle.releasePointerCapture(event.pointerId);
        node.classList.remove("dashboard-card-dragging");
        const collisions = items.filter(other => other.visible && other.key !== item.key && overlaps(item, other));
        if (collisions.length === 1)
        {
          const target = collisions[0];
          const targetAtOriginal = { ...target, column: original.column, row: original.row };
          const ignored = new Set([item.key, target.key]);
          if (canPlaceExcluding(targetAtOriginal, ignored)) {
            target.column = original.column;
            target.row = original.row;
          } else {
            const slot = findNearestSlot(item, item.column, item.row);
            if (slot) Object.assign(item, slot);
            else Object.assign(item, original);
          }
        } else {
          const slot = findNearestSlot(item, item.column, item.row);
          if (slot) Object.assign(item, slot);
          else Object.assign(item, original);
        }
        commitLayoutChange(tLegacy("正在自動儲存版面…"));
      };

      const cancel = () => {
        Object.assign(item, original);
        end();
      };

      document.addEventListener("pointermove", move, { capture: true, passive: false });
      document.addEventListener("pointerup", end, { capture: true, once: true });
      document.addEventListener("pointercancel", cancel, { capture: true, once: true });
  }

  function attachDrag(handle, node, item) {
    handle.addEventListener("pointerdown", event => {
      beginDrag(event, handle, node, item);
    });
  }

  function compactLayout() {
    const visible = items
      .filter(item => item.visible)
      .sort((a, b) => a.row - b.row || a.column - b.column);
    const original = new Map(visible.map(item => [item.key, { column: item.column, row: item.row }]));
    const placed = [];
    for (const item of visible) {
      let slot = null;
      for (let row = 0; row <= maxRows() - item.height && !slot; row += 1) {
        for (let column = 0; column <= 12 - item.width; column += 1) {
          const candidate = { ...item, row, column };
          if (!placed.some(other => overlaps(candidate, other))) {
            slot = { row, column };
            break;
          }
        }
      }
      if (!slot) {
        for (const current of visible) Object.assign(current, original.get(current.key));
        setStatus(tLegacy("目前卡片尺寸無法重新排列；請先縮小一張卡片。"), "error");
        return false;
      }
      Object.assign(item, slot);
      placed.push(item);
    }
    return visible.some(item => {
      const before = original.get(item.key);
      return item.column !== before.column || item.row !== before.row;
    });
  }

  function resizeCard(item, axis, direction) {
    const previous = { column: item.column, row: item.row, width: item.width, height: item.height };
    if (axis === "width") {
      item.width = Math.max(2, Math.min(12, item.width + direction));
    } else {
      item.height = Math.max(1, Math.min(Math.min(8, maxRows()), item.height + direction));
    }
    if (item.width === previous.width && item.height === previous.height) return;
    item.column = Math.min(item.column, 12 - item.width);
    item.row = Math.min(item.row, maxRows() - item.height);
    const slot = findNearestSlot(item, item.column, item.row);
    if (slot) Object.assign(item, slot);
    else {
      Object.assign(item, previous);
      setStatus(tLegacy("這個尺寸放不下；請先隱藏或移動相鄰卡片。"), "error");
    }
    commitLayoutChange(tLegacy("正在自動儲存版面…"));
  }

  function renderSelectionControls(nodes) {
    selectionControls.replaceChildren();
    selectionControls.hidden = !editing;
    if (!editing) return;
    let item = itemFor(selectedKey);
    if (!item?.visible) {
      item = items.find(candidate => candidate.visible) || null;
      selectedKey = item?.key || "";
    }
    const node = selectedKey ? nodes.get(selectedKey) : null;
    if (!item || !node) {
      selectionControls.hidden = true;
      return;
    }
    const title = document.createElement("strong");
    title.textContent = titleFor(item.key, node);
    const handle = makeButton(tLegacy("移動"), `${tLegacy("移動")}${titleFor(item.key, node)}`, () => {}, "dashboard-drag-handle");
    attachDrag(handle, node, item);
    selectionControls.append(
      title,
      handle,
      makeSizeControl("寬度", "寬度減少一格", "寬度增加一格", () => resizeCard(item, "width", -1), () => resizeCard(item, "width", 1)),
      makeSizeControl("高度", "高度減少一格", "高度增加一格", () => resizeCard(item, "height", -1), () => resizeCard(item, "height", 1)),
      makeButton(tLegacy("移出版面"), tLegacy("從目前版面隱藏"), () => {
        if (items.filter(candidate => candidate.visible).length <= 1) {
          setStatus(tLegacy("資訊板至少要保留一張可見卡片。"), "error");
          return;
        }
        item.visible = false;
        selectedKey = "";
        compactLayout();
        commitLayoutChange(tLegacy("正在自動儲存版面…"));
      }, "dashboard-remove-card"),
    );
  }

  function applyNodeLayout(node, item) {
    node.style.gridColumn = `${item.column + 1} / span ${item.width}`;
    node.style.gridRow = `${item.row + 1} / span ${item.height}`;
  }

  function apply() {
    // Remote clients restore their device credential asynchronously. Do not
    // turn the server-rendered cards into an empty board while the first
    // authenticated layout request is still pending (or was rejected).
    if (!layoutLoaded) return false;
    const addedMissingCards = addMissingCards();
    grid.style.setProperty("--dashboard-row-count", String(maxRows()));
    const nodes = new Map(cardNodes().map(node => [node.dataset.dashboardKey, node]));
    for (const [key, node] of nodes) {
      const item = itemFor(key);
      const visible = Boolean(item?.visible);
      removeEditorChrome(node);
      node.hidden = !visible;
      node.classList.toggle("dashboard-card-selected", editing && visible && key === selectedKey);
      node.toggleAttribute("aria-hidden", !visible);
      if (editing && visible) node.setAttribute("aria-selected", key === selectedKey ? "true" : "false");
      else node.removeAttribute("aria-selected");
      if (editing && visible) node.tabIndex = 0;
      else node.removeAttribute("tabindex");
      if (item) applyNodeLayout(node, item);
    }
    renderSelectionControls(nodes);
    renderLibrary(nodes);
    updateStatus();
    return addedMissingCards;
  }

  function renderLibrary(nodes = new Map(cardNodes().map(node => [node.dataset.dashboardKey, node]))) {
    cardLibrary.replaceChildren();
    const hiddenItems = items.filter(item => !item.visible && nodes.has(item.key));
    if (!hiddenItems.length) {
      const empty = document.createElement("span");
      empty.className = "dashboard-library-empty";
      empty.textContent = tLegacy("所有可用卡片都已在版面上。");
      cardLibrary.append(empty);
      return;
    }
    for (const item of hiddenItems) {
      const button = makeButton(`＋ ${titleFor(item.key, nodes.get(item.key))}`, tLegacy("加入目前版面"), () => {
        const slot = findNearestSlot(item, 0, 0);
        if (!slot) {
          setStatus(tLegacy("目前單屏已滿，請先隱藏或縮小其他卡片。"), "error");
          return;
        }
        Object.assign(item, slot, { visible: true });
        cardLibrary.hidden = true;
        commitLayoutChange(tLegacy("正在自動儲存版面…"));
      });
      cardLibrary.append(button);
    }
  }

  function setStatus(message, level = "", detail = "") {
    applyFeedbackState(editStatus, { message, state: level || "info", detail });
  }

  function scheduleAutoSave(reason = "", force = false) {
    if (!layoutLoaded) return;
    if (!items.some(item => item.visible)) {
      setStatus(tLegacy("資訊板至少要保留一張可見卡片。"), "error");
      return;
    }
    if (saving) {
      autoSaveQueued = true;
      return;
    }
    if (!force && layoutSignature() === lastSavedSignature) return;
    clearTimeout(autoSaveTimer);
    setStatus(reason || tLegacy("正在自動儲存版面…"), "working");
    autoSaveTimer = setTimeout(() => save({ automatic: true }), 450);
  }

  function commitLayoutChange(reason) {
    apply();
    scheduleAutoSave(reason || tLegacy("正在自動儲存版面…"));
  }

  function updateStatus() {
    const visible = items.filter(item => item.visible).length;
    const profileLabel = profile === "eink-landscape" ? "電子書橫向"
      : profile === "eink-portrait" ? "電子書直向"
      : "一般版面";
    setStatus(`${tLegacy(profileLabel)} · ${visible} ${tLegacy("張卡片")} · ${tLegacy("單屏")} ${maxRows()} ${tLegacy("列")}`);
  }

  function setEditing(next) {
    editing = Boolean(next);
    shell.classList.toggle("dashboard-editing", editing);
    document.body.classList.toggle("dashboard-edit-mode", editing);
    editToggle.setAttribute("aria-pressed", editing ? "true" : "false");
    editToggle.textContent = editing ? tLegacy("取消編輯") : tLegacy("編輯版面");
    editBar.hidden = !editing;
    cardLibrary.hidden = true;
    if (editing && !itemFor(selectedKey)?.visible) selectedKey = items.find(item => item.visible)?.key || "";
    if (!editing) {
      selectedKey = "";
      closeConfig?.();
    }
    apply();
  }

  async function load() {
    const requestedProfile = getProfile();
    const generation = ++loadGeneration;
    profile = requestedProfile;
    layoutLoaded = false;
    try {
      const result = await fetchJsonOrThrow(`/api/dashboard/layout?profile=${encodeURIComponent(requestedProfile)}`);
      if (generation !== loadGeneration || getProfile() !== requestedProfile) return;
      profile = requestedProfile;
      items = (result.items || result.Items || []).map(cloneItem);
      revision = Number(result.revision ?? result.Revision) || 0;
      layoutLoaded = true;
      const addedMissingCards = apply();
      lastSavedSignature = addedMissingCards || revision === 0 ? "" : layoutSignature();
      if (addedMissingCards || revision === 0) {
        scheduleAutoSave(tLegacy("正在建立可保存的版面…"), true);
      }
    } catch (error) {
      setStatus(tLegacy("版面讀取失敗"), "error", error.message || "");
    }
  }

  async function save(options = {}) {
    const automatic = options.automatic === true;
    if (!layoutLoaded || !items.some(item => item.visible)) {
      setStatus(tLegacy("資訊板至少要保留一張可見卡片。"), "error");
      return;
    }
    const targetProfile = profile;
    const currentSignature = layoutSignature();
    if (saving) {
      autoSaveQueued = true;
      return;
    }
    if (currentSignature === lastSavedSignature) {
      if (!automatic) {
        setStatus(tLegacy("版面已儲存"), "success");
        setEditing(false);
      }
      return;
    }

    const payloadItems = items.map(cloneItem);
    const payloadSignature = layoutSignature(payloadItems);
    let saveSucceeded = false;
    saving = true;
    if (!automatic) saveButton.disabled = true;
    try {
      const result = await fetchJsonOrThrow("/api/dashboard/layout", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ profile: targetProfile, items: payloadItems }),
      });
      revision = Number(result.revision ?? result.Revision) || revision;
      saveSucceeded = true;
      if (profile === targetProfile && layoutSignature() === payloadSignature) {
        items = (result.items || result.Items || []).map(cloneItem);
        lastSavedSignature = layoutSignature();
      } else if (profile === targetProfile) {
        lastSavedSignature = payloadSignature;
        autoSaveQueued = true;
      }
      if (automatic) {
        setStatus(tLegacy("版面已自動儲存"), "success");
      } else {
        setEditing(false);
      }
    } catch (error) {
      setStatus(tLegacy("版面儲存失敗"), "error", error.message || "");
    } finally {
      saving = false;
      if (!automatic) saveButton.disabled = false;
      if (saveSucceeded && (autoSaveQueued || layoutSignature() !== lastSavedSignature)) {
        autoSaveQueued = false;
        scheduleAutoSave(tLegacy("正在自動儲存最新調整…"), true);
      } else if (!saveSucceeded) {
        // A rejected payload must not become a sub-second retry loop. A later
        // explicit edit or authenticated reload can try again with valid data.
        autoSaveQueued = false;
      }
    }
  }

  async function reset() {
    if (!await confirmAction({
      title: tLegacy("恢復預設"),
      message: tLegacy("恢復這個裝置方向的預設版面？"),
      confirmLabel: tLegacy("恢復預設"),
      cancelLabel: tLegacy("取消"),
      tone: "danger"
    })) return;
    setStatus(tLegacy("正在恢復預設版面…"), "working");
    try {
      const result = await fetchJsonOrThrow(`/api/dashboard/layout/reset?profile=${encodeURIComponent(profile)}`, { method: "POST" });
      items = (result.items || result.Items || []).map(cloneItem);
      revision = Number(result.revision ?? result.Revision) || revision;
      const addedMissingCards = apply();
      lastSavedSignature = addedMissingCards ? "" : layoutSignature();
      if (addedMissingCards) scheduleAutoSave(tLegacy("正在保存新增卡片…"), true);
      else setStatus(tLegacy("已恢復預設版面"), "success");
    } catch (error) {
      setStatus(tLegacy("版面重設失敗"), "error", error.message || "");
    }
  }

  editToggle?.addEventListener("click", () => setEditing(!editing));
  addCard?.addEventListener("click", () => {
    cardLibrary.hidden = !cardLibrary.hidden;
    if (!cardLibrary.hidden) renderLibrary();
  });
  saveButton?.addEventListener("click", () => save({ automatic: false }));
  resetButton?.addEventListener("click", reset);
  compactButton?.addEventListener("click", () => {
    const moved = compactLayout();
    if (moved) commitLayoutChange(tLegacy("正在自動儲存版面…"));
    else {
      apply();
      setStatus(tLegacy("版面已經沒有可向上收合的空白。"), "");
    }
  });
  settingsButton?.addEventListener("click", () => openCardSettings?.());
  managerButton?.addEventListener("click", () => openSourceManager?.());
  addSourceButton?.addEventListener("click", () => openSourceForm?.());
  configClose?.addEventListener("click", () => closeConfig?.());
  grid.addEventListener("click", event => {
    if (!editing) return;
    const node = event.target.closest("[data-dashboard-key]");
    if (!node || !grid.contains(node)) return;
    event.preventDefault();
    event.stopPropagation();
    selectedKey = node.dataset.dashboardKey || "";
    apply();
  }, true);
  grid.addEventListener("pointerdown", event => {
    if (!editing) return;
    const node = event.target.closest("[data-dashboard-key]");
    const item = node ? itemFor(node.dataset.dashboardKey || "") : null;
    if (!node || !item?.visible || item.key !== selectedKey) return;
    beginDrag(event, node, node, item);
  }, true);
  grid.addEventListener("keydown", event => {
    if (!editing || !["Enter", " "].includes(event.key)) return;
    const node = event.target.closest("[data-dashboard-key]");
    if (!node || !grid.contains(node)) return;
    event.preventDefault();
    selectedKey = node.dataset.dashboardKey || "";
    apply();
  });
  document.addEventListener("dashboard:cards-changed", () => {
    if (!layoutLoaded) return;
    const addedMissingCards = apply();
    if (addedMissingCards) scheduleAutoSave(tLegacy("正在保存新增卡片…"), true);
  });
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      const nextProfile = getProfile();
      if (nextProfile !== profile) load();
      else apply();
    }, 180);
  });
  return {
    load,
    loadIfNeeded: () => layoutLoaded ? Promise.resolve() : load(),
    apply,
    save,
    setEditing,
    isEditing: () => editing,
    isLoaded: () => layoutLoaded,
  };
}
