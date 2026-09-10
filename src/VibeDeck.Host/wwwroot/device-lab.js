const devices = {
  "boox-go-color-7": {
    name: "BOOX Go Color 7",
    portrait: [794, 1054],
    landscape: [1054, 794],
    dpr: 1.594,
    physical: "1264 × 1680",
    defaultOrientation: "landscape",
    sourceKey: "booxSource",
  },
  "asus-zenpad-p024": {
    name: "ASUS ZenPad 8.0 P024 (LineageOS)",
    portrait: [569, 911],
    landscape: [911, 569],
    dpr: 1.125,
    physical: "800 × 1280",
    defaultOrientation: "landscape",
    minimumTextSize: 11,
    minimumControlHeight: 28,
    sourceKey: "p024Source",
  },
  "galaxy-s23": {
    name: "Samsung Galaxy S23",
    portrait: [360, 780],
    landscape: [780, 360],
    dpr: 3,
    physical: "1080 × 2340",
    defaultOrientation: "portrait",
    sourceKey: "webSource",
  },
  "iphone-xs": {
    name: "iPhone XS",
    portrait: [375, 812],
    landscape: [812, 375],
    dpr: 3,
    physical: "1125 × 2436",
    defaultOrientation: "portrait",
    sourceKey: "appleSource",
  },
};

let labMessages = {};

const deviceSelect = document.getElementById("deviceSelect");
const orientationSelect = document.getElementById("orientationSelect");
const modeSelect = document.getElementById("modeSelect");
const deckControl = document.getElementById("deckControl");
const deckSelect = document.getElementById("deckSelect");
const trustStateSelect = document.getElementById("trustStateSelect");
const languageSelect = document.getElementById("languageSelect");
const scaleSelect = document.getElementById("scaleSelect");
const reloadPreview = document.getElementById("reloadPreview");
const labStage = document.getElementById("labStage");
const deviceScaler = document.getElementById("deviceScaler");
const deviceFrame = document.getElementById("deviceFrame");
const deviceScreen = document.getElementById("deviceScreen");
const previewFrame = document.getElementById("previewFrame");
const deviceName = document.getElementById("deviceName");
const deviceMetrics = document.getElementById("deviceMetrics");
const layoutStatus = document.getElementById("layoutStatus");
const layoutDetail = document.getElementById("layoutDetail");
const deviceSource = document.getElementById("deviceSource");

let loadTimer = 0;
let statusTimer = 0;
let deckCatalog = [];

function readDeckField(value, pascal, camel, fallback = "") {
  return value?.[pascal] ?? value?.[camel] ?? fallback;
}

async function loadDeckCatalog(preferredDeckId = "") {
  try {
    const response = await fetch("/api/decks", { cache: "no-store" });
    if (!response.ok) throw new Error(`Deck catalog failed to load (${response.status})`);
    const payload = await response.json();
    const rawDecks = payload.Decks ?? payload.decks ?? [];
    deckCatalog = Array.isArray(rawDecks) ? rawDecks.map(deck => ({
      id: String(readDeckField(deck, "Id", "id", "")).trim(),
      name: String(readDeckField(deck, "Name", "name", "")).trim(),
      icon: String(readDeckField(deck, "Icon", "icon", "")).trim(),
    })).filter(deck => deck.id && deck.name) : [];
  } catch {
    deckCatalog = [];
  }

  deckSelect.replaceChildren();
  for (const deck of deckCatalog) {
    const option = document.createElement("option");
    option.value = deck.id;
    option.textContent = `${deck.icon ? `${deck.icon} ` : ""}${deck.name}`;
    deckSelect.appendChild(option);
  }

  const requested = String(preferredDeckId || "").trim();
  const fallback = deckCatalog[0]?.id || "";
  deckSelect.value = deckCatalog.some(deck => deck.id === requested) ? requested : fallback;
  deckSelect.disabled = deckCatalog.length === 0;
}

function syncDeckControl() {
  deckControl.hidden = modeSelect.value !== "deck";
}

function text(key, values = {}) {
  return String(labMessages[key] || key)
    .replace(/\{(\w+)\}/g, (_, name) => values[name] ?? `{${name}}`);
}

async function loadLanguage() {
  const response = await fetch(`/locales/${encodeURIComponent(languageSelect.value)}.json`, { cache: "no-store" });
  if (!response.ok) throw new Error(`Locale failed to load (${response.status})`);
  const catalog = await response.json();
  labMessages = catalog.deviceLab || {};
  applyLanguage();
}

function applyLanguage() {
  document.documentElement.lang = languageSelect.value;
  document.querySelectorAll("[data-i18n]").forEach(element => {
    element.textContent = text(element.dataset.i18n);
  });
  updateDeviceLabels();
  setChecking();
}

function currentDevice() {
  return devices[deviceSelect.value] || devices["boox-go-color-7"];
}

