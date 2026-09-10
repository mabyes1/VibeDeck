function readField(value, pascal, camel, fallback = "") {
  return value?.[pascal] ?? value?.[camel] ?? fallback;
}

export function deckMode(id) {
  const value = String(id || "").trim();
  return value ? `deck:${value}` : "deck";
}

export function deckIdFromMode(mode) {
  const value = String(mode || "");
  return value.startsWith("deck:") ? value.slice(5) : "";
}

export function normalizeDeckCatalog(payload = {}) {
  const rawDecks = payload.Decks ?? payload.decks ?? [];
  const rawIssues = payload.Issues ?? payload.issues ?? [];
  return {
    rootPath: readField(payload, "RootPath", "rootPath", ""),
    decks: Array.isArray(rawDecks) ? rawDecks.map(item => ({
      id: readField(item, "Id", "id", ""),
      name: readField(item, "Name", "name", ""),
      entry: readField(item, "Entry", "entry", ""),
      icon: readField(item, "Icon", "icon", ""),
      type: readField(item, "Type", "type", "static"),
      url: readField(item, "Url", "url", ""),
    })).filter(item => item.id && item.name && item.url) : [],
    issues: Array.isArray(rawIssues) ? rawIssues.map(item => ({
      folder: readField(item, "Folder", "folder", ""),
      message: readField(item, "Message", "message", ""),
    })).filter(item => item.folder || item.message) : [],
  };
}

export function deckSandbox(type, url, hostOrigin = "") {
  if (type !== "embed") return type === "proxy" ? "allow-scripts allow-forms" : "allow-scripts";
  try {
    const targetOrigin = new URL(url, hostOrigin || "http://localhost").origin;
    if (hostOrigin && targetOrigin === hostOrigin) return "allow-scripts allow-forms";
  } catch {}
  return "allow-scripts allow-forms allow-same-origin";
}

export function isDeckDataPath(value, hostOrigin = "http://localhost") {
  try {
    const url = new URL(String(value || ""), hostOrigin);
    const expectedOrigin = new URL(hostOrigin).origin;
    return url.origin === expectedOrigin && url.pathname.startsWith("/api/deck-data/");
  } catch {
    return false;
  }
}