function currentViewport() {
  return currentDevice()[orientationSelect.value] || currentDevice().portrait;
}

function updateDeviceLabels() {
  const device = currentDevice();
  const [width, height] = currentViewport();
  deviceName.textContent = device.name;
  deviceMetrics.textContent = `${width} × ${height} CSS px · DPR ${device.dpr} · ${device.physical} px`;
  deviceSource.textContent = text(device.sourceKey);
}

function previewUrl() {
  const parameters = new URLSearchParams({
    mode: modeSelect.value,
    lang: languageSelect.value,
    devicePreview: deviceSelect.value,
    previewTrust: trustStateSelect.value,
    eink: deviceSelect.value === "boox-go-color-7" ? "1" : "0",
    source: "device-lab",
    preview: String(Date.now()),
  });
  if (deviceSelect.value === "boox-go-color-7" && ["sideboard", "quota"].includes(modeSelect.value)) {
    parameters.set("viewer", "1");
  }
  if (modeSelect.value === "deck") {
    parameters.set("viewer", "1");
    if (deckSelect.value) parameters.set("deck", deckSelect.value);
  }
  if (trustStateSelect.value === "local" && modeSelect.value === "setup") {
    parameters.delete("mode");
  }
  return `/index.html?${parameters.toString()}`;
}

function setChecking() {
  layoutStatus.className = "status-badge checking";
  layoutStatus.textContent = text("checking");
  layoutDetail.textContent = text("loading");
}

function applyScale() {
  const frameWidth = deviceFrame.offsetWidth;
  const frameHeight = deviceFrame.offsetHeight;
  if (!frameWidth || !frameHeight) return;

  const requested = scaleSelect.value === "auto" ? null : Number(scaleSelect.value);
  const availableWidth = Math.max(100, labStage.clientWidth - 52);
  const availableHeight = Math.max(100, labStage.clientHeight - 52);
  const scale = requested || Math.min(1, availableWidth / frameWidth, availableHeight / frameHeight);
  deviceFrame.style.transform = `scale(${scale})`;
  deviceScaler.style.width = `${Math.ceil(frameWidth * scale)}px`;
  deviceScaler.style.height = `${Math.ceil(frameHeight * scale)}px`;
}

function configureFrame({ reload = true } = {}) {
  const device = currentDevice();
  const [width, height] = currentViewport();
  if (deviceSelect.value === "boox-go-color-7" && modeSelect.value === "display") {
    modeSelect.value = "sideboard";
  }
  modeSelect.querySelector('option[value="display"]').disabled = deviceSelect.value === "boox-go-color-7";
  syncDeckControl();
  deviceFrame.dataset.device = deviceSelect.value;
  deviceFrame.dataset.orientation = orientationSelect.value;
  deviceScreen.style.width = `${width}px`;
  deviceScreen.style.height = `${height}px`;
  previewFrame.style.width = `${width}px`;
  previewFrame.style.height = `${height}px`;
  previewFrame.width = width;
  previewFrame.height = height;
  updateDeviceLabels();
  setChecking();
  requestAnimationFrame(applyScale);
  if (reload) previewFrame.src = previewUrl();
}

function inspectLayout() {
  try {
    const previewWindow = previewFrame.contentWindow;
    const previewDocument = previewFrame.contentDocument;
    if (!previewWindow || !previewDocument?.documentElement) return;
    const [expectedWidth, expectedHeight] = currentViewport();
    const actualWidth = Math.round(previewWindow.innerWidth);
    const actualHeight = Math.round(previewWindow.innerHeight);
    const root = previewDocument.documentElement;
    const body = previewDocument.body;
    const contentWidth = Math.max(root.scrollWidth, body?.scrollWidth || 0);
    const exact = actualWidth === expectedWidth && actualHeight === expectedHeight;
    const overflow = contentWidth > actualWidth + 1;
    const isBoox = deviceSelect.value === "boox-go-color-7";
    const minimumTextSize = currentDevice().minimumTextSize ?? (isBoox ? 16 : 12);
    const minimumControlHeight = currentDevice().minimumControlHeight ?? (isBoox ? 44 : 36);
    const isVisible = element => {
      const style = previewWindow.getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) !== 0 && rect.width > 0 && rect.height > 0;
    };
    const controls = Array.from(previewDocument.querySelectorAll("button, select, input:not([type='hidden']), summary"))
      .filter(isVisible);
    const undersizedControls = controls.filter(element => element.getBoundingClientRect().height + 0.5 < minimumControlHeight);
    const textElements = Array.from(previewDocument.querySelectorAll("small, span, p, li, label, strong, b, time, code"))
      .filter(element => isVisible(element) && element.textContent.trim());
    const undersizedText = textElements.filter(element => parseFloat(previewWindow.getComputedStyle(element).fontSize) + 0.05 < minimumTextSize);
    const qualityIssue = undersizedControls.length > 0 || undersizedText.length > 0;
    const quotaShell = modeSelect.value === "quota" ? previewDocument.querySelector(".quota-shell") : null;
    const quotaCards = quotaShell ? Array.from(quotaShell.querySelectorAll(".quota-account-card")) : [];
    const shellStyle = quotaShell ? previewWindow.getComputedStyle(quotaShell) : null;
    const shellClipsContent = Boolean(quotaShell && shellStyle &&
      ["hidden", "clip"].includes(shellStyle.overflowY) &&
      quotaShell.scrollHeight > quotaShell.clientHeight + 1);
    const cardWidthFloor = Math.min(280, Math.max(0, actualWidth - 32));
    const crampedQuotaCard = quotaCards.some(card => {
      const rect = card.getBoundingClientRect();
      return rect.width + 0.5 < cardWidthFloor || card.scrollWidth > card.clientWidth + 1;
    });
    const quotaLayoutIssue = shellClipsContent || crampedQuotaCard;
    const values = {
      actual: `${actualWidth} × ${actualHeight}`,
      expected: `${expectedWidth} × ${expectedHeight}`,
      content: contentWidth,
      controls: undersizedControls.length,
      text: undersizedText.length,
      controlHeight: minimumControlHeight,
      textSize: minimumTextSize,
    };

    const passed = exact && !overflow && !qualityIssue && !quotaLayoutIssue;
    layoutStatus.className = `status-badge ${passed ? "pass" : "fail"}`;
    layoutStatus.textContent = text(passed ? "pass" : qualityIssue && !overflow && !quotaLayoutIssue ? "qualityFail" : "fail");
    layoutDetail.textContent = quotaLayoutIssue
      ? text("quotaClipping", values)
      : overflow
      ? text("overflow", values)
      : qualityIssue
        ? text("quality", values)
      : exact
        ? text("exact", values)
        : text("mismatch", values);
    const controlSamples = undersizedControls.slice(0, 6).map(element => {
      const rect = element.getBoundingClientRect();
      return `${element.tagName.toLowerCase()}#${element.id || "-"} ${Math.round(rect.height)}px ${element.textContent.trim().slice(0, 30)}`;
    });
    const textSamples = undersizedText.slice(0, 6).map(element => {
      const size = previewWindow.getComputedStyle(element).fontSize;
      return `${element.tagName.toLowerCase()}#${element.id || "-"} ${size} ${element.textContent.trim().slice(0, 30)}`;
    });
    const quotaSamples = [];
    if (shellClipsContent) quotaSamples.push("quota shell clips vertically");
    if (crampedQuotaCard) quotaSamples.push("quota card is too narrow or overflows");
    layoutDetail.title = [...quotaSamples, ...controlSamples, ...textSamples].join("\n");
  } catch {
    setChecking();
  }
}

deviceSelect.addEventListener("change", () => {
  orientationSelect.value = currentDevice().defaultOrientation;
  configureFrame();
});
orientationSelect.addEventListener("change", () => configureFrame());
modeSelect.addEventListener("change", () => configureFrame());
deckSelect.addEventListener("change", () => configureFrame());
trustStateSelect.addEventListener("change", () => configureFrame());
languageSelect.addEventListener("change", async () => {
  await loadLanguage();
  configureFrame();
});
scaleSelect.addEventListener("change", applyScale);
reloadPreview.addEventListener("click", () => configureFrame());
previewFrame.addEventListener("load", () => {
  clearTimeout(loadTimer);
  loadTimer = window.setTimeout(inspectLayout, 900);
});
window.addEventListener("resize", applyScale);

async function start() {
  const parameters = new URLSearchParams(location.search);
  languageSelect.value = parameters.get("lang") || "zh-Hant";
  if (!["zh-Hant", "en", "ja"].includes(languageSelect.value)) languageSelect.value = "zh-Hant";
  deviceSelect.value = parameters.get("device") || "boox-go-color-7";
  if (!devices[deviceSelect.value]) deviceSelect.value = "boox-go-color-7";
  trustStateSelect.value = parameters.get("trust") || "paired";
  if (!["paired", "unpaired", "pending", "local"].includes(trustStateSelect.value)) trustStateSelect.value = "paired";
  orientationSelect.value = currentDevice().defaultOrientation;
  const requestedMode = parameters.get("mode") || "";
  if (["sideboard", "quota", "display", "deck", "setup"].includes(requestedMode)) {
    modeSelect.value = requestedMode;
  }
  await loadDeckCatalog(parameters.get("deck") || "");
  await loadLanguage();
  configureFrame();
  statusTimer = window.setInterval(inspectLayout, 1500);
}

start().catch(error => {
  layoutStatus.className = "status-badge fail";
  layoutStatus.textContent = "Locale error";
  layoutDetail.textContent = error.message;
});
window.addEventListener("beforeunload", () => window.clearInterval(statusTimer));