export function createCustomDeckController({
  switcher,
  addButton,
  frame,
  help,
  folderPath,
  status,
  issues,
  openFolderButton,
  openExampleButton,
  refreshButton,
  fetchJsonOrThrow,
  navigate,
  getActiveMode,
  isLocalRequest,
  exitViewer,
  toggleViewer,
  getEnvironment,
  shouldPoll = () => true,
}) {
  let catalog = { rootPath: "", decks: [], issues: [] };
  let pollTimer = null;
  let refreshing = false;

  function syncEnvironment() {
    if (!frame || frame.hidden || !frame.contentWindow) return;
    let environment = {};
    try {
      environment = getEnvironment?.() || {};
    } catch {}
    try {
      frame.contentWindow.postMessage({
        type: "vibedeck:deck-environment",
        environment,
      }, "*");
    } catch {}
  }

  async function handleDeckMessage(event) {
    if (!frame || event.source !== frame.contentWindow) return;
    const message = event.data;
    if (!message || typeof message !== "object") return;

    if (message.type === "vibedeck:deck-command" && message.action === "exit-viewer") {
      await exitViewer?.();
      return;
    }
    if (message.type === "vibedeck:deck-command" && message.action === "toggle-viewer") {
      await toggleViewer?.();
      return;
    }

    if (message.type !== "vibedeck:deck-request") return;
    const requestId = String(message.requestId || "").trim();
    if (!requestId || message.action !== "get-json") return;

    const path = String(message.path || "");
    const reply = payload => {
      try {
        event.source?.postMessage({
          type: "vibedeck:deck-response",
          requestId,
          ...payload,
        }, "*");
      } catch {}
    };

    if (!isDeckDataPath(path, window.location.origin)) {
      reply({ ok: false, error: "Deck bridge only permits GET /api/deck-data/*." });
      return;
    }

    try {
      const data = await fetchJsonOrThrow(path);
      reply({ ok: true, data });
    } catch (error) {
      reply({
        ok: false,
        error: error?.message || "Deck data request failed.",
        status: error?.status || 0,
      });
    }
  }

  window.addEventListener("message", handleDeckMessage);
  frame?.addEventListener("load", syncEnvironment);

  const findDeck = id => catalog.decks.find(deck => deck.id === id) || null;

  function setStatus(message = "", kind = "") {
    if (!status) return;
    status.textContent = message;
    status.dataset.kind = kind;
  }

  function renderIssues() {
    if (!issues) return;
    issues.replaceChildren();
    if (!catalog.issues.length) {
      issues.hidden = true;
      return;
    }

    const title = document.createElement("strong");
    title.textContent = "Decks needing attention";
    const list = document.createElement("ul");
    for (const issue of catalog.issues) {
      const item = document.createElement("li");
      const folder = issue.folder ? `${issue.folder}: ` : "";
      item.textContent = `${folder}${issue.message}`;
      list.appendChild(item);
    }
    issues.append(title, list);
    issues.hidden = false;
  }

  function renderTabs() {
    switcher?.querySelectorAll(".custom-deck-tab").forEach(button => button.remove());
    if (!switcher || !addButton) return;

    for (const deck of catalog.decks) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "mode-button custom-deck-tab";
      button.dataset.deckId = deck.id;
      button.textContent = `${deck.icon ? `${deck.icon} ` : ""}${deck.name}`;
      button.title = deck.name;
      button.addEventListener("click", () => navigate(deckMode(deck.id)));
      switcher.insertBefore(button, addButton);
    }
  }

  function updateHelp() {
    if (folderPath && catalog.rootPath) folderPath.textContent = catalog.rootPath;
    if (openFolderButton) openFolderButton.textContent = isLocalRequest?.() ? "Open Deck Folder" : "Copy Deck Folder";
    if (openExampleButton) openExampleButton.disabled = !findDeck("coding-pet");
    renderIssues();
  }

  function activate(mode = getActiveMode?.() || "") {
    const id = deckIdFromMode(mode);
    const activeDeck = id ? findDeck(id) : null;

    addButton?.classList.toggle("active", mode === "deck");
    switcher?.querySelectorAll(".custom-deck-tab").forEach(button => {
      button.classList.toggle("active", Boolean(id) && button.dataset.deckId === id);
    });

    if (!id) {
      if (help) help.hidden = false;
      if (frame) {
        frame.hidden = true;
        frame.removeAttribute("src");
        delete frame.dataset.deckId;
        delete frame.dataset.deckUrl;
      }
      return;
    }

    if (!activeDeck) {
      if (help) help.hidden = false;
      if (frame) {
        frame.hidden = true;
        frame.removeAttribute("src");
      }
      setStatus(`Deck “${id}” is unavailable. It may have been removed or its manifest / entry is invalid.`, "error");
      return;
    }

    if (help) help.hidden = true;
    if (frame) {
      frame.hidden = false;
      frame.title = activeDeck.name;
      frame.setAttribute("sandbox", deckSandbox(activeDeck.type, activeDeck.url, window.location.origin));
      if (frame.dataset.deckUrl !== activeDeck.url) {
        frame.src = activeDeck.url;
        frame.dataset.deckId = activeDeck.id;
        frame.dataset.deckUrl = activeDeck.url;
      }
      syncEnvironment();
    }
    setStatus("");
  }

  async function refresh({ silent = false, reloadActive = false } = {}) {
    if (refreshing) return catalog;
    refreshing = true;
    try {
      const previousMode = getActiveMode?.() || "";
      const previousDeckId = deckIdFromMode(previousMode);
      catalog = normalizeDeckCatalog(await fetchJsonOrThrow("/api/decks"));
      renderTabs();
      updateHelp();

      if (previousDeckId && !findDeck(previousDeckId)) {
        navigate("deck");
        setStatus(`Deck “${previousDeckId}” disappeared or is no longer valid.`, "error");
      } else {
        if (reloadActive && previousDeckId && frame && !frame.hidden) {
          const activeDeck = findDeck(previousDeckId);
          if (activeDeck) {
            frame.src = activeDeck.url;
            frame.dataset.deckUrl = activeDeck.url;
          }
        }
        activate(previousMode);
        if (!silent) {
          const count = catalog.decks.length;
          setStatus(`${count} Deck${count === 1 ? "" : "s"} discovered${catalog.issues.length ? ` · ${catalog.issues.length} issue${catalog.issues.length === 1 ? "" : "s"}` : ""}.`);
        }
      }
      return catalog;
    } catch (error) {
      if (!silent) setStatus(error.message || "Deck discovery failed.", "error");
      throw error;
    } finally {
      refreshing = false;
    }
  }

  async function openFolder() {
    if (!catalog.rootPath) await refresh({ silent: true });
    if (isLocalRequest?.()) {
      await fetchJsonOrThrow("/api/decks/open-folder", { method: "POST" });
      setStatus("Opened Deck folder on this PC.");
      return;
    }

    try {
      await navigator.clipboard.writeText(catalog.rootPath);
      setStatus("Deck folder path copied.");
    } catch {
      setStatus(`Deck folder: ${catalog.rootPath}`);
    }
  }

  addButton?.addEventListener("click", () => navigate("deck"));
  openFolderButton?.addEventListener("click", () => openFolder().catch(error => setStatus(error.message || "Could not open Deck folder.", "error")));
  openExampleButton?.addEventListener("click", () => {
    if (findDeck("coding-pet")) navigate(deckMode("coding-pet"));
  });
  refreshButton?.addEventListener("click", () => refresh({ reloadActive: true }).catch(() => {}));

  return {
    activate,
    refresh,
    syncEnvironment,
    getCatalog: () => catalog,
    startPolling(intervalMs = 5000) {
      if (pollTimer) return;
      pollTimer = setInterval(() => {
        if (document.visibilityState === "hidden" || !shouldPoll()) return;
        refresh({ silent: true }).catch(() => {});
      }, intervalMs);
    },
    stopPolling() {
      if (!pollTimer) return;
      clearInterval(pollTimer);
      pollTimer = null;
    },
    destroy() {
      window.removeEventListener("message", handleDeckMessage);
      frame?.removeEventListener("load", syncEnvironment);
      if (pollTimer) clearInterval(pollTimer);
      pollTimer = null;
    },
  };
}
