import {
  averagePercent,
  describeWeatherCode,
  formatFileSize,
  formatGb,
  formatMbps,
  formatPercent,
  formatSeconds,
  formatTemperature,
  formatWeatherLocation,
} from "./modules/formatters.js?v=48";
import { createDisplayInputController } from "./modules/display-input.js?v=50";
import { createCustomCardsController } from "./modules/custom-cards.js?v=55";
import { createActivityFeedController } from "./modules/activity-feed.js?v=55";
import { createDashboardLayoutController } from "./modules/dashboard-layout.js?v=64";
import { createQuotaController } from "./modules/quota-controller.js?v=52";
import {
  createQuotaAccountNavigator,
} from "./modules/quota-account-navigation.js?v=1";
import {
  buildQuotaViewState,
  groupAgyAccounts,
  groupSingleProviderAccounts,
  quotaDataFingerprint,
} from "./modules/quota-model.js?v=1";
import {
  buildQuotaHelpSpec,
  buildQuotaSummary,
  formatQuotaStateLabel,
} from "./modules/quota-presentation.js?v=2";
import { createQuotaSetupCardRenderer } from "./modules/quota-setup-cards.js?v=2";
import { createCodexAccountManager } from "./modules/quota-codex-account-manager.js?v=1";
import { createQuotaActionController } from "./modules/quota-action-controller.js?v=3";
import { createQuotaCardRenderer } from "./modules/quota-card-renderer.js?v=3";
import { createDiagnosticsController } from "./modules/diagnostics-controller.js?v=1";
import { createDeviceManagementView } from "./modules/device-management-view.js?v=1";
import { createDeviceActionsController } from "./modules/device-actions-controller.js?v=1";
import { createPairingSessionController } from "./modules/pairing-session-controller.js?v=1";
import { createProductUpdateController } from "./modules/product-update-controller.js?v=1";
import { createDisplayInstallController } from "./modules/display-install-controller.js?v=1";
import { createTurnSettingsController } from "./modules/turn-settings-controller.js?v=1";
import { createKeepAwakeController } from "./modules/keep-awake-controller.js?v=1";
import { createDisplaySourceController } from "./modules/display-source-controller.js?v=1";
import {
  chooseAutoDisplayMode,
  getAutoModeValue,
  readClientDisplayMetrics,
} from "./modules/display-auto-mode.js?v=1";
import { createHostAuthController } from "./modules/host-auth-controller.js?v=1";
import { createCustomDeckController } from "./modules/custom-deck-controller.js?v=1";
import { createQuotaMiniCardController } from "./modules/quota-mini-card.js?v=59";
import { createSideboardController } from "./modules/sideboard.js?v=51";
import { createMobileOverviewController } from "./modules/mobile-overview.js?v=3";
import { createResponsiveSpaceController } from "./modules/responsive-space.js?v=1";
import { isFullscreenDisplayStreaming as isFullscreenDisplayStreamingPolicy } from "./modules/dashboard-background-policy.js?v=1";
import { createStreamController } from "./modules/stream-controller.js?v=52";
import { tuneVideoReceiver } from "./modules/stream-tuning.js?v=47";
import { applyFeedbackState } from "./modules/feedback-state.js?v=1";
import { confirmAction } from "./modules/ui-confirm.js?v=1";
import {
  escapeHtml,
  formatQuotaWindowLabel,
  summarizeQuotaWindow,
} from "./modules/quota-formatters.js?v=51";
import { getIntlLocale, initLocale, onLocaleChange, t, tApi, tLegacy, translateText } from "./modules/i18n.js?v=4";
import {
  DEVICE_TOKEN_KEY,
  DEVICE_ID_KEY,
  DEVICE_COOKIE,
  DEVICE_TOKEN_HISTORY_KEY,
  DEVICE_TOKEN_HISTORY_LIMIT,
  readCookie,
  writeCookie,
  writeDeviceCookies,
  loadStoredDeviceCredentials,
  loadDeviceTokenHistory,
  saveDeviceTokenHistory,
} from "./modules/device-credentials.js?v=1";
import {
  CLIENT_INSTANCE_KEY,
  CLIENT_INSTANCE_COOKIE,
  getOrCreateClientInstanceId,
} from "./modules/client-instance.js?v=1";
import {
  isLoopbackHost,
  isIosUA,
  isIphoneUA,
  isMobileUA,
  shouldPreferWebRtcDisplay,
} from "./modules/env-detect.js?v=2";
import {
  EINK_PREF_KEY,
  readEinkQuery,
  readEinkCookie,
  looksLikeBooxScreen,
  detectEinkHardware,
} from "./modules/eink-detect.js?v=1";

    const screen = document.getElementById("screen");
    const statusText = document.getElementById("status");
    const dot = document.getElementById("dot");
    const rotation = document.getElementById("rotation");
    const orientation = document.getElementById("orientation");
    let einkPhysicalOrientation = "unknown";
    let einkOrientationCandidate = "";
    let einkOrientationCandidateSince = 0;
    const displayMode = document.getElementById("displayMode");
    const setupMode = document.getElementById("setupMode");
    const sideboardMode = document.getElementById("sideboardMode");
    const quotaMode = document.getElementById("quotaMode");
    const addDeckMode = document.getElementById("addDeckMode");
    const refresh = document.getElementById("refresh");
    const productUpdate = document.getElementById("productUpdate");
    const productUpdateStatus = document.getElementById("productUpdateStatus");
    const fullscreen = document.getElementById("fullscreen");
    const displaySettingsToggle = document.getElementById("displaySettingsToggle");
    const displayToolbar = document.getElementById("displayToolbar");
    const displayToolbarToggle = document.getElementById("displayToolbarToggle");
    const displaySource = document.getElementById("displaySource");
    const displaySourceKind = document.getElementById("displaySourceKind");
    const remoteKeyboardButton = document.getElementById("remoteKeyboardButton");
    const remoteKeyboardInput = document.getElementById("remoteKeyboardInput");
    const displayEmptyState = document.getElementById("displayEmptyState");
    const displayStreamStateMessage = document.getElementById("displayStreamStateMessage");
    const displayStreamRetry = document.getElementById("displayStreamRetry");
    // Declared early: display-source setup may run before the
    // display-input controller is constructed. Access before this line = TDZ crash.
    let displayInputController = null;
    const displayEmptyTitle = document.getElementById("displayEmptyTitle");
    const displayEmptyMessage = document.getElementById("displayEmptyMessage");
    const installVirtualDisplay = document.getElementById("installVirtualDisplay");
    const setupInstallVirtualDisplay = document.getElementById("setupInstallVirtualDisplay");
    const setupDisplayInstallDetail = document.getElementById("setupDisplayInstallDetail");
    const displayInstallDetail = document.getElementById("displayInstallDetail");
    const openSideboardFromEmpty = document.getElementById("openSideboardFromEmpty");
    const exitViewer = document.getElementById("exitViewer");
    const streamPreset = document.getElementById("streamPreset");
    const streamFps = document.getElementById("streamFps");
    const streamQuality = document.getElementById("streamQuality");
    const streamTransport = document.getElementById("streamTransport");
    const applyStream = document.getElementById("applyStream");
    const turnSettingsPanel = document.getElementById("turnSettingsPanel");
    const turnKeyId = document.getElementById("turnKeyId");
    const turnApiToken = document.getElementById("turnApiToken");
    const saveTurnSettings = document.getElementById("saveTurnSettings");
    const testTurnSettings = document.getElementById("testTurnSettings");
    const clearTurnSettings = document.getElementById("clearTurnSettings");
    const turnSettingsStatus = document.getElementById("turnSettingsStatus");
    const turnDiagnosticsStatus = document.getElementById("turnDiagnosticsStatus");
    const modePreset = document.getElementById("modePreset");
    const modeWidth = document.getElementById("modeWidth");
    const modeHeight = document.getElementById("modeHeight");
    const modeRefresh = document.getElementById("modeRefresh");
    const applyMode = document.getElementById("applyMode");
    const driverState = document.getElementById("driverState");
    const qrCode = document.getElementById("qrCode");
    const qrCaption = document.getElementById("qrCaption");
    const prettyLink = document.getElementById("prettyLink");
    const httpsLink = document.getElementById("httpsLink");
    const httpsCertLink = document.getElementById("httpsCertLink");
    const androidCertLink = document.getElementById("androidCertLink");
    const androidCertHelp = document.getElementById("androidCertHelp");
    const certificateSetupTitle = document.getElementById("certificateSetupTitle");
    const publicEndpointPanel = document.getElementById("publicEndpointPanel");
    const publicEndpointStatus = document.getElementById("publicEndpointStatus");
    const publicEndpointInstallationId = document.getElementById("publicEndpointInstallationId");
    const publicEndpointUrl = document.getElementById("publicEndpointUrl");
    const publicEndpointControls = document.getElementById("publicEndpointControls");
    const publicEndpointHint = document.getElementById("publicEndpointHint");
    const savePublicEndpoint = document.getElementById("savePublicEndpoint");
    const clearPublicEndpoint = document.getElementById("clearPublicEndpoint");
    const deviceConnectionCodePanel = document.getElementById("deviceConnectionCodePanel");
    const deviceConnectionCodeUrl = document.getElementById("deviceConnectionCodeUrl");
    const deviceConnectionCode = document.getElementById("deviceConnectionCode");
    const deviceConnectionCodeStatus = document.getElementById("deviceConnectionCodeStatus");
    const generateDeviceConnectionCode = document.getElementById("generateDeviceConnectionCode");
    const httpLink = document.getElementById("httpLink");
    const phonePairRequest = document.getElementById("phonePairRequest");
    const phonePairIntro = document.getElementById("phonePairIntro");
    const launchDeckWindow = document.getElementById("launchDeckWindow");
    const pairingStepInstall = document.getElementById("pairingStepInstall");
    const pairingStepPair = document.getElementById("pairingStepPair");
    const pairingStepOpen = document.getElementById("pairingStepOpen");
    const pairingStepInstallTitle = document.getElementById("pairingStepInstallTitle");
    const pairingStepInstallHint = document.getElementById("pairingStepInstallHint");
    const pairingStepPairTitle = document.getElementById("pairingStepPairTitle");
    const pairingStepPairHint = document.getElementById("pairingStepPairHint");
    const trustState = document.getElementById("trustState");
    const streamCapabilityState = document.getElementById("streamCapabilityState");
    const trustedDevicesPanel = document.getElementById("trustedDevicesPanel");
    const newDeviceConnectPanel = document.getElementById("newDeviceConnectPanel");
    const openNewDevicePanel = document.getElementById("openNewDevicePanel");
    const pendingPairingPanel = document.getElementById("pendingPairingPanel");
    const pendingPairingList = document.getElementById("pendingPairingList");
    const trustedDeviceList = document.getElementById("trustedDeviceList");
    const clearTrustedDevices = document.getElementById("clearTrustedDevices");
    const refreshTrustedDevices = document.getElementById("refreshTrustedDevices");
    const diagnosticsPanel = document.getElementById("diagnosticsPanel");
    const diagnosticsToggle = document.getElementById("diagnosticsToggle");
    const diagnosticsPanelContent = document.getElementById("diagnosticsPanelContent");
    const diagnosticsSummary = document.getElementById("diagnosticsSummary");
    const diagnosticsList = document.getElementById("diagnosticsList");
    const refreshDiagnostics = document.getElementById("refreshDiagnostics");
    const markDiagnostics = document.getElementById("markDiagnostics");
    const copyDiagnostics = document.getElementById("copyDiagnostics");
    const wakeState = document.getElementById("wakeState");
    const appState = document.getElementById("appState");
    const deviceState = document.getElementById("deviceState");
    const displayView = document.getElementById("displayView");
    const sideboardView = document.getElementById("sideboardView");
    const quotaView = document.getElementById("quotaView");
    const customDeckView = document.getElementById("customDeckView");
    const customDeckFrame = document.getElementById("customDeckFrame");
    const customDeckHelp = document.getElementById("customDeckHelp");
    const customDeckFolderPath = document.getElementById("customDeckFolderPath");
    const customDeckStatus = document.getElementById("customDeckStatus");
    const customDeckIssues = document.getElementById("customDeckIssues");
    const customDeckOpenFolder = document.getElementById("customDeckOpenFolder");
    const customDeckOpenExample = document.getElementById("customDeckOpenExample");
    const customDeckRefresh = document.getElementById("customDeckRefresh");
    let customDeckController = null;
    const sideboardShell = document.getElementById("sideboardShell");
    const systemSideboardPage = document.getElementById("systemSideboardPage");
    const customSideboardPage = document.getElementById("customSideboardPage");
    const sideboardPageTabs = document.getElementById("sideboardPageTabs");
    const customCardsGrid = document.getElementById("customCardsGrid");
    const customCardsStatus = document.getElementById("customCardsStatus");
    const customRefreshCards = document.getElementById("customRefreshCards");
    const customSettingsButton = document.getElementById("customSettingsButton");
    const customCardSettingsPanel = document.getElementById("customCardSettingsPanel");
    const customSettingsClose = document.getElementById("customSettingsClose");
    const customCardSettingsForm = document.getElementById("customCardSettingsForm");
    const customSettingsCard = document.getElementById("customSettingsCard");
    const customSettingsMaxItems = document.getElementById("customSettingsMaxItems");
    const customSettingsStreamEnabled = document.getElementById("customSettingsStreamEnabled");
    const customSettingsStreamDelay = document.getElementById("customSettingsStreamDelay");
    const customSettingsHint = document.getElementById("customSettingsHint");
    const customSettingsSave = document.getElementById("customSettingsSave");
    const customSettingsClear = document.getElementById("customSettingsClear");
    const customManageButton = document.getElementById("customManageButton");
    const customSourcesManager = document.getElementById("customSourcesManager");
    const customSourceList = document.getElementById("customSourceList");
    const customSourceForm = document.getElementById("customSourceForm");
    const customSourceFormTitle = document.getElementById("customSourceFormTitle");
    const customSourceKey = document.getElementById("customSourceKey");
    const customSourceDisplayName = document.getElementById("customSourceDisplayName");
    const customCardType = document.getElementById("customCardType");
    const customCardTitle = document.getElementById("customCardTitle");
    const customCardPosition = document.getElementById("customCardPosition");
    const customStaleAfter = document.getElementById("customStaleAfter");
    const customDefaultTtl = document.getElementById("customDefaultTtl");
    const customMaxItems = document.getElementById("customMaxItems");
    const customSourceFormSubmit = document.getElementById("customSourceFormSubmit");
    const customSourceCancel = document.getElementById("customSourceCancel");
    const customCredentialPanel = document.getElementById("customCredentialPanel");
    const customCredentialText = document.getElementById("customCredentialText");
    const customCredentialCopy = document.getElementById("customCredentialCopy");
    const customCredentialClose = document.getElementById("customCredentialClose");
    const customAddSource = document.getElementById("customAddSource");
    const customManagerAdd = document.getElementById("customManagerAdd");
    const customManagerClose = document.getElementById("customManagerClose");
    const windowsNotificationControl = document.getElementById("windowsNotificationControl");
    const windowsNotificationStatus = document.getElementById("windowsNotificationStatus");
    const windowsNotificationMessage = document.getElementById("windowsNotificationMessage");
    const windowsNotificationEnable = document.getElementById("windowsNotificationEnable");
    const windowsNotificationDisable = document.getElementById("windowsNotificationDisable");
    const rtcScreen = document.getElementById("rtcScreen");
    const sideHeadline = document.getElementById("sideHeadline");
    const sideSummary = document.getElementById("sideSummary");
    const sideError = document.getElementById("sideError");
    const sideLoad = document.getElementById("sideLoad");
    const sideLoadNormal = document.getElementById("sideLoadNormal");
    const sideLoadStatus = document.getElementById("sideLoadStatus");
    const sideLoadStatusReason = document.getElementById("sideLoadStatusReason");
    const sideLoadAlert = document.getElementById("sideLoadAlert");
    const sideLoadAlertTitle = document.getElementById("sideLoadAlertTitle");
    const sideLoadAlertReason = document.getElementById("sideLoadAlertReason");
    const sideHost = document.getElementById("sideHost");
    const sideUptime = document.getElementById("sideUptime");
    const sideHealth = document.getElementById("sideHealth");
    const sideCpu = document.getElementById("sideCpu");
    const sideCpuSub = document.getElementById("sideCpuSub");
    const sideCpuBar = document.getElementById("sideCpuBar");
    const sideRam = document.getElementById("sideRam");
    const sideRamSub = document.getElementById("sideRamSub");
    const sideRamBar = document.getElementById("sideRamBar");
    const sideGpu = document.getElementById("sideGpu");
    const sideGpuSub = document.getElementById("sideGpuSub");
    const sideGpuBar = document.getElementById("sideGpuBar");
    const sideVram = document.getElementById("sideVram");
    const sideVramSub = document.getElementById("sideVramSub");
    const sideVramBar = document.getElementById("sideVramBar");
    const sideNet = document.getElementById("sideNet");
    const sideNetSub = document.getElementById("sideNetSub");
    const sideNetBar = document.getElementById("sideNetBar");
    const sideDisk = document.getElementById("sideDisk");
    const sideDiskSub = document.getElementById("sideDiskSub");
    const sideDiskBar = document.getElementById("sideDiskBar");
    const sideDiskIo = document.getElementById("sideDiskIo");
    const sideWeather = document.getElementById("sideWeather");
    const sideWeatherSub = document.getElementById("sideWeatherSub");
    const sideProcessList = document.getElementById("sideProcessList");
    const activityFeedCard = document.getElementById("activityFeedCard");
    const activityFeedList = document.getElementById("activityFeedList");
    const activityFeedFilters = [...document.querySelectorAll("[data-activity-filter]")];
    const activityFeedFilterSelect = document.getElementById("activityFeedFilterSelect");
    const quotaMiniSource = document.getElementById("quotaMiniSource");
    const quotaMiniValue = document.getElementById("quotaMiniValue");
    const quotaMiniBar = document.getElementById("quotaMiniBar");
    const quotaMiniReset = document.getElementById("quotaMiniReset");
    const quotaMiniState = document.getElementById("quotaMiniState");
    const quotaMiniCredits = document.getElementById("quotaMiniCredits");
    const quotaSummary = document.getElementById("quotaSummary");
    const quotaUpdated = document.getElementById("quotaUpdated");
    const quotaTabs = document.getElementById("quotaTabs");
    const quotaHelp = document.getElementById("quotaHelp");
    const quotaGrid = document.getElementById("quotaGrid");
    const hostAuthGate = document.getElementById("hostAuthGate");
    const hostAuthForm = document.getElementById("hostAuthForm");
    const hostAuthPassword = document.getElementById("hostAuthPassword");
    const hostAuthSubmit = document.getElementById("hostAuthSubmit");
    const hostAuthError = document.getElementById("hostAuthError");
    const responsiveSpaceController = createResponsiveSpaceController();
    const wsBase = `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}`;
    let lastUrl = null;
    let inputSocket = null;
    let streamController = null;
    let streamStats = null;
    let activeMode = "display";
    let dashboardConnectionState = "connecting";
    let sideboardTimer = null;
    let quotaTimer = null;
    let customCardsTimer = null;
    let activityNotificationsTimer = null;
    let dashboardConnectionTimer = null;
    let deviceStatusTimer = null;
    let deviceStatusInterval = 0;
    let dashboardEvents = null;
    const dashboardRefreshState = {
      sideboard: { last: 0, timer: null, dirty: false },
      quota: { last: 0, timer: null, dirty: false },
      customCards: { last: 0, timer: null, dirty: false }
    };
    let customCardsController = null;
    let quotaMiniController = null;
    let actionToken = "";
    let actionHeaderName = "X-VibeDeck-Action-Token";
    let hostVersionLabel = "";
    const DISPLAY_MODE_STORAGE_KEY = "vibeDeckModePreset.v2";
    const AUTO_DISPLAY_MODE = getAutoModeValue();
    let displayModePresets = [];
    let autoDisplayModeTimer = null;
    let lastAutoDisplayModeSignature = "";

    function persistDeviceCredentials(token, id) {
      const nextToken = (token || "").trim();
      const nextId = (id || "").trim();
      const previousToken = typeof deviceToken === "string" ? deviceToken.trim() : "";
      if (nextToken && previousToken && nextToken !== previousToken) {
        saveDeviceTokenHistory([previousToken, ...loadDeviceTokenHistory()].filter(value => value !== nextToken));
      } else if (!nextToken) {
        saveDeviceTokenHistory([]);
      }
      deviceToken = nextToken;
      deviceId = nextId;
      try {
        if (nextToken) {
          localStorage.setItem(DEVICE_TOKEN_KEY, nextToken);
          sessionStorage.setItem(DEVICE_TOKEN_KEY, nextToken);
          writeDeviceCookies(nextToken, 400);
        } else {
          localStorage.removeItem(DEVICE_TOKEN_KEY);
          sessionStorage.removeItem(DEVICE_TOKEN_KEY);
          writeDeviceCookies("", 0);
        }
        if (nextId) {
          localStorage.setItem(DEVICE_ID_KEY, nextId);
          sessionStorage.setItem(DEVICE_ID_KEY, nextId);
        } else {
          localStorage.removeItem(DEVICE_ID_KEY);
          sessionStorage.removeItem(DEVICE_ID_KEY);
        }
      } catch {
        // Private mode / storage blocked: cookie may still work.
        if (nextToken) writeDeviceCookies(nextToken, 400);
      }
    }

    const storedDevice = loadStoredDeviceCredentials();
    let deviceToken = storedDevice.token;
    let deviceId = storedDevice.id;
    let deviceHeaderName = "X-VibeDeck-Device-Token";
    let deviceTrusted = false;
    let deviceLocalRequest = false;
    let pairingQrActive = false;
    let usesTrustedPublicUrl = false;
    // Pairing main path stays QR + steps + approval. Advanced connect details only
    // reappear when the endpoint itself needs repair (not during a healthy wait).
    let pairingNeedsAdvancedConnect = false;
    let lastConnectInfoSnapshot = null;
    let quotaSnapshotData = null;
    // Keep the initial quota view consistent across phone and E Ink clients.
    // Users can still switch tabs; the versioned key prevents an old device
    // preference from making one client open AGY while another opens Codex.
    const QUOTA_TAB_STORAGE_KEY = "vibeDeckQuotaTab.v2";
    let quotaActiveTab = localStorage.getItem(QUOTA_TAB_STORAGE_KEY) || "codex";
    const quotaAccountNavigator = createQuotaAccountNavigator();
    let quotaSwipeStartX = null;
    let installPromptEvent = null;
    let pendingApprovalTimer = null;
    let connectInfoTimer = null;
    let shouldDefaultToFirstDeviceSetup = false;
    const touchLongPressMs = 460;
    const touchDragThresholdPx = 12;
    const DEVICE_PREVIEW_KINDS = new Set(["boox-go-color-7", "asus-zenpad-p024", "galaxy-s23", "iphone-xs", "linux-desktop"]);
    const MOBILE_DEVICE_PREVIEW_KINDS = new Set(["boox-go-color-7", "asus-zenpad-p024", "galaxy-s23", "iphone-xs"]);
    const DEVICE_PREVIEW_TRUST_STATES = new Set(["paired", "unpaired", "pending", "local"]);
    const devicePreviewKind = (() => {
      if (!isLoopbackHost()) return "";
      const value = new URLSearchParams(location.search).get("devicePreview") || "";
      return DEVICE_PREVIEW_KINDS.has(value) ? value : "";
    })();
    let devicePreviewTrustState = (() => {
      if (!devicePreviewKind) return "";
      const value = new URLSearchParams(location.search).get("previewTrust") || "paired";
      return DEVICE_PREVIEW_TRUST_STATES.has(value) ? value : "paired";
    })();

    function isDevicePreview(kind = "") {
      return Boolean(devicePreviewKind) && (!kind || devicePreviewKind === kind);
    }

    function isDevicePreviewTrust(state) {
      return isDevicePreview() && devicePreviewTrustState === state;
    }

    function applyDevicePreviewEnvironment() {
      const root = document.documentElement;
      if (!isDevicePreview()) {
        delete root.dataset.devicePreview;
        root.style.removeProperty("--safe-area-inset-top");
        root.style.removeProperty("--safe-area-inset-right");
        root.style.removeProperty("--safe-area-inset-bottom");
        root.style.removeProperty("--safe-area-inset-left");
        return;
      }

      root.dataset.devicePreview = devicePreviewKind;
      const landscape = window.innerWidth > window.innerHeight;
      const iphone = isDevicePreview("iphone-xs");
      root.style.setProperty("--safe-area-inset-top", iphone && !landscape ? "44px" : "0px");
      root.style.setProperty("--safe-area-inset-right", iphone && landscape ? "44px" : "0px");
      root.style.setProperty("--safe-area-inset-bottom", iphone ? (landscape ? "21px" : "34px") : "0px");
      root.style.setProperty("--safe-area-inset-left", iphone && landscape ? "44px" : "0px");
    }

    function isMobileClient() {
      if (isDevicePreview()) return MOBILE_DEVICE_PREVIEW_KINDS.has(devicePreviewKind);
      return isMobileUA();
    }

    function writeEinkPreference(value) {
      if (isDevicePreview()) return;
      const flag = value ? "1" : "0";
      try {
        localStorage.setItem(EINK_PREF_KEY, flag);
      } catch {
        // ignore
      }
      try {
        // Cookie backup: some BOOX "Add to Home Screen" WebAPK paths keep cookies
        // more reliably than a fresh localStorage partition on first launch.
        document.cookie = `${EINK_PREF_KEY}=${flag}; Path=/; Max-Age=31536000; SameSite=Lax`;
      } catch {
        // ignore
      }
    }

    function isEinkClient() {
      if (isDevicePreview()) return isDevicePreview("boox-go-color-7");
      const query = readEinkQuery();
      if (query !== null) return query;

      try {
        const stored = localStorage.getItem(EINK_PREF_KEY);
        if (stored === "1") return true;
        if (stored === "0") return false;
      } catch {
        // ignore
      }

      const cookie = readEinkCookie();
      if (cookie === "1") return true;
      if (cookie === "0") return false;

      return detectEinkHardware();
    }

    function ensureEinkPreferenceSticky() {
      if (isDevicePreview()) return;
      // Once we know this device wants e-ink (hardware or explicit query), persist
      // so PWA home-screen launches (which drop ?eink=1) still open paper mode.
      const query = readEinkQuery();
      if (query === false) {
        writeEinkPreference(false);
        return;
      }
      if (query === true || detectEinkHardware()) {
        try {
          if (localStorage.getItem(EINK_PREF_KEY) !== "0" && readEinkCookie() !== "0") {
            writeEinkPreference(true);
          }
        } catch {
          writeEinkPreference(true);
        }
      }
    }

    function setEinkClient(enabled, { persist = true, syncUrl = true } = {}) {
      if (persist) {
        writeEinkPreference(enabled);
      }

      if (syncUrl) {
        try {
          const url = new URL(location.href);
          if (enabled) url.searchParams.set("eink", "1");
          else url.searchParams.delete("eink");
          history.replaceState({}, "", url.pathname + url.search + url.hash);
        } catch {
          // ignore
        }
      }

      applyClientChrome();
      if (enabled) {
        // E-ink product default is sideboard, not display stream.
        try {
          if (typeof setMode === "function") setMode("sideboard");
        } catch {
          // view switch may not be ready during early boot
        }
      }
      updateEinkToggle();
    }

    function updateEinkToggle() {
      const button = document.getElementById("einkModeToggle");
      if (!button) return;
      const on = isEinkClient();
      button.classList.toggle("active", on);
      button.setAttribute("aria-pressed", on ? "true" : "false");
      button.textContent = tLegacy(on ? "電子書 ON" : "電子書");
      button.title = tLegacy(on
        ? "目前是電子紙版面（資訊板優先、高對比）。再按可切回一般手機版。"
        : "切換成電子紙版面（BOOX / 電子書）。也可在網址加 ?eink=1。");
    }

    function dashboardMinInterval(topic) {
      if (topic === "sideboard") return isEinkClient() ? 8000 : 1000;
      if (topic === "customCards") return isEinkClient() ? 8000 : 250;
      return isEinkClient() ? 20000 : 5000;
    }

    function isFullscreenDisplayStreaming() {
      return isFullscreenDisplayStreamingPolicy(activeMode, document.body);
    }

    function suspendDashboardBackgroundWork() {
      if (!isFullscreenDisplayStreaming()) return;
      for (const state of Object.values(dashboardRefreshState)) {
        if (state.timer) clearTimeout(state.timer);
        state.timer = null;
        state.dirty = false;
      }
    }

    function resumeDashboardBackgroundWork() {
      if (isFullscreenDisplayStreaming() || document.visibilityState === "hidden") return;
      scheduleDashboardRefresh("sideboard", true);
      scheduleDashboardRefresh("quota", true);
      scheduleDashboardRefresh("customCards", true);
    }

    function scheduleDashboardRefresh(topic, immediate = false) {
      const state = dashboardRefreshState[topic];
      if (!state || isFullscreenDisplayStreaming()) return;
      state.dirty = true;
      if (document.visibilityState === "hidden" || state.timer) return;
      const elapsed = Date.now() - state.last;
      const delay = immediate ? 0 : Math.max(0, dashboardMinInterval(topic) - elapsed);
      state.timer = setTimeout(async () => {
        state.timer = null;
        if (!state.dirty || document.visibilityState === "hidden" || isFullscreenDisplayStreaming()) {
          state.dirty = false;
          return;
        }
        state.dirty = false;
        state.last = Date.now();
        if (topic === "sideboard") await refreshSideboard();
        else if (topic === "customCards") await customCardsController?.refresh();
        else if (activeMode === "sideboard") await quotaMiniController?.refresh();
        else await refreshQuotas();
        if (state.dirty) scheduleDashboardRefresh(topic);
      }, delay);
    }

    function connectDashboardEvents() {
      if (dashboardEvents || typeof EventSource === "undefined") return;
      dashboardEvents = new EventSource("/api/dashboard/events");
      dashboardEvents.onopen = () => {
        const topic = activeMode === "quota" ? "quota" : "sideboard";
        scheduleDashboardRefresh(topic, true);
      };
      dashboardEvents.addEventListener("sideboard", () => scheduleDashboardRefresh("sideboard"));
      dashboardEvents.addEventListener("quota", () => scheduleDashboardRefresh("quota"));
      dashboardEvents.addEventListener("custom-card", () => scheduleDashboardRefresh("customCards"));
      dashboardEvents.addEventListener("sync", () => {
        scheduleDashboardRefresh("sideboard");
        scheduleDashboardRefresh("quota");
        scheduleDashboardRefresh("customCards");
      });
      dashboardEvents.onerror = () => {
        // EventSource reconnects automatically. Low-frequency fallback timers remain active.
      };
    }

    function defaultStreamPreset() {
      // iPhone Safari JPEG is heavier; prefer battery/balanced over 60fps.
      if (isIos()) return "battery";
      return isMobileClient() ? "balanced" : "smooth";
    }

    function applyClientChrome() {
      const preview = isDevicePreview();
      const previewLocal = isDevicePreviewTrust("local");
      const localConsole = Boolean(deviceLocalRequest) && (!preview || previewLocal);
      const phoneClient = !localConsole && isMobileClient();
      const ios = isIos();
      const eink = isEinkClient();
      const trusted = Boolean(deviceTrusted) || isDevicePreviewTrust("paired");

      applyDevicePreviewEnvironment();
      document.documentElement.classList.toggle("eink-boot", eink);
      document.body.classList.toggle("eink-client", eink);
      const themeColor = document.querySelector('meta[name="theme-color"]');
      if (themeColor) themeColor.setAttribute("content", eink ? "#f2f0e8" : "#111820");
      document.body.classList.toggle("phone-client", phoneClient);
      document.body.classList.toggle("remote-client", !localConsole);
      document.body.classList.toggle("ios-client", ios && !localConsole);
      document.body.classList.toggle("device-trusted", trusted && !localConsole);
      document.body.classList.toggle("pc-console", localConsole);
      document.body.classList.toggle("standalone-app", isStandaloneApp());
      if (publicEndpointPanel) publicEndpointPanel.hidden = !localConsole;
      if (turnSettingsPanel) turnSettingsPanel.hidden = !localConsole;
      const pairingRescue = document.getElementById("phonePairRescue");
      if (pairingRescue) pairingRescue.hidden = !phoneClient || trusted;
      customCardsController?.syncAccess?.();
      applyForcedLandscape();
      updateIosHomeTip();
      updateEinkToggle();
      // Phone/PC empty-state CTAs depend on client chrome classes.
      displaySources.syncEmptyActions();
    }

    function updateIosHomeTip() {
      const tip = document.getElementById("iosHomeTip");
      if (!tip) return;
      const trusted = Boolean(deviceTrusted) || isDevicePreviewTrust("paired");
      tip.classList.toggle("show-install", isIos() && !isStandaloneApp());
      if (!isIos()) return;
      if (usesTrustedPublicUrl && !trusted) {
        tip.textContent = t("secureEndpoint.iosPair");
        return;
      }
      if (location.protocol !== "https:") {
        tip.innerHTML = tLegacy("iPhone 必須用 <strong>HTTPS</strong>。第一次警告請按進階並繼續前往。");
        return;
      }
      if (!trusted) {
        tip.innerHTML = tLegacy("HTTPS 就緒。回 PC 配對並掃 QR，成功後同一頁加入主畫面。");
      } else if (!isStandaloneApp()) {
        tip.innerHTML = tLegacy("已配對。分享 → <strong>加入主畫面</strong>，打開後點 <strong>長亮 ON</strong>。");
      } else {
        tip.innerHTML = tLegacy("主畫面模式。用副螢幕時點 <strong>長亮 ON</strong>。");
      }
    }

    function shouldForceLandscape() {
      if (isDevicePreview()) return false;
      return orientation?.value === "landscape" &&
        !isDeckWindow() &&
        !deviceLocalRequest &&
        (isMobileClient() || document.body.classList.contains("phone-client"));
    }

    function applyForcedLandscape() {
      if (!shouldForceLandscape()) {
        document.documentElement.classList.remove("phone-force-landscape");
        document.body.classList.remove("force-landscape");
        return;
      }

      const physicalWidth = window.visualViewport ? window.visualViewport.width : window.innerWidth;
      const physicalHeight = window.visualViewport ? window.visualViewport.height : window.innerHeight;
      const physicalPortrait = physicalHeight >= physicalWidth;

      document.documentElement.classList.toggle("phone-force-landscape", physicalPortrait);
      document.body.classList.toggle("force-landscape", physicalPortrait);

      // Browser may allow lock only after user gesture / fullscreen; best-effort.
      if (orientation?.value !== "auto" && screen.orientation && typeof screen.orientation.lock === "function") {
        screen.orientation.lock(orientation.value).catch(() => {});
      }
    }

    function getRawViewportSize() {
      const viewport = window.visualViewport;
      return {
        width: viewport ? viewport.width : window.innerWidth,
        height: viewport ? viewport.height : window.innerHeight,
      };
    }

    function syncEinkSensorOrientationClasses(width, height) {
      const forcePortrait = isEinkClient() &&
        orientation?.value === "auto" &&
        einkPhysicalOrientation.startsWith("portrait-") &&
        width > height;
      const primary = forcePortrait && einkPhysicalOrientation === "portrait-primary";
      const secondary = forcePortrait && einkPhysicalOrientation === "portrait-secondary";
      for (const root of [document.documentElement, document.body]) {
        root.classList.toggle("eink-sensor-portrait", forcePortrait);
        root.classList.toggle("eink-sensor-portrait-primary", primary);
        root.classList.toggle("eink-sensor-portrait-secondary", secondary);
      }
      return forcePortrait;
    }

    function setEinkPhysicalOrientation(value) {
      if (einkPhysicalOrientation === value) return;
      einkPhysicalOrientation = value;
      document.documentElement.dataset.einkPhysicalOrientation = value;
      updateViewportSize();
    }

    function handleEinkDeviceMotion(event) {
      if (!isEinkClient() || orientation?.value !== "auto") return;
      const gravity = event.accelerationIncludingGravity;
      const x = Number(gravity?.x);
      const y = Number(gravity?.y);
      if (!Number.isFinite(x) || !Number.isFinite(y) || Math.max(Math.abs(x), Math.abs(y)) < 3) return;

      let candidate = "";
      if (Math.abs(y) > Math.abs(x) * 1.2) {
        candidate = y >= 0 ? "portrait-primary" : "portrait-secondary";
      } else if (Math.abs(x) > Math.abs(y) * 1.2) {
        candidate = x >= 0 ? "landscape-primary" : "landscape-secondary";
      }
      if (!candidate) return;

      const now = performance.now();
      if (candidate !== einkOrientationCandidate) {
        einkOrientationCandidate = candidate;
        einkOrientationCandidateSince = now;
        return;
      }
      // E-ink readers report noisy gravity while being moved. Wait until the
      // device has settled before changing the whole dashboard canvas.
      if (now - einkOrientationCandidateSince >= 400) setEinkPhysicalOrientation(candidate);
    }

    function isIos() {
      if (isDevicePreview()) return isDevicePreview("iphone-xs");
      return isIosUA();
    }

    function isIphone() {
      if (isDevicePreview()) return isDevicePreview("iphone-xs");
      return isIphoneUA();
    }

    function prefersWebRtcDisplay() {
      return shouldPreferWebRtcDisplay({
        forced: new URLSearchParams(location.search).get("webrtc") === "1",
        hasPeerConnection: typeof window.RTCPeerConnection === "function",
        secureContext: window.isSecureContext,
        loopback: isLoopbackHost(),
      });
    }

    function isStandaloneApp() {
      if (isDevicePreview()) return true;
      return window.matchMedia("(display-mode: standalone)").matches ||
        window.navigator.standalone === true;
    }

    function isDeckWindow() {
      const value = new URLSearchParams(location.search).get("deck");
      return isLoopbackHost() && (value === "1" || value === "true");
    }

    function updateViewportSize() {
      applyDevicePreviewEnvironment();
      const rawViewport = getRawViewportSize();
      let width = rawViewport.width;
      let height = rawViewport.height;
      const forceLandscape = shouldForceLandscape() && height >= width;
      const forceEinkPortrait = syncEinkSensorOrientationClasses(width, height);

      // When CSS rotates portrait → landscape, layout metrics are swapped.
      if (forceLandscape) {
        const swapped = width;
        width = height;
        height = swapped;
      }

      // A stale installed BOOX PWA can keep Chrome's Activity locked to
      // sensor-landscape. CSS rotates that canvas, so expose portrait metrics
      // to the dashboard layout while the fallback is active.
      if (forceEinkPortrait) {
        const swapped = width;
        width = height;
        height = swapped;
      }

      document.documentElement.style.setProperty("--viewer-width", `${width}px`);
      document.documentElement.style.setProperty("--viewer-height", `${height}px`);
      document.body.classList.toggle("viewport-portrait", !forceLandscape && height >= width);
      document.body.classList.toggle("viewport-landscape", forceLandscape || width > height);
      applyForcedLandscape();
      responsiveSpaceController.refresh();
    }

    function describeClient() {
      updateInstallState();
      if (isIos()) {
        deviceState.textContent = location.protocol === "https:"
          ? (isStandaloneApp() ? "iPhone HTTPS 主畫面模式。" : "iPhone HTTPS。可分享 → 加入主畫面。")
          : "iPhone 必須改用 HTTPS。";
        return;
      }

      deviceState.textContent = "手機模式。";
    }

    function buildHttpsUrlFromCurrent() {
      const host = location.hostname;
      if (!host || host === "localhost" || host === "127.0.0.1") {
        return "";
      }
      const path = location.pathname || "/index.html";
      const search = location.search || "";
      const hash = location.hash || "";
      return `https://${host}:5443${path}${search}${hash}`;
    }

    /** iPhone single path: never use HTTP for the app UI. */
    function enforceMobileHttpsPath() {
      if (!isMobileClient() || isLoopbackHost()) {
        document.body.classList.remove("mobile-http-blocked");
        return false;
      }

      if (location.protocol === "https:") {
        document.body.classList.remove("mobile-http-blocked");
        return false;
      }

      // Every phone platform uses one origin and one onboarding path.
      document.body.classList.add("mobile-http-blocked");
      const httpsUrl = buildHttpsUrlFromCurrent();
      const openBtn = document.getElementById("mobileGateOpenHttps");
      const link = document.getElementById("mobileGateHttpsUrl");
      if (link && httpsUrl) {
        link.href = httpsUrl;
        link.textContent = httpsUrl;
      }
      if (openBtn) {
        openBtn.onclick = () => {
          if (httpsUrl) {
            location.replace(httpsUrl);
          }
        };
      }
      return true;
    }

    function setMode(mode) {
      activeMode = mode;
      const isSetup = mode === "setup";
      const isSideboard = mode === "sideboard";
      const isQuota = mode === "quota";
      const isDeck = mode === "deck" || mode.startsWith("deck:");
      const isDisplay = !isSetup && !isSideboard && !isQuota && !isDeck;
      // A leftover "paired" toast must never sit on top of the display stream.
      if (!isSideboard && !isQuota) pairingSession.hideSuccess();
      document.body.classList.toggle("mode-display", isDisplay);
      document.body.classList.toggle("mode-setup", isSetup);
      document.body.classList.toggle("mode-sideboard", isSideboard);
      document.body.classList.toggle("mode-quota", isQuota);
      document.body.classList.toggle("mode-deck", isDeck);
      if (isSetup || isSideboard || isQuota || isDeck) {
        document.body.classList.remove("display-settings-open");
        displaySettingsToggle?.setAttribute("aria-expanded", "false");
        if (displaySettingsToggle) displaySettingsToggle.textContent = t("ui.settings");
      }
      displayView.classList.toggle("active", isDisplay);
      sideboardView.classList.toggle("active", isSideboard);
      quotaView.classList.toggle("active", isQuota);
      customDeckView.classList.toggle("active", isDeck);
      requestAnimationFrame(() => responsiveSpaceController.refresh());
      displayMode.classList.toggle("active", isDisplay);
      setupMode?.classList.toggle("active", isSetup);
      sideboardMode.classList.toggle("active", isSideboard);
      quotaMode.classList.toggle("active", isQuota);
      addDeckMode.classList.toggle("active", mode === "deck");
      customDeckController?.activate(mode);
      document.querySelectorAll("[data-dashboard-mode]").forEach(button => {
        const active = button.dataset.dashboardMode === mode;
        button.classList.toggle("active", active);
        button.setAttribute("aria-pressed", active ? "true" : "false");
      });
      if (!isDevicePreview()) localStorage.setItem("vibeDeckViewMode", mode);

      if (isSetup) {
        if (document.body.classList.contains("viewer-fullscreen") || document.body.classList.contains("dashboard-viewer")) {
          exitLandscapeViewer();
        }
      } else if (isSideboard) {
        customCardsController?.setPage("system", false);
        scheduleDashboardRefresh("sideboard", true);
        scheduleDashboardRefresh("customCards", true);
        if (document.body.classList.contains("viewer-fullscreen")) {
          exitLandscapeViewer();
        }
      } else if (isQuota) {
        scheduleDashboardRefresh("quota", true);
        if (document.body.classList.contains("viewer-fullscreen")) {
          exitLandscapeViewer();
        }
      } else if (isDeck) {
        if (document.body.classList.contains("viewer-fullscreen")) {
          exitLandscapeViewer();
        }
      } else if (isDisplay && displaySources.getSelectedName() && canUseProtectedConnection()) {
        scheduleAutoDisplayMode(120);
        connectVideo();
      }
    }

    function getInitialMode() {
      const params = new URLSearchParams(location.search);
      const requestedMode = params.get("mode");
      if (["display", "setup", "sideboard", "quota"].includes(requestedMode)) {
        return isEinkClient() && requestedMode === "display" ? "sideboard" : requestedMode;
      }
      if (requestedMode === "deck") {
        const requestedDeck = (params.get("deck") || "").trim();
        return requestedDeck ? `deck:${requestedDeck}` : "deck";
      }

      if (shouldDefaultToFirstDeviceSetup) return "setup";
      const stored = localStorage.getItem("vibeDeckViewMode") || "";
      if (isEinkClient() && (stored === "display" || !stored)) return "sideboard";
      if (stored === "deck" || stored.startsWith("deck:")) return stored;
      return stored || "display";
    }

    function shouldStartInViewer() {
      const value = new URLSearchParams(location.search).get("viewer");
      return value === "1" || value === "true";
    }

    function setSideSkin(skin) {
      const nextSkin = ["command", "dial", "focus"].includes(skin) ? skin : "command";
      sideboardShell.classList.remove("skin-command", "skin-dial", "skin-focus");
      sideboardShell.classList.add(`skin-${nextSkin}`);
      for (const button of document.querySelectorAll("[data-side-skin]")) {
        button.classList.toggle("active", button.dataset.sideSkin === nextSkin);
      }
      localStorage.setItem("vibeDeckSideSkin", nextSkin);
    }

    function setBar(element, value) {
      const percent = Number.isFinite(value) ? Math.max(0, Math.min(100, value)) : 0;
      element.style.width = `${percent}%`;
    }

    function setText(element, value) {
      element.textContent = value == null || value === "" ? "--" : translateText(String(value));
    }

    function parseJsonResponse(text, label) {
      const raw = (text || "").replace(/^\uFEFF/, "").trim();
      if (!raw) return {};
      try {
        return JSON.parse(raw);
      } catch (error) {
        const preview = raw.slice(0, 80).replace(/\s+/g, " ");
        const looksHtml = /^<!doctype|^<html/i.test(raw);
        throw new Error(
          looksHtml
            ? `${label || "API"} 回了 HTML 而不是 JSON（多半是離線頁或連不到 Host）。請確認 PC Host 有開、iPhone 用 https://<PC-IP>:5443。`
            : `${label || "API"} JSON 解析失敗：${preview || error.message}`
        );
      }
    }

    function canUseProtectedConnection() {
      return deviceTrusted || deviceLocalRequest || hostAuthController.isAuthenticated();
    }

    function setTrustState(message, good) {
      trustState.textContent = message || "";
      trustState.classList.toggle("good", Boolean(good));
      trustState.classList.toggle("warn", !good);
    }

    function setPairingProgress(percent, message) {
      const idle = percent == null;
      const value = idle ? 0 : Math.max(0, Math.min(100, Number(percent) || 0));
      document.querySelectorAll("[data-pair-progress]").forEach(element => {
        element.style.setProperty("--pair-progress", `${value}%`);
        element.classList.toggle("idle", idle);
        element.classList.toggle("complete", value >= 100);
        const meter = element.querySelector("[data-pair-progress-bar]");
        const label = element.querySelector("[data-pair-progress-label]");
        const detail = element.querySelector("[data-pair-progress-detail]");
        if (meter) {
          meter.value = value;
          meter.setAttribute("aria-valuenow", String(value));
        }
        if (label) label.textContent = `${value}%`;
        if (detail) detail.textContent = message || "準備配對";
      });
    }

    function setPairingStep(element, state) {
      element.classList.toggle("done", state === "done");
      element.classList.toggle("active", state === "active");
    }

    function updatePairingGuide(state) {
      const normalized = state || "pair";
      setPairingStep(pairingStepInstall, normalized === "scan" ? "active" : "done");
      setPairingStep(
        pairingStepPair,
        normalized === "pair" || normalized === "warning"
          ? "active"
          : normalized === "approve" || normalized === "paired" ? "done" : ""
      );
      setPairingStep(pairingStepOpen, normalized === "approve" ? "active" : normalized === "paired" ? "done" : "");
      if (normalized === "paired") {
        qrCaption.textContent = tLegacy("手機已配對，可以直接使用 VibeDeck。");
      } else if (normalized === "approve") {
        qrCaption.textContent = tLegacy("手機申請已送達；請核對驗證碼並按「允許」。");
      } else {
        qrCaption.textContent = usesTrustedPublicUrl
          ? t("secureEndpoint.qrScan")
          : tLegacy("手機掃碼；若出現瀏覽器警告，按「進階」→「繼續前往」。");
      }
    }

    function computePairingNeedsAdvancedConnect(info = {}) {
      const connectorManaged = Boolean(info.PublicConnectorManaged ?? info.publicConnectorManaged);
      const connectorState = String(info.PublicConnectorState || info.publicConnectorState || "").trim().toLowerCase();
      const connectorError = String(info.PublicConnectorError || info.publicConnectorError || "").trim();
      const connectorHealthy = info.PublicConnectorHealthy ?? info.publicConnectorHealthy;
      const httpsAvailable = info.HttpsAvailable ?? info.httpsAvailable;
      const transitional = connectorState === "provisioning"
        || connectorState === "starting"
        || connectorState === "waiting"
        || connectorState === "";

      // Healthy trusted URL or normal local QR pairing must not surface tunnel/cert chrome.
      if (connectorState === "error") return true;
      if (connectorManaged && connectorError && !transitional) return true;
      if (connectorManaged && connectorHealthy === false && !transitional) return true;
      // Local HTTPS missing: advanced panel has setup hint / cert recovery.
      if (httpsAvailable === false) return true;
      return false;
    }

    function syncAdvancedConnectLinksForPairing() {
      const links = document.querySelector(".connect-links");
      if (!links) return;

      const pairingActive = document.body.classList.contains("pairing-active") || pairingQrActive;
      document.body.classList.toggle("pairing-needs-advanced", pairingActive && pairingNeedsAdvancedConnect);

      if (pairingActive && !pairingNeedsAdvancedConnect) {
        // Hide completely during a healthy pairing wait — do not leave a summary row.
        links.open = false;
        links.hidden = true;
        links.setAttribute("hidden", "");
        return;
      }

      links.hidden = false;
      links.removeAttribute("hidden");
      if (pairingActive && pairingNeedsAdvancedConnect) {
        // Failure path: open so the user sees the repair surface without an extra click.
        links.open = true;
        return;
      }

      // Outside pairing: keep advanced collapsed when the trusted public URL is healthy.
      if (usesTrustedPublicUrl) links.open = false;
    }

    function updatePublicEndpointUi(info) {
      lastConnectInfoSnapshot = info || null;
      const publicUrl = String(info.PublicUrl || info.publicUrl || "").trim();
      const installationId = String(info.InstallationId || info.installationId || "").trim();
      const baseDomain = String(info.PublicBaseDomain || info.publicBaseDomain || "").trim();
      const expectedUrl = installationId && baseDomain
        ? `https://${installationId}.${baseDomain}/`
        : "";
      const connectorManaged = Boolean(info.PublicConnectorManaged ?? info.publicConnectorManaged);
      const connectorState = String(info.PublicConnectorState || info.publicConnectorState || "").trim().toLowerCase();
      usesTrustedPublicUrl = Boolean(info.UsesTrustedPublicUrl ?? info.usesTrustedPublicUrl) && Boolean(publicUrl);
      pairingNeedsAdvancedConnect = computePairingNeedsAdvancedConnect(info);
      document.body.classList.toggle("trusted-public-endpoint", usesTrustedPublicUrl);
      syncAdvancedConnectLinksForPairing();
      if (pairingQrActive) {
        setTrustState(
          usesTrustedPublicUrl
            ? t("secureEndpoint.pairPrompt")
            : tLegacy("請用手機掃描 QR Code；若出現警告，按「進階」→「繼續前往」，再於手機按「連接這台電腦」。"),
          true
        );
        updatePairingGuide("scan");
      }

      if (pairingStepInstallTitle) {
        pairingStepInstallTitle.textContent = t("secureEndpoint.localScanTitle");
      }
      if (pairingStepInstallHint) {
        pairingStepInstallHint.textContent = usesTrustedPublicUrl
          ? t("secureEndpoint.scanHint")
          : t("secureEndpoint.localScanHint");
      }
      if (pairingStepPairTitle) {
        pairingStepPairTitle.textContent = usesTrustedPublicUrl
          ? t("secureEndpoint.pairTitle")
          : t("secureEndpoint.localWarningTitle");
      }
      if (pairingStepPairHint) {
        pairingStepPairHint.textContent = usesTrustedPublicUrl
          ? t("secureEndpoint.pairHint")
          : t("secureEndpoint.localWarningHint");
      }

      if (publicEndpointInstallationId) {
        publicEndpointInstallationId.textContent = installationId || "--";
      }
      if (publicEndpointUrl && document.activeElement !== publicEndpointUrl) {
        publicEndpointUrl.value = publicUrl;
        publicEndpointUrl.placeholder = expectedUrl || t("secureEndpoint.urlPlaceholder");
      }
      if (publicEndpointStatus) {
        const managedStatusKey = connectorState === "provisioning"
          ? "secureEndpoint.statusProvisioning"
          : connectorState === "starting" || connectorState === "waiting"
            ? "secureEndpoint.statusStarting"
            : connectorState === "error"
              ? "secureEndpoint.statusError"
              : "secureEndpoint.statusAutomatic";
        publicEndpointStatus.textContent = usesTrustedPublicUrl
          ? t("secureEndpoint.statusEnabled")
          : connectorManaged
            ? t(managedStatusKey)
            : t("secureEndpoint.statusPending", { url: expectedUrl || "--" });
      }
      if (publicEndpointControls) {
        publicEndpointControls.hidden = connectorManaged;
      }
      if (publicEndpointHint) {
        publicEndpointHint.textContent = connectorManaged
          ? t("secureEndpoint.automaticHint")
          : t("secureEndpoint.manualHint");
      }
      if (clearPublicEndpoint) {
        clearPublicEndpoint.hidden = connectorManaged || !usesTrustedPublicUrl;
      }
      if (certificateSetupTitle) {
        certificateSetupTitle.hidden = usesTrustedPublicUrl;
      }
      if (deviceConnectionCodePanel) {
        deviceConnectionCodePanel.hidden = !usesTrustedPublicUrl;
      }
      if (deviceConnectionCodeUrl) {
        deviceConnectionCodeUrl.textContent = baseDomain || "vibedeck.pp.ua";
      }
      if (!usesTrustedPublicUrl && deviceConnectionCode) {
        deviceConnectionCode.textContent = "--------";
      }
      if (!usesTrustedPublicUrl && deviceConnectionCodeStatus) {
        deviceConnectionCodeStatus.textContent = t("secureEndpoint.deviceCodeIdle");
      }
      applyClientChrome();
      updateIosHomeTip();
    }

    function formatDeviceConnectionCode(code) {
      const normalized = String(code || "").replace(/[^A-Z2-9]/gi, "").toUpperCase();
      return normalized.length === 8 ? `${normalized.slice(0, 4)}-${normalized.slice(4)}` : normalized || "--------";
    }

    async function createDeviceConnectionCode() {
      if (!deviceLocalRequest || !usesTrustedPublicUrl || !generateDeviceConnectionCode) return;
      const originalText = generateDeviceConnectionCode.textContent;
      generateDeviceConnectionCode.disabled = true;
      generateDeviceConnectionCode.textContent = t("secureEndpoint.deviceCodeGenerating");
      try {
        const result = await fetchJsonOrThrow("/api/connect/device-code", { method: "POST" });
        const code = String(result.code || result.Code || "");
        const expiresAt = String(result.expiresAt || result.ExpiresAt || "");
        if (!code || !expiresAt) throw new Error("connection_code.invalid_response");
        if (deviceConnectionCode) deviceConnectionCode.textContent = formatDeviceConnectionCode(code);
        if (deviceConnectionCodeStatus) {
          const expiry = new Date(expiresAt);
          const time = Number.isNaN(expiry.getTime())
            ? "--"
            : expiry.toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit" });
          deviceConnectionCodeStatus.textContent = t("secureEndpoint.deviceCodeReady", { expiresAt: time });
        }
      } catch {
        if (deviceConnectionCodeStatus) deviceConnectionCodeStatus.textContent = t("secureEndpoint.deviceCodeFailed");
      } finally {
        generateDeviceConnectionCode.disabled = false;
        generateDeviceConnectionCode.textContent = originalText;
      }
    }

    async function saveTrustedPublicEndpoint() {
      if (!deviceLocalRequest || !publicEndpointUrl || !savePublicEndpoint) return;
      const originalText = savePublicEndpoint.textContent;
      savePublicEndpoint.disabled = true;
      savePublicEndpoint.textContent = t("secureEndpoint.saveBusy");
      try {
        await fetchJsonOrThrow("/api/connect/public-endpoint", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ publicUrl: publicEndpointUrl.value.trim() })
        });
        await loadConnectInfo();
        showInstallQr();
        setTrustState(t("secureEndpoint.saved"), true);
      } catch (error) {
        if (publicEndpointStatus) publicEndpointStatus.textContent = error.message || t("secureEndpoint.saveFailed");
      } finally {
        savePublicEndpoint.disabled = false;
        savePublicEndpoint.textContent = originalText;
      }
    }

    async function clearTrustedPublicEndpoint() {
      if (!deviceLocalRequest || !clearPublicEndpoint) return;
      const originalText = clearPublicEndpoint.textContent;
      clearPublicEndpoint.disabled = true;
      try {
        await fetchJsonOrThrow("/api/connect/public-endpoint", { method: "DELETE" });
        await loadConnectInfo();
        showInstallQr();
        setTrustState(t("secureEndpoint.cleared"), true);
      } catch (error) {
        if (publicEndpointStatus) publicEndpointStatus.textContent = error.message || t("secureEndpoint.clearFailed");
      } finally {
        clearPublicEndpoint.disabled = false;
        clearPublicEndpoint.textContent = originalText;
      }
    }

    function showInstallQr({ activate = pairingQrActive } = {}) {
      pairingQrActive = Boolean(activate);
      setPairingUiActive(pairingQrActive);
      if (qrCode) {
        if (qrCode.dataset.blobUrl) {
          URL.revokeObjectURL(qrCode.dataset.blobUrl);
          delete qrCode.dataset.blobUrl;
        }
        qrCode.src = `/qr.svg?t=${Date.now()}`;
      }
      if (deviceTrusted && !deviceLocalRequest) {
        updatePairingGuide("paired");
        setPairingProgress(100, `${pairingSession.deviceName()} 可繼續使用`);
        return true;
      }
      updatePairingGuide(pairingQrActive ? "scan" : "pair");
      if (pairingQrActive) setPairingProgress(null, t("pairingUx.waitingForPhone"));
      return true;
    }

    function appendDeviceToken(params) {
      if (deviceToken) {
        params.set("deviceToken", deviceToken);
      }
      return params;
    }

    function syncDeviceStatusPolling() {
      const nextInterval = deviceLocalRequest ? 3000 : deviceTrusted ? 10000 : 0;
      if (nextInterval === deviceStatusInterval) return;
      if (deviceStatusTimer) clearInterval(deviceStatusTimer);
      deviceStatusTimer = null;
      deviceStatusInterval = nextInterval;
      if (nextInterval > 0) {
        deviceStatusTimer = setInterval(() => {
          if (!isFullscreenDisplayStreaming()) loadDeviceTrustStatus();
        }, nextInterval);
      }
    }

    function setPairControlsVisible(localConsole) {
      if (setupMode) {
        setupMode.hidden = !localConsole;
        if (localConsole) setupMode.removeAttribute("hidden");
        else setupMode.setAttribute("hidden", "");
      }
      if (launchDeckWindow) {
        launchDeckWindow.hidden = !localConsole;
        if (localConsole) launchDeckWindow.removeAttribute("hidden");
        else launchDeckWindow.setAttribute("hidden", "");
      }
      productUpdates.setLocalConsole(localConsole);
    }

    async function loadDeviceTrustStatus() {
      try {
        const identity = await pairingSession.resolveDeviceInfo();
        let result = await fetchJsonOrThrow("/api/devices/status", {
          headers: {
            "X-VibeDeck-Client-Instance": identity.clientInstanceId,
            "X-VibeDeck-Device-Model": encodeURIComponent(identity.model || "")
          }
        });
        deviceHeaderName = result.DeviceHeader || result.deviceHeader || deviceHeaderName;
        deviceTrusted = Boolean(result.Trusted ?? result.trusted);

        // The same HTTPS origin can temporarily point at an installed Host or a
        // source-development Host with a separate trust database. Re-pairing on
        // one rotates that realm's token. Keep a bounded history and recover the
        // token accepted by the currently running Host instead of looking
        // permanently unpaired when switching back.
        if (!deviceTrusted) {
          const originalToken = deviceToken;
          const candidates = [
            readCookie(DEVICE_COOKIE),
            ...loadDeviceTokenHistory(),
          ].map(value => String(value || "").trim())
            .filter((value, index, values) => value && value !== originalToken && values.indexOf(value) === index);
          for (const candidate of candidates) {
            deviceToken = candidate;
            try {
              const candidateResult = await fetchJsonOrThrow("/api/devices/status", {
                headers: {
                  "X-VibeDeck-Client-Instance": identity.clientInstanceId,
                  "X-VibeDeck-Device-Model": encodeURIComponent(identity.model || "")
                }
              });
              if (candidateResult.Trusted ?? candidateResult.trusted) {
                deviceToken = originalToken;
                persistDeviceCredentials(candidate, deviceId);
                result = candidateResult;
                deviceTrusted = true;
                break;
              }
            } catch {
              // Try the next credential; the normal status UI handles failure.
            } finally {
              if (!deviceTrusted) deviceToken = originalToken;
            }
          }
        }
        if (isDevicePreview()) {
          const previewTrusted = isDevicePreviewTrust("paired");
          const previewLocal = isDevicePreviewTrust("local");
          result = {
            ...result,
            Trusted: previewTrusted,
            LocalRequest: previewLocal,
            PairedDeviceCount: previewTrusted ? 1 : 0,
            CurrentDevice: previewTrusted ? { Name: pairingSession.deviceName() } : null,
            Devices: []
          };
          deviceTrusted = previewTrusted;
        }
        deviceLocalRequest = isDevicePreview()
          ? isDevicePreviewTrust("local")
          : Boolean(result.LocalRequest ?? result.localRequest) || isLoopbackHost();
        const pairedDeviceCount = Number(result.PairedDeviceCount ?? result.pairedDeviceCount ?? 0);
        let storedViewMode = "";
        if (!isDevicePreview()) {
          try { storedViewMode = localStorage.getItem("vibeDeckViewMode") || ""; } catch { }
        }
        shouldDefaultToFirstDeviceSetup = deviceLocalRequest &&
          pairedDeviceCount === 0 &&
          !new URLSearchParams(location.search).get("mode") &&
          !storedViewMode;
        const currentDevice = result.CurrentDevice || result.currentDevice;
        setPairControlsVisible(deviceLocalRequest);
        deviceManagementView.render(result, deviceLocalRequest);

        if (deviceLocalRequest) {
          setTrustState(t("pairingUx.trustLocal"), true);
          if (!pairingQrActive) setPairingProgress(null, t("pairingUx.readyForNewDevice"));
          if (!pairingQrActive) updatePairingGuide("pair");
          deviceActions.loadPending();
          if (!pendingApprovalTimer) pendingApprovalTimer = setInterval(deviceActions.loadPending, 2000);
        } else if (isDevicePreviewTrust("pending")) {
          const previewCode = "245 731";
          setTrustState(t("pairingUx.waitingForApprovalCode", { code: previewCode }), false);
          setPairingProgress(70, t("pairingUx.waitingForApprovalCode", { code: previewCode }));
          updatePairingGuide("approve");
          if (phonePairIntro) phonePairIntro.textContent = t("pairingUx.phoneWaitingApproval", { code: previewCode });
          if (phonePairRequest) {
            phonePairRequest.disabled = true;
            phonePairRequest.textContent = t("pairingUx.requestSent");
          }
        } else if (isDevicePreviewTrust("unpaired")) {
          setTrustState(tLegacy("請按「連接這台電腦」，再回 PC 按允許。"), false);
          setPairingProgress(null, t("pairingUx.readyToConnect"));
          updatePairingGuide("pair");
          if (phonePairIntro) phonePairIntro.textContent = t("pairingUx.phoneIntro");
          if (phonePairRequest) {
            phonePairRequest.disabled = false;
            phonePairRequest.textContent = t("pairingUx.phoneTitle");
          }
        } else if (hostAuthController.isAuthenticated()) {
          setTrustState(t("pairingUx.trustRemoteOk"), true);
          setPairingProgress(100, t("pairingUx.trustRemoteProgress"));
          updatePairingGuide("paired");
        } else if (deviceTrusted) {
          const name = currentDevice?.Name || currentDevice?.name || t("pairingUx.trustPairedDevice");
          setTrustState(t("pairingUx.trustPaired", { name }), true);
          setPairingProgress(100, t("pairingUx.deviceCanContinue", { name }));
          updatePairingGuide("paired");
        } else if (hostAuthController.isRequired() && !hostAuthController.isAuthenticated()) {
          setTrustState(t("pairingUx.trustLoginRequired"), false);
          setPairingProgress(10, t("pairingUx.trustLoginProgress"));
          updatePairingGuide("install");
        } else {
          if (pairingSession.consumeConnectionCodeAutoPairing()) {
            pairingSession.stopPolling();
            pairingSession.clearStoredPending();
            pairingSession.request({ allowCreate: true }).catch(error => setTrustState(error.message || t("pairingUx.pairRequestFailed"), false));
          } else if (pairingSession.hasStoredPending()) {
            pairingSession.request().catch(error => setTrustState(error.message || t("pairingUx.pairRequestFailed"), false));
          } else {
            setTrustState(tLegacy("請按「連接這台電腦」，再回 PC 按允許。"), false);
            setPairingProgress(null, t("pairingUx.readyToConnect"));
            updatePairingGuide("pair");
          }
        }

        applyClientChrome();
        syncDeviceStatusPolling();
        return result;
      } catch (error) {
        // A transient /api/devices/status failure must not drop a phone that is
        // already paired: forcing deviceTrusted=false here flashed the pairing
        // rescue panel back over the live secondary-screen view on iOS blips.
        // Keep the last known trust; only a successful response changes it.
        deviceLocalRequest = deviceLocalRequest || isLoopbackHost();
        setPairControlsVisible(deviceLocalRequest);
        if (!deviceTrusted) deviceManagementView.render(null, deviceLocalRequest);
        setTrustState(error.message || t("pairingUx.trustStatusUnavailable"), deviceTrusted);
        if (!deviceTrusted) setPairingProgress(null, t("pairingUx.reconnecting"));
        applyClientChrome();
        syncDeviceStatusPolling();
        return null;
      }
    }

    function setPairingUiActive(active) {
      document.body.classList.toggle("pairing-active", Boolean(active));
      if (active && newDeviceConnectPanel) {
        newDeviceConnectPanel.hidden = false;
        newDeviceConnectPanel.open = true;
      }
      // Re-evaluate with the latest connect snapshot so a healthy wait never
      // re-opens tunnel / cert details, while connector errors still surface.
      if (lastConnectInfoSnapshot) {
        pairingNeedsAdvancedConnect = computePairingNeedsAdvancedConnect(lastConnectInfoSnapshot);
      }
      syncAdvancedConnectLinksForPairing();
    }

    function startPhonePairing() {
      pairingQrActive = true;
      setPairingUiActive(true);
      setPairingProgress(null, t("pairingUx.preparingQr"));
      showInstallQr({ activate: true });
      setTrustState(
        usesTrustedPublicUrl
          ? t("secureEndpoint.pairPrompt")
          : tLegacy("請用手機掃描 QR Code；若出現警告，按「進階」→「繼續前往」，再於手機按「連接這台電腦」。"),
        true
      );
      setStatus("手機 QR Code 已顯示", true);
      updatePairingGuide("scan");
      // Keep the QR in view after the panel expands (PC setup can be long).
      requestAnimationFrame(() => {
        document.querySelector(".connect-qr")?.scrollIntoView({ block: "nearest", behavior: "smooth" });
      });
    }

    async function launchDeckOnVirtualDisplay() {
      const mode = activeMode === "quota" ? "quota" : "sideboard";
      const originalText = launchDeckWindow.textContent;
      launchDeckWindow.disabled = true;
      launchDeckWindow.textContent = t("ui.launchDeckOpening");

      try {
        const result = await fetchJsonOrThrow("/api/deck/launch", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ mode })
        });
        setAppState(result.Message || result.message || t("ui.launchDeckSuccess"), true);
      } catch (error) {
        setAppState(error.message || t("ui.launchDeckFailed"), false);
      } finally {
        launchDeckWindow.disabled = false;
        launchDeckWindow.textContent = originalText;
      }
    }

    async function ensureActionToken() {
      if (actionToken) return actionToken;
      const response = await fetch("/api/session", { cache: "no-store" });
      const data = parseJsonResponse(await response.text(), "/api/session");
      if (!response.ok) {
        throw new Error(data.error || data.message || `取得 session 失敗 HTTP ${response.status}`);
      }
      actionToken = data.ActionToken || data.actionToken || "";
      actionHeaderName = data.ActionHeader || data.actionHeader || actionHeaderName;
      deviceHeaderName = data.DeviceHeader || data.deviceHeader || deviceHeaderName;
      const version = data.Version || data.version || "";
      if (version) {
        hostVersionLabel = version;
        applyHostVersionLabel();
      }
      if (!actionToken) throw new Error("session 沒有 action token。");
      return actionToken;
    }

    function applyHostVersionLabel() {
      const el = document.getElementById("hostVersionLabel");
      if (!el || !hostVersionLabel) return;
      el.textContent = `v${hostVersionLabel}`;
      el.hidden = false;
      el.title = `VibeDeck Host ${hostVersionLabel}`;
    }

    function createAuditTraceId() {
      const generated = window.crypto?.randomUUID?.()
        || `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
      return generated.replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 16).padEnd(8, "0");
    }

    function reportBrowserFault(kind, message, source = "", line = 0, column = 0, traceId = "") {
      if (!deviceLocalRequest || !actionToken || navigator.onLine === false) return;
      const headers = new Headers({
        "Content-Type": "application/json",
        "X-VibeDeck-Trace-Id": traceId || createAuditTraceId()
      });
      if (deviceToken) headers.set(deviceHeaderName, deviceToken);
      headers.set(actionHeaderName, actionToken);
      fetch("/api/diagnostics/audit/browser-error", {
        method: "POST",
        cache: "no-store",
        headers,
        body: JSON.stringify({ kind, message, source, line, column })
      }).catch(() => {});
    }

    async function fetchJsonOrThrow(url, init = {}, retryOnTokenRefresh = true) {
      const method = String(init.method || "GET").toUpperCase();
      const headers = new Headers(init.headers || {});
      const traceId = headers.get("X-VibeDeck-Trace-Id") || createAuditTraceId();
      headers.set("X-VibeDeck-Trace-Id", traceId);
      if (deviceToken) {
        headers.set(deviceHeaderName, deviceToken);
      }
      if (method !== "GET" && method !== "HEAD") {
        const token = await ensureActionToken();
        headers.set(actionHeaderName, token);
      }

      let response;
      try {
        response = await fetch(url, { cache: "no-store", ...init, headers });
      } catch (networkError) {
        const failure = new Error(`${tLegacy(`連線失敗：${url}（${networkError.message || "network error"}）`)} · ${tLegacy("追蹤碼")} ${traceId}`);
        failure.traceId = traceId;
        failure.requestUrl = url;
        reportBrowserFault("network", failure.message, "", 0, 0, traceId);
        throw failure;
      }
      const responseTraceId = response.headers.get("X-VibeDeck-Trace-Id") || traceId;
      const text = await response.text();
      const data = parseJsonResponse(text, url);
      if (response.status === 401) {
        await hostAuthController.loadStatus().catch(() => {});
      }
      if (response.status === 403 && retryOnTokenRefresh && method !== "GET" && method !== "HEAD") {
        actionToken = "";
        return fetchJsonOrThrow(url, init, false);
      }

      if (!response.ok) {
        const errorPayload = data.error && typeof data.error === "object" ? data.error : null;
        const rawMessage = errorPayload?.message ||
          data.Message ||
          data.message ||
          (typeof data.error === "string" ? data.error : null) ||
          `HTTP ${response.status}`;
        const code = errorPayload?.code || data.code || data.Code || "";
        const error = new Error(`${tApi(code, rawMessage)} · ${tLegacy("追蹤碼")} ${responseTraceId}`);
        error.code = code;
        error.status = response.status;
        error.traceId = responseTraceId;
        error.requestUrl = url;
        throw error;
      }
      return data;
    }

    function isTrustRequiredError(error) {
      return (error?.status === 401 || error?.status === 403) &&
        /not paired|trust|token|login/i.test(error.message || "");
    }

    customDeckController = createCustomDeckController({
      switcher: document.querySelector(".view-switcher"),
      addButton: addDeckMode,
      frame: customDeckFrame,
      help: customDeckHelp,
      folderPath: customDeckFolderPath,
      status: customDeckStatus,
      issues: customDeckIssues,
      openFolderButton: customDeckOpenFolder,
      openExampleButton: customDeckOpenExample,
      refreshButton: customDeckRefresh,
      fetchJsonOrThrow,
      navigate: setMode,
      getActiveMode: () => activeMode,
      isLocalRequest: () => deviceLocalRequest,
      shouldPoll: () => !isFullscreenDisplayStreaming(),
    });

    const activityFeedController = createActivityFeedController({
      elements: {
        card: activityFeedCard,
        list: activityFeedList,
        filters: activityFeedFilters,
        select: activityFeedFilterSelect,
      },
    });
    // Keep the merged activity card independent from custom-card rendering.
    // This guarantees the feed receives the Windows payload even when the
    // custom-card page is hidden or its stream renderer is busy.
    const refreshActivityNotifications = async () => {
      if (isFullscreenDisplayStreaming()) return;
      try {
        const snapshot = await fetchJsonOrThrow("/api/custom-cards");
        const card = (snapshot?.cards || []).find(item => item.sourceKey === "windows-notifications") || null;
        activityFeedController.setWindowsNotifications(card);
      } catch (error) {
        if (!isTrustRequiredError(error)) console.debug("activity notifications refresh failed", error);
      }
    };
    quotaMiniController = createQuotaMiniCardController({
      elements: {
        select: quotaMiniSource,
        value: quotaMiniValue,
        bar: quotaMiniBar,
        reset: quotaMiniReset,
        state: quotaMiniState,
        credits: quotaMiniCredits,
      },
      fetchJsonOrThrow,
    });

    const sideboardController = createSideboardController({
      elements: {
        sideHeadline,
        sideSummary,
        sideError,
        sideLoad,
        sideLoadNormal,
        sideLoadStatus,
        sideLoadStatusReason,
        sideLoadAlert,
        sideLoadAlertTitle,
        sideLoadAlertReason,
        sideHost,
        sideUptime,
        sideHealth,
        sideCpu,
        sideCpuSub,
        sideCpuBar,
        sideRam,
        sideRamSub,
        sideRamBar,
        sideGpu,
        sideGpuSub,
        sideGpuBar,
        sideVram,
        sideVramSub,
        sideVramBar,
        sideNet,
        sideNetSub,
        sideNetBar,
        sideDisk,
        sideDiskSub,
        sideDiskBar,
        sideDiskIo,
        sideWeather,
        sideWeatherSub,
        sideProcessList,
      },
      fetchJsonOrThrow,
      isTrustRequiredError,
      getActiveMode: () => activeMode,
      setText,
      setBar,
      formatters: {
        averagePercent,
        describeWeatherCode,
        formatGb,
        formatMbps,
        formatPercent,
        formatSeconds,
        formatTemperature,
        formatWeatherLocation,
      },
      onConnectionChange: state => setDashboardConnectionState(state),
      onWorkPulse: workPulse => activityFeedController.setWorkPulse(workPulse),
    });
    createMobileOverviewController();
    const refreshSideboard = () => Promise.all([
      sideboardController.refresh(),
      quotaMiniController.refresh(),
    ]);

    try {
      customCardsController = createCustomCardsController({
        elements: {
          customSideboardPage,
          systemSideboardPage,
          customPageTabs: sideboardPageTabs,
          customCardsGrid,
          customCardsStatus,
          customRefresh: customRefreshCards,
          customSettingsButton,
          customCardSettingsPanel,
          customSettingsClose,
          customCardSettingsForm,
          customSettingsCard,
          customSettingsMaxItems,
          customSettingsStreamEnabled,
          customSettingsStreamDelay,
          customSettingsHint,
          customSettingsSave,
          customSettingsClear,
          customManageButton,
          customSourcesManager,
          customSourceList,
          customSourceForm,
          customSourceFormTitle,
          customSourceKey,
          customSourceDisplayName,
          customCardType,
          customCardTitle,
          customCardPosition,
          customStaleAfter,
          customDefaultTtl,
          customMaxItems,
          customSourceFormSubmit,
          customSourceCancel,
          customCredentialPanel,
          customCredentialText,
          customCredentialCopy,
          customCredentialClose,
          customAddSource,
          customManagerAdd,
          customManagerClose,
          windowsNotificationControl,
          windowsNotificationStatus,
          windowsNotificationMessage,
          windowsNotificationEnable,
          windowsNotificationDisable,
        },
        fetchJsonOrThrow,
        getActiveMode: () => activeMode,
        isLocalConsole: () => Boolean(deviceLocalRequest),
        isTrustRequiredError,
        isEinkClient,
        onWindowsNotifications: card => activityFeedController.setWindowsNotifications(card),
      });
    } catch (error) {
      // 自訂卡片模組掛了也不能拖垮 iPhone 顯示器 / 配對主流程
      console.error("custom cards controller failed", error);
      customCardsController = null;
    }

    let dashboardLayoutController = null;
    try {
      const openDashboardConfig = action => {
        customCardsController?.setPage("custom", false);
        action?.();
      };
      const closeDashboardConfig = () => {
        customCardsController?.setSettingsPanelVisible(false);
        customCardsController?.setManagerVisible(false);
        customCardsController?.setPage("system", false);
      };
      dashboardLayoutController = createDashboardLayoutController({
        fetchJsonOrThrow,
        isEinkClient,
        openCardSettings: () => openDashboardConfig(() => customCardsController?.setSettingsPanelVisible(true)),
        openSourceManager: () => openDashboardConfig(() => customCardsController?.setManagerVisible(true)),
        openSourceForm: () => openDashboardConfig(() => customCardsController?.showForm()),
        closeConfig: closeDashboardConfig,
      });
    } catch (error) {
      console.error("dashboard layout controller failed", error);
    }

    const quotaController = createQuotaController({
      elements: { quotaSummary, quotaUpdated, quotaHelp, quotaGrid },
      getActiveMode: () => activeMode,
      fetchJsonOrThrow,
      isTrustRequiredError,
      renderSnapshot: renderQuotas,
      renderErrorHelp: (error, requiresTrust) => renderQuotaHelpBlock(
        "無法讀取額度",
        requiresTrust
          ? ["手機需先與 PC 完成配對，才能查看本機 AI 額度。"]
          : [error.message || "額度 API 失敗。", "請確認 Host 在跑，並在 PC 本機操作額度來源。"]
      ),
      onConnectionChange: state => setDashboardConnectionState(state),
    });
    const refreshQuotas = options => quotaController.refresh(options);
    const quotaActions = createQuotaActionController({
      document,
      window,
      t,
      tApi,
      tLegacy,
      applyFeedbackState,
      fetchJsonOrThrow,
      refreshQuotas,
      getQuotaSnapshot: () => quotaSnapshotData,
      quotaDataFingerprint,
      confirmAction,
      isLocalHost: () => isLoopbackHost(location.hostname),
      startOAuthPolling: () => quotaController.startOAuthPolling(),
    });
    const quotaSetupCards = createQuotaSetupCardRenderer({
      document,
      t,
      tLegacy,
      isEinkQuotaClient,
      applyQuotaCardStatus: quotaActions.applyCardStatus,
      wireQuotaToolbox: quotaActions.wireToolbox,
    });
    const codexAccountManager = createCodexAccountManager({
      document,
      t,
      tLegacy,
      fetchJsonOrThrow,
      confirmAction,
      runQuotaButton: quotaActions.runButton,
      refreshQuotas,
      reauthorizeCodexQuota: quotaActions.reauthorizeCodex,
      setQuotaCardStatus: quotaActions.setCardStatus,
      applyQuotaCardStatus: quotaActions.applyCardStatus,
    });
    const quotaCards = createQuotaCardRenderer({
      document,
      t,
      tLegacy,
      getIntlLocale,
      actions: quotaActions,
      codexAccountManager,
      changeAccount: changeQuotaAccount,
    });
    const diagnosticsController = createDiagnosticsController({
      document,
      navigator,
      elements: { panel: diagnosticsPanel, summary: diagnosticsSummary, list: diagnosticsList, markButton: markDiagnostics },
      fetchJsonOrThrow,
      tLegacy,
      getIntlLocale,
      isLocalRequest: () => deviceLocalRequest,
    });
    const deviceManagementView = createDeviceManagementView({
      document,
      elements: {
        trustedDevicesPanel,
        newDeviceConnectPanel,
        diagnosticsPanel,
        trustedDeviceList,
        clearTrustedDevices,
      },
      t,
      getIntlLocale,
      startPairing: startPhonePairing,
    });
    const deviceActions = createDeviceActionsController({
      document,
      elements: { pendingPairingPanel, pendingPairingList, trustedDeviceList },
      t,
      tLegacy,
      fetchJsonOrThrow,
      confirmAction,
      isLocalRequest: () => deviceLocalRequest,
      setPairingUiActive,
      setPairingProgress,
      updatePairingGuide,
      setTrustState,
      persistDeviceCredentials,
      reloadTrustStatus: loadDeviceTrustStatus,
    });
    const hostAuthController = createHostAuthController({
      elements: {
        gate: hostAuthGate,
        passwordInput: hostAuthPassword,
        submitButton: hostAuthSubmit,
        errorElement: hostAuthError,
      },
      fetch,
      parseJsonResponse,
      t,
      tLegacy,
    });
    const pairingSession = createPairingSessionController({
      document,
      navigator,
      localStorage,
      history,
      location,
      fetch,
      t,
      tLegacy,
      parseJsonResponse,
      elements: {
        phonePairIntro,
        phonePairRequest,
        successBanner: document.getElementById("pairSuccessBanner"),
      },
      isDeviceTrusted: () => deviceTrusted,
      isLocalRequest: () => deviceLocalRequest,
      isHostAuthenticated: hostAuthController.isAuthenticated,
      isBooxPreview: () => isDevicePreview("boox-go-color-7"),
      isEinkClient,
      isIos,
      isStandaloneApp,
      getClientInstanceId: getOrCreateClientInstanceId,
      setPairingProgress,
      setTrustState,
      updatePairingGuide,
      persistDeviceCredentials,
      reloadTrustStatus: loadDeviceTrustStatus,
    });
    const productUpdates = createProductUpdateController({
      button: productUpdate,
      status: productUpdateStatus,
      t,
      tLegacy,
      applyFeedbackState,
      fetchJsonOrThrow,
      confirmAction,
      isLocalConsole: () => deviceLocalRequest,
    });
    const displaySources = createDisplaySourceController({
      document,
      localStorage,
      t,
      elements: {
        displaySource,
        displaySourceKind,
        displayToolbar,
        displayToolbarToggle,
        remoteKeyboardButton,
        remoteKeyboardInput,
        displayEmptyState,
        displayEmptyTitle,
        displayEmptyMessage,
        installVirtualDisplay,
        openSideboardFromEmpty,
        modePreset,
        driverState,
      },
      isLocalRequest: () => deviceLocalRequest,
      setDisplayAspectRatio,
      getDisplayInputController: () => displayInputController,
    });
    const displayInstall = createDisplayInstallController({
      elements: {
        displayInstallDetail,
        setupDisplayInstallDetail,
        installVirtualDisplay,
        setupInstallVirtualDisplay,
      },
      t,
      tApi,
      tLegacy,
      applyFeedbackState,
      fetchJsonOrThrow,
      isLocalRequest: () => deviceLocalRequest,
      isPhoneClient: displaySources.isPhoneClient,
      syncEmptyActions: displaySources.syncEmptyActions,
      setAvailability: displaySources.setAvailability,
      reloadDisplays: loadPhoneDisplay,
      hasVibeDeckDisplay: displaySources.hasVibeDeckDisplay,
      connectVideo,
    });
    const turnSettingsController = createTurnSettingsController({
      elements: {
        turnKeyId,
        turnApiToken,
        turnSettingsStatus,
        turnDiagnosticsStatus,
        saveTurnSettings,
        testTurnSettings,
        clearTurnSettings,
      },
      t,
      tLegacy,
      fetchJsonOrThrow,
      confirmAction,
      isLocalRequest: () => deviceLocalRequest,
    });
    const keepAwakeController = createKeepAwakeController({
      document,
      window,
      navigator,
      localStorage,
      button: document.getElementById("keepAwake"),
      video: document.getElementById("keepAwakeVideo"),
      isIos,
      isMobileClient,
      setWakeState,
    });

    onLocaleChange(() => {
      // Existing DOM text is translated by the runtime. Refresh the data-backed
      // panels so their next render uses the new locale for formatters/statuses.
      updateEinkToggle();
      refreshSideboard().catch(() => {});
      refreshQuotas({ force: true }).catch(() => {});
      const customRefresh = customCardsController?.refreshAll?.();
      customRefresh?.catch?.(() => {});
      updatePairingGuide(deviceTrusted ? "paired" : "pair");
      updateEinkToggle();
      keepAwakeController.updateCapability();
      productUpdates.renderCurrent();
      displaySources.refreshLocalizedUi();
      if (displaySettingsToggle) {
        const open = document.body.classList.contains("display-settings-open");
        displaySettingsToggle.textContent = open ? t("ui.settingsCollapse") : t("ui.settings");
      }
      if (hostAuthSubmit && !hostAuthSubmit.disabled) {
        hostAuthSubmit.textContent = t("gates.hostAuthSubmit");
      }
    });

    function renderQuotas(snapshot) {
      quotaSnapshotData = snapshot || {};
      quotaMiniController?.renderSnapshot(snapshot);
      renderQuotaContent();
    }

    function renderQuotaContent() {
      quotaGrid.replaceChildren();
      if (quotaHelp) quotaHelp.replaceChildren();
      const snapshot = quotaSnapshotData || {};
      const viewState = buildQuotaViewState(snapshot, quotaActiveTab);
      const providers = viewState.providers;
      const tabs = viewState.tabs;
      quotaActiveTab = viewState.activeTab;
      renderQuotaTabs(tabs);

      const agyAccounts = viewState.agyAccounts;
      const codexProviders = viewState.codexProviders;
      const codexUsable = viewState.codexUsable;
      const tabProviders = viewState.tabProviders;

      quotaSummary.textContent = buildQuotaSummary(viewState, tLegacy);
      quotaUpdated.textContent = snapshot?.GeneratedAt || snapshot?.generatedAt
        ? new Date(snapshot.GeneratedAt || snapshot.generatedAt).toLocaleTimeString(getIntlLocale(), { hour: "2-digit", minute: "2-digit" })
        : "--";

      if (quotaHelp) {
        quotaHelp.append(renderQuotaHelpForTab(quotaActiveTab, {
          agyCount: agyAccounts.length,
          codexUsable,
          codexCount: codexProviders.length
        }));
      }

      if (quotaActiveTab === "agy") {
        if (agyAccounts.length) {
          const index = getQuotaAccountIndex("agy", agyAccounts);
          quotaGrid.append(quotaCards.renderAgyAccountPage(agyAccounts[index], agyAccounts, index, snapshot));
        } else {
          quotaGrid.append(quotaSetupCards.renderSetup("agy"));
        }
      } else if (tabProviders.length) {
        const index = getQuotaAccountIndex(quotaActiveTab, tabProviders);
        const provider = tabProviders[index];
        const pageInfo = { index, total: tabProviders.length, tabId: quotaActiveTab };
        if (quotaActiveTab === "codex" && isEinkQuotaClient()) {
          quotaGrid.append(quotaCards.renderEinkCodexOverview(provider, snapshot, pageInfo));
        } else {
          if (quotaActiveTab === "codex") quotaGrid.append(codexAccountManager.render());
          quotaGrid.append(quotaCards.renderSingleQuotaPage(provider, snapshot, pageInfo));
        }
      } else if (quotaActiveTab === "codex") {
        if (!isEinkQuotaClient()) quotaGrid.append(codexAccountManager.render());
        quotaGrid.append(quotaSetupCards.renderSetup("codex"));
      } else if (quotaActiveTab === "claude-code") {
        quotaGrid.append(quotaSetupCards.renderSetup("claude-code"));
      }

      if (!quotaGrid.children.length) {
        quotaGrid.append(quotaSetupCards.renderEmpty("等待額度來源", ["切換上方分頁查看各來源的導入說明。"]));
      }
    }

    function isEinkQuotaClient() {
      return document.body.classList.contains("eink-client");
    }

    function renderQuotaHelpBlock(title, steps) {
      const block = document.createElement("details");
      block.className = "quota-help-block";
      if (isEinkQuotaClient()) block.classList.add("quota-help-block-eink");
      // Collapsed by default — pipeline notes are dense and steal vertical space.
      block.open = false;
      const summary = document.createElement("summary");
      summary.className = "quota-help-summary";
      const label = document.createElement("span");
      label.className = "quota-help-summary-label";
      label.textContent = title;
      const hint = document.createElement("span");
      hint.className = "quota-help-summary-hint";
      hint.textContent = "說明";
      summary.append(label, hint);
      block.append(summary);
      const body = document.createElement("div");
      body.className = "quota-help-body";
      const list = document.createElement("ol");
      for (const step of steps) {
        const li = document.createElement("li");
        li.textContent = step;
        list.append(li);
      }
      body.append(list);
      block.append(body);
      return block;
    }

    function renderQuotaHelpForTab(tabId, state = {}) {
      const spec = buildQuotaHelpSpec(tabId, state, isEinkQuotaClient());
      return renderQuotaHelpBlock(spec.title, spec.steps);
    }

    function renderQuotaTabs(tabs) {
      quotaTabs.replaceChildren();
      for (const tab of tabs) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = `quota-tab${tab.id === quotaActiveTab ? " active" : ""}`;
        button.setAttribute("role", "tab");
        button.setAttribute("aria-selected", tab.id === quotaActiveTab ? "true" : "false");
        button.textContent = tab.label;
        button.addEventListener("click", () => {
          quotaActiveTab = tab.id;
          localStorage.setItem(QUOTA_TAB_STORAGE_KEY, quotaActiveTab);
          renderQuotaContent();
        });
        quotaTabs.append(button);
      }
    }

    function getQuotaAccountIndex(tabId, items) {
      return quotaAccountNavigator.getIndex(tabId, items);
    }

    function changeQuotaAccount(tabId, delta) {
      const providers = quotaSnapshotData?.Providers || quotaSnapshotData?.providers || [];
      const accounts = tabId === "agy"
        ? groupAgyAccounts(providers)
        : groupSingleProviderAccounts(providers, tabId);
      const total = accounts.length;
      if (total < 2) return;
      quotaAccountNavigator.move(tabId, accounts, delta);
      renderQuotaContent();
    }

    function setStatus(text, online, detail = "") {
      statusText.textContent = text;
      statusText.title = detail;
      if (displayStreamStateMessage && !online) displayStreamStateMessage.textContent = text;
      dot.classList.toggle("online", online);
      document.body.classList.toggle("stream-online", online);
    }

    function setDashboardConnectionState(state) {
      dashboardConnectionState = state === "online" ? "online" : "connecting";
      document.querySelectorAll("[data-eink-connection-state]").forEach(element => {
        const online = dashboardConnectionState === "online";
        element.textContent = online ? "連線中" : "正在連線";
        element.classList.toggle("online", online);
        element.classList.toggle("connecting", !online);
        element.classList.toggle("offline", !online);
      });
    }

    function setWakeState(text, good) {
      wakeState.textContent = text;
      wakeState.classList.toggle("good", good);
      wakeState.classList.toggle("warn", !good);
    }

    function setAppState(text, good) {
      appState.textContent = text;
      appState.classList.toggle("good", good);
      appState.classList.toggle("warn", !good);
    }

    function setStreamCapabilityState(text, good) {
      streamCapabilityState.textContent = text;
      streamCapabilityState.classList.toggle("good", good);
      streamCapabilityState.classList.toggle("warn", !good);
    }

    async function loadStreamCapabilities() {
      try {
        const capabilities = await fetchJsonOrThrow("/api/stream/capabilities");
        const h264Supported = Boolean(capabilities?.h264?.supported ?? capabilities?.H264?.Supported);
        const webrtcSupported = Boolean(capabilities?.webrtc?.supported ?? capabilities?.Webrtc?.Supported);
        const h264Metrics = capabilities?.h264?.metrics || capabilities?.H264?.Metrics || null;
        const metricsActive = Boolean(h264Metrics?.Active ?? h264Metrics?.active);
        const recentFps = Number(h264Metrics?.RecentQueuedFps ?? h264Metrics?.recentQueuedFps);
        const recentMbps = Number(h264Metrics?.RecentMbps ?? h264Metrics?.recentMbps);
        const skipped = Number(h264Metrics?.RecentSkippedFps ?? h264Metrics?.recentSkippedFps);
        const metricText = metricsActive && Number.isFinite(recentFps) && Number.isFinite(recentMbps)
          ? `Host H.264 ${recentFps.toFixed(0)}fps ${recentMbps.toFixed(1)}Mbps idle ${Number.isFinite(skipped) ? skipped.toFixed(0) : "0"}fps`
          : "";
        setStreamCapabilityState(
          webrtcSupported
            ? (metricText ? `串流：WebRTC H.264 · ${metricText}` : "串流：瀏覽器 WebRTC H.264 可用，JPEG fallback 已保留。")
            : h264Supported
              ? (metricText ? `串流：${metricText}` : "串流：JPEG 可用，瀏覽器 H.264 可用。")
            : "串流：JPEG 可用；H.264 Host 編碼器尚未接上。",
          webrtcSupported || h264Supported);
      } catch {
        setStreamCapabilityState("串流：無法讀取能力狀態。", false);
      }
    }

    function updateInstallState() {
      // No fake install button: iOS must use Safari Share → Add to Home Screen.
      // Android Chrome may still fire beforeinstallprompt; we only surface status text.
      if (isStandaloneApp()) {
        setAppState("App：已在主畫面。", true);
        return;
      }

      if (isIos()) {
        setAppState(
          location.protocol === "https:"
            ? "App：Safari 分享 → 加入主畫面（不要用網頁假按鈕）。"
            : "App：先改 HTTPS，再分享 → 加入主畫面。",
          location.protocol === "https:"
        );
        return;
      }

      if (installPromptEvent) {
        setAppState("App：瀏覽器可安裝（用瀏覽器選單）。", true);
        return;
      }

      if (!window.isSecureContext && location.hostname !== "localhost" && location.hostname !== "127.0.0.1") {
        setAppState("App：安裝提示需要 HTTPS。", false);
        return;
      }

      setAppState("App：可用瀏覽器選單加入主畫面。", false);
    }

    function resetStreamStats() {
      streamStats = {
        frames: 0,
        bytes: 0,
        lastFrames: 0,
        lastBytes: 0,
        lastTime: performance.now()
      };
    }

    function recordFrame(byteLength, fallbackReason = "") {
      if (!streamStats) return;
      streamStats.frames += 1;
      streamStats.bytes += byteLength || 0;
      const now = performance.now();
      const elapsed = now - streamStats.lastTime;
      if (elapsed < 1000) return;

      const frameDelta = streamStats.frames - streamStats.lastFrames;
      const byteDelta = streamStats.bytes - streamStats.lastBytes;
      const fps = frameDelta * 1000 / elapsed;
      const mbps = byteDelta * 8 / elapsed / 1000;
      const protocolLabel = fallbackReason ? `${fallbackReason} · ` : "";
      setStatus(`${protocolLabel}jpeg ${fps.toFixed(0)}fps ${mbps.toFixed(1)}Mbps`, true);
      streamStats.lastFrames = streamStats.frames;
      streamStats.lastBytes = streamStats.bytes;
      streamStats.lastTime = now;
    }

    function getActiveStreamElement() {
      return streamController?.getActiveStreamElement() || screen;
    }

    function getMediaWidth(element) {
      return element.videoWidth || element.naturalWidth || element.clientWidth || 0;
    }

    function getMediaHeight(element) {
      return element.videoHeight || element.naturalHeight || element.clientHeight || 0;
    }

    function setDisplayAspectRatio(value) {
      screen.style.aspectRatio = value;
      rtcScreen.style.aspectRatio = value;
    }

    function applyRotation() {
      document.body.classList.remove("rotate-90", "rotate-180", "rotate-270");
      const resolvedRotation = resolveRotation();
      if (resolvedRotation !== "0") {
        document.body.classList.add(`rotate-${resolvedRotation}`);
      }
      localStorage.setItem("vibeDeckRotation", rotation.value);
    }

    function applyOrientation() {
      const value = ["auto", "portrait", "landscape"].includes(orientation.value)
        ? orientation.value
        : "auto";
      orientation.value = value;
      localStorage.setItem("vibeDeckOrientation", value);
      applyForcedLandscape();
      updateViewportSize();
      if (window.screen?.orientation) {
        if (value !== "auto") {
          window.screen.orientation.lock?.(value).catch(() => {});
        } else {
          window.screen.orientation.unlock?.();
          if (document.fullscreenElement && isEinkClient()) {
            window.screen.orientation.lock?.("any").catch(() => {});
          }
        }
      }
    }

    function resolveRotation() {
      if (rotation.value !== "auto") return rotation.value;

      // Auto rotation: only rotate when in fullscreen viewer mode so the desktop
      // fills the screen landscape. In the non-fullscreen thumbnail preview, keep
      // it upright — portrait streams show portrait, landscape shows landscape.
      if (!document.body.classList.contains("viewer-fullscreen")) return "0";

      const viewportWidth = window.visualViewport ? window.visualViewport.width : window.innerWidth;
      const viewportHeight = window.visualViewport ? window.visualViewport.height : window.innerHeight;
      const viewportPortrait = viewportHeight > viewportWidth;
      const activeMedia = getActiveStreamElement();
      const streamPortrait = getMediaHeight(activeMedia) > getMediaWidth(activeMedia);

      return viewportPortrait !== streamPortrait ? "90" : "0";
    }

    async function enterLandscapeViewer() {
      updateViewportSize();
      const isDisplayMode = activeMode === "display";
      document.body.classList.toggle("viewer-fullscreen", isDisplayMode);
      document.body.classList.toggle("dashboard-viewer", !isDisplayMode);
      // E-ink / sideboard also need the immersive body class for 100dvh layout.
      if (!isDisplayMode) {
        document.body.classList.add("viewer-immersive");
      } else {
        document.body.classList.remove("viewer-immersive");
        suspendDashboardBackgroundWork();
      }
      const mainContent = document.querySelector("main");
      mainContent?.scrollTo({ top: 0, left: 0 });
      window.scrollTo(0, 1);
      setTimeout(() => {
        mainContent?.scrollTo({ top: 0, left: 0 });
        updateViewportSize();
        applyRotation();
      }, 250);

      // iOS: no Fullscreen API — CSS viewer-fullscreen is the whole path.
      // Android phone display: same CSS path. Real Fullscreen API + CSS
      // force-landscape (rotate 90°) fight each other and break landscape layout.
      // Keep the real Fullscreen API only for non-phone / non-display panels (e.g. BOOX sideboard).
      const useBrowserFullscreen = !isIos() && !(isDisplayMode && isMobileClient());
      if (useBrowserFullscreen) {
        const root = document.documentElement;
        const candidates = [
          () => root.requestFullscreen && root.requestFullscreen({ navigationUI: "hide" }),
          () => root.webkitRequestFullscreen && root.webkitRequestFullscreen(),
          () => root.webkitRequestFullScreen && root.webkitRequestFullScreen(),
          () => document.body.requestFullscreen && document.body.requestFullscreen({ navigationUI: "hide" })
        ];
        for (const tryEnter of candidates) {
          try {
            const result = tryEnter();
            if (result && typeof result.then === "function") await result;
            if (document.fullscreenElement || document.webkitFullscreenElement) break;
          } catch {
            // try next vendor path
          }
        }
      }

      if (isDisplayMode && orientation.value === "landscape" && window.screen && window.screen.orientation && window.screen.orientation.lock) {
        try {
          await window.screen.orientation.lock("landscape");
        } catch {
        }
      }
      if (!isDisplayMode && isEinkClient() && orientation.value === "auto" && window.screen?.orientation?.lock) {
        try {
          await window.screen.orientation.lock("any");
        } catch {
        }
      }
      await keepAwakeController.ensure();
      applyForcedLandscape();
      updateViewportSize();
      applyRotation();
      if (isDisplayMode) {
        connectVideo();
      }
    }

    function toggleDisplayViewerFromScreen() {
      if (activeMode !== "display") return;
      if (document.body.classList.contains("viewer-fullscreen")) {
        exitLandscapeViewer();
      } else {
        enterLandscapeViewer();
      }
    }

    async function exitLandscapeViewer() {
      document.body.classList.remove("viewer-fullscreen");
      document.body.classList.remove("dashboard-viewer");
      document.body.classList.remove("viewer-immersive");
      resumeDashboardBackgroundWork();
      try {
        if (document.exitFullscreen && (document.fullscreenElement || document.webkitFullscreenElement)) {
          await document.exitFullscreen();
        } else if (document.webkitExitFullscreen) {
          document.webkitExitFullscreen();
        }
      } catch {
      }
      applyRotation();
    }

    async function loadPhoneDisplay() {
      let displays;
      try {
        displays = await fetchJsonOrThrow("/api/displays");
      } catch (error) {
        displaySources.clear({ resetDisplays: true });
        if (isTrustRequiredError(error)) {
          setStatus("請先配對手機", false);
          driverState.textContent = "請先配對手機，才能觀看 Windows 畫面。";
          displaySources.setAvailability(false, "尚未完成配對", "完成配對後才能觀看與控制 Windows 畫面。資訊板可直接使用。");
          return;
        }

        setStatus("顯示器無法使用", false);
        driverState.textContent = error.message || "無法取得 VibeDeck 顯示器狀態。";
        displaySources.setAvailability(false, "暫時連不上顯示器", "請確認 Host 仍在執行，或先改用資訊板。");
        return;
      }

      displaySources.setDisplays(displays);
      const display = displaySources.chooseCurrent();
      if (!display) {
        displaySources.clear();
        setStatus("找不到可用顯示器", false);
        driverState.textContent = "Windows 目前沒有可擷取的顯示器。";
        displaySources.setAvailability(false, "找不到 Windows 顯示器", "請確認 Host 正在已登入的 Windows 桌面工作階段執行。");
        await displayInstall.load();
        return;
      }

      displaySources.applySelected(display, false);
      displaySources.renderOptions();
    }

    function inputSocketUrl() {
      const params = appendDeviceToken(new URLSearchParams());
      const query = params.toString();
      return `${wsBase}/ws/input${query ? `?${query}` : ""}`;
    }

    function applyStreamPresetFields() {
      const presets = {
        battery: { fps: 30, quality: 48 },
        balanced: { fps: 45, quality: 56 },
        smooth: { fps: 60, quality: 60 },
        sharp: { fps: 60, quality: 68 }
      };
      const preset = presets[streamPreset.value];
      if (preset) {
        streamFps.value = preset.fps;
        streamQuality.value = preset.quality;
      }
      localStorage.setItem("vibeDeckStreamPreset", streamPreset.value);
    }

    function applyStreamSettings() {
      localStorage.setItem("vibeDeckStreamFps", streamFps.value);
      localStorage.setItem("vibeDeckStreamQuality", streamQuality.value);
      localStorage.setItem("vibeDeckStreamTransport", streamTransport?.value || "auto");
      connectVideo();
    }

    async function loadDisplayStatus() {
      try {
        const status = await fetchJsonOrThrow("/api/display/status");
        if (displaySources.getSelectedDisplay()?.IsVibeDeckDisplay) {
          const state = status.State || status.state || "";
          const stateLabel = state === "driver-started"
            ? t("ui.virtualDisplayDriverReady")
            : state === "driver-installed"
              ? t("ui.virtualDisplayDriverInstalled")
              : state === "driver-not-installed"
                ? t("ui.virtualDisplayDriverMissing")
                : t("ui.virtualDisplayDriverUnknown");
          driverState.classList.toggle("good", state === "driver-started");
          driverState.classList.toggle("warn", state !== "driver-started");
          driverState.textContent = `${driverState.textContent} · ${stateLabel}`;
        }
      } catch (error) {
        if (displaySources.getSelectedDisplay()?.IsVibeDeckDisplay && !isTrustRequiredError(error)) {
          driverState.classList.remove("good");
          driverState.classList.add("warn");
          driverState.textContent = `${driverState.textContent} · ${t("ui.virtualDisplayDriverUnknown")}`;
        }
      }
    }

    async function loadConnectInfo() {
      const response = await fetch("/api/connect", { cache: "no-store" });
      const info = parseJsonResponse(await response.text(), "/api/connect");
      if (!response.ok) {
        throw new Error(info.error || info.message || `連線資訊讀取失敗 HTTP ${response.status}`);
      }
      const httpsAvailable = Boolean(info.HttpsAvailable ?? info.httpsAvailable);
      const rootCertificateUrl = info.RootCertificateUrl || info.rootCertificateUrl || "";
      const httpsSetupHint = info.HttpsSetupHint || info.httpsSetupHint || "";
      const httpsUrl = info.HttpsUrl || info.httpsUrl || "";
      const httpUrl = info.HttpUrl || info.httpUrl || "";
      const publicUrl = info.PublicUrl || info.publicUrl || "";
      updatePublicEndpointUi(info);
      if (!pairingQrActive) {
        showInstallQr();
      }
      // HTTPS is the canonical phone URL; HTTP remains a local bootstrap fallback.
      prettyLink.href = httpsAvailable ? httpsUrl : (info.LocalNameHttpUrl || httpUrl);
      prettyLink.textContent = httpsAvailable
        ? usesTrustedPublicUrl
          ? t("secureEndpoint.phoneUrl", { url: publicUrl || httpsUrl })
          : `手機請用：${httpsUrl}`
        : `本機：${info.LocalNameHttpUrl || httpUrl}`;
      httpsLink.href = httpsAvailable ? httpsUrl : "#";
      httpsLink.textContent = httpsAvailable
        ? usesTrustedPublicUrl
          ? t("secureEndpoint.linkUrl", { url: publicUrl || httpsUrl })
          : `HTTPS（推薦）：${httpsUrl}`
        : `HTTPS 未就緒：${httpsSetupHint || "PC 執行 scripts\\setup-https.ps1"}`;
      httpsCertLink.hidden = !httpsAvailable || !rootCertificateUrl;
      httpsCertLink.href = rootCertificateUrl || "#";
      httpsCertLink.textContent = "安裝 HTTPS 憑證";
      const androidCertificateUrl = rootCertificateUrl.replace(/\.cer(?=$|\?)/i, ".crt");
      androidCertLink.hidden = !httpsAvailable || !rootCertificateUrl;
      androidCertLink.href = androidCertificateUrl || "#";
      androidCertHelp.hidden = !httpsAvailable || !rootCertificateUrl;
      httpLink.href = httpUrl || "#";
      httpLink.hidden = usesTrustedPublicUrl;
      httpLink.textContent = `HTTP（本機備援）：${httpUrl}`;
      const connectTitleHint = document.getElementById("connectTitleHint");
      if (connectTitleHint) {
        connectTitleHint.textContent = usesTrustedPublicUrl
          ? t("secureEndpoint.connectHint")
          : tLegacy("不必安裝憑證。第一次開啟時，在瀏覽器警告頁按「進階」→「繼續前往」。");
      }
      const gateHttps = document.getElementById("mobileGateHttpsUrl");
      if (gateHttps && httpsUrl) {
        gateHttps.href = httpsUrl;
        gateHttps.textContent = httpsUrl;
      }
      const openHttps = document.getElementById("mobileGateOpenHttps");
      if (openHttps) {
        const target = buildHttpsUrlFromCurrent() || (httpsUrl ? new URL("index.html" + (location.search || ""), httpsUrl).toString() : "");
        openHttps.onclick = () => {
          if (target) location.replace(target);
        };
      }
      keepAwakeController.updateCapability();
    }

    async function loadModePresets() {
      const response = await fetch("/api/display/modes");
      const presets = parseJsonResponse(await response.text(), "/api/display/modes");
      if (!response.ok || !Array.isArray(presets)) {
        throw new Error(presets.error || presets.message || `解析度預設讀取失敗 HTTP ${response.status}`);
      }
      displayModePresets = presets;
      modePreset.textContent = "";
      const autoOption = document.createElement("option");
      autoOption.value = AUTO_DISPLAY_MODE;
      autoOption.textContent = tLegacy("自動（依裝置）");
      modePreset.append(autoOption);
      for (const preset of presets) {
        const option = document.createElement("option");
        option.value = JSON.stringify(preset);
        option.textContent = preset.Label;
        modePreset.append(option);
      }
      const storedMode = localStorage.getItem(DISPLAY_MODE_STORAGE_KEY) || AUTO_DISPLAY_MODE;
      modePreset.value = Array.from(modePreset.options).some(option => option.value === storedMode)
        ? storedMode
        : AUTO_DISPLAY_MODE;
      applyPresetFields();
    }

    function applyPresetFields() {
      if (!modePreset.value) return;
      localStorage.setItem(DISPLAY_MODE_STORAGE_KEY, modePreset.value);
      if (modePreset.value === AUTO_DISPLAY_MODE) {
        const choice = chooseAutoDisplayMode(displayModePresets, readClientDisplayMetrics());
        if (!choice) return;
        modeWidth.value = choice.preset.Width;
        modeHeight.value = choice.preset.Height;
        modeRefresh.value = choice.preset.RefreshRate;
        return;
      }
      const preset = JSON.parse(modePreset.value);
      modeWidth.value = preset.Width;
      modeHeight.value = preset.Height;
      modeRefresh.value = preset.RefreshRate;
    }

    async function applyAutoDisplayMode() {
      if (isEinkClient() || activeMode !== "display" || modePreset.value !== AUTO_DISPLAY_MODE) return false;
      if (deviceLocalRequest || document.body.classList.contains("pc-console")) return false;
      if (!canUseProtectedConnection() || !displayModePresets.length) return false;
      const display = displaySources.getSelectedDisplay();
      if (!display?.IsVibeDeckDisplay) return false;

      const choice = chooseAutoDisplayMode(displayModePresets, readClientDisplayMetrics());
      if (!choice) return false;
      const preset = choice.preset;
      modeWidth.value = preset.Width;
      modeHeight.value = preset.Height;
      modeRefresh.value = preset.RefreshRate;

      const signature = `${preset.Width}x${preset.Height}@${preset.RefreshRate}`;
      if (display.Width === Number(preset.Width) && display.Height === Number(preset.Height)) {
        lastAutoDisplayModeSignature = signature;
        return false;
      }
      if (lastAutoDisplayModeSignature === signature) return false;

      lastAutoDisplayModeSignature = signature;
      try {
        await applyDisplayMode();
        return true;
      } catch (error) {
        lastAutoDisplayModeSignature = "";
        driverState.classList.add("warn");
        driverState.textContent = error.message || tLegacy("自動解析度套用失敗");
        return false;
      }
    }

    function scheduleAutoDisplayMode(delay = 300) {
      if (autoDisplayModeTimer) clearTimeout(autoDisplayModeTimer);
      autoDisplayModeTimer = setTimeout(() => {
        autoDisplayModeTimer = null;
        applyPresetFields();
        applyAutoDisplayMode().catch(() => {});
      }, delay);
    }

    async function applyDisplayMode() {
      const result = await fetchJsonOrThrow("/api/display/mode", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          Width: Number(modeWidth.value),
          Height: Number(modeHeight.value),
          RefreshRate: Number(modeRefresh.value)
        })
      });
      driverState.textContent = result.Message;
      displaySources.resetSelectionName();
      setTimeout(async () => {
        await loadPhoneDisplay();
        connectVideo();
      }, 1200);
    }

    function connectInput() {
      if (!canUseProtectedConnection()) {
        return;
      }

      inputSocket = new WebSocket(inputSocketUrl());
      inputSocket.onclose = () => setTimeout(connectInput, 1000);
    }

    function connectVideo() {
      return streamController?.connect();
    }

    function closeJpegStream() {
      return streamController?.closeJpegStream();
    }

    function closeRtcStream(invalidate = true) {
      return streamController?.closeRtcStream(invalidate);
    }

    try {
      streamController = createStreamController({
        elements: { screen, rtcScreen },
        getWsBase: () => wsBase,
        appendDeviceToken,
        getSelectedDisplayName: displaySources.getSelectedName,
        getStreamSettings: () => ({
          fps: Number(streamFps.value),
          quality: Number(streamQuality.value),
          transportMode: streamTransport?.value || "auto",
          rotationIsAuto: rotation.value === "auto",
        }),
        canUseProtectedConnection,
        loadPhoneDisplay,
        prefersWebRtcDisplay,
        isLoopbackHost,
        setStatus,
        applyRotation,
        resetJpegStats: resetStreamStats,
        recordJpegFrame: recordFrame,
        fetchJsonOrThrow,
        tuneVideoReceiver,
        reportDiagnostic: report => {
          fetchJsonOrThrow("/api/stream/diagnostics", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(report)
          }).catch(() => {});
        },
      });
    } catch (error) {
      console.error("stream controller failed", error);
      streamController = null;
    }

    try {
      displayInputController = createDisplayInputController({
        targets: [screen, rtcScreen],
        getInputSocket: () => inputSocket,
        getDeviceName: displaySources.getSelectedName,
        getActiveStreamElement,
        getMediaWidth,
        getMediaHeight,
        resolveRotation,
        isMobileClient,
        enterLandscapeViewer,
        keyboardButton: remoteKeyboardButton,
        keyboardInput: remoteKeyboardInput,
        translate: key => t(key),
        touchLongPressMs,
        touchDragThresholdPx,
      });
      displayInputController.wireAll();
    } catch (error) {
      console.error("display input controller failed", error);
      displayInputController = null;
    }
    quotaGrid.addEventListener("pointerdown", event => {
      quotaSwipeStartX = event.clientX;
    });
    quotaGrid.addEventListener("pointerup", event => {
      if (quotaSwipeStartX == null) return;
      const delta = event.clientX - quotaSwipeStartX;
      quotaSwipeStartX = null;
      if (Math.abs(delta) > 48) {
        changeQuotaAccount(quotaActiveTab, delta < 0 ? 1 : -1);
      }
    });
    quotaGrid.addEventListener("pointercancel", () => {
      quotaSwipeStartX = null;
    });
    rotation.addEventListener("change", applyRotation);
    orientation.addEventListener("change", applyOrientation);
    streamPreset.addEventListener("change", () => {
      applyStreamPresetFields();
      applyStreamSettings();
    });
    applyStream.addEventListener("click", applyStreamSettings);
    streamTransport?.addEventListener("change", applyStreamSettings);
    saveTurnSettings?.addEventListener("click", turnSettingsController.save);
    testTurnSettings?.addEventListener("click", turnSettingsController.test);
    clearTurnSettings?.addEventListener("click", turnSettingsController.clear);
    modePreset.addEventListener("change", () => {
      lastAutoDisplayModeSignature = "";
      applyPresetFields();
      if (modePreset.value === AUTO_DISPLAY_MODE) scheduleAutoDisplayMode(80);
    });
    applyMode.addEventListener("click", applyDisplayMode);
    displaySource?.addEventListener("change", () => {
      const display = displaySources.findByName(displaySource.value);
      if (!display || display.DeviceName === displaySources.getSelectedName()) return;
      displayInputController?.dismissKeyboard?.();
      remoteKeyboardInput?.blur();
      displaySources.applySelected(display);
      // Keep expanded after switching source so the user sees the new selection.
      displaySources.setToolbarExpanded(true);
      connectVideo();
    });
    displayToolbarToggle?.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      displaySources.toggleToolbarExpanded();
    });
    savePublicEndpoint?.addEventListener("click", () => {
      saveTrustedPublicEndpoint();
    });
    clearPublicEndpoint?.addEventListener("click", () => {
      clearTrustedPublicEndpoint();
    });
    generateDeviceConnectionCode?.addEventListener("click", () => {
      createDeviceConnectionCode();
    });
    openNewDevicePanel?.addEventListener("click", () => {
      if (newDeviceConnectPanel) {
        newDeviceConnectPanel.hidden = false;
        newDeviceConnectPanel.open = true;
      }
      setPairControlsVisible(true);
      startPhonePairing();
    });
    // Expanding "連接新裝置" itself must prepare the QR — users should not have
    // to discover the separate ＋ 新增 control just to unhide .connect-qr.
    newDeviceConnectPanel?.addEventListener("toggle", () => {
      if (!newDeviceConnectPanel.open) return;
      if (!deviceLocalRequest && !isLoopbackHost()) return;
      setPairControlsVisible(true);
      startPhonePairing();
    });
    // 流程圖第 2 步也可點，避免按鈕被藏時完全沒入口
    document.getElementById("pairingStepPair")?.addEventListener("click", () => {
      if (!isLoopbackHost() && !deviceLocalRequest) return;
      setPairControlsVisible(true);
      startPhonePairing();
    });
    phonePairRequest?.addEventListener("click", async () => {
      phonePairRequest.disabled = true;
      phonePairRequest.textContent = tLegacy("連接中…");
      try {
        if (isDevicePreview()) {
          devicePreviewTrustState = "pending";
          await loadDeviceTrustStatus();
          return;
        }
        await pairingSession.request({ allowCreate: true });
      } catch (error) {
        setTrustState(error.message || t("pairingUx.pairRequestFailed"), false);
      } finally {
        if (!pairingSession.hasActivePoll() && !isDevicePreviewTrust("pending")) {
          phonePairRequest.disabled = false;
          phonePairRequest.textContent = t("pairingUx.phoneTitle");
        }
      }
    });
    displaySettingsToggle?.addEventListener("click", () => {
      const open = !document.body.classList.contains("display-settings-open");
      document.body.classList.toggle("display-settings-open", open);
      displaySettingsToggle.setAttribute("aria-expanded", open ? "true" : "false");
      displaySettingsToggle.textContent = open ? t("ui.settingsCollapse") : t("ui.settings");
    });
    installVirtualDisplay?.addEventListener("click", () => {
      // Device setup / driver install is a local PC console task only.
      if (!deviceLocalRequest || displaySources.isPhoneClient()) return;
      setMode("setup");
    });
    setupInstallVirtualDisplay?.addEventListener("click", displayInstall.install);
    openSideboardFromEmpty?.addEventListener("click", () => setMode("sideboard"));
    hostAuthForm?.addEventListener("submit", event => {
      event.preventDefault();
      hostAuthController.login(hostAuthPassword?.value || "").then(success => {
        if (success) location.reload();
      });
    });
    launchDeckWindow.addEventListener("click", () => {
      launchDeckOnVirtualDisplay();
    });
    refreshTrustedDevices.addEventListener("click", () => {
      loadDeviceTrustStatus();
    });
    diagnosticsToggle?.addEventListener("click", () => {
      if (!diagnosticsPanel || !diagnosticsPanelContent) return;
      const isOpen = diagnosticsPanel.classList.toggle("is-open");
      diagnosticsToggle.setAttribute("aria-expanded", String(isOpen));
      diagnosticsPanelContent.hidden = !isOpen;
      if (isOpen) diagnosticsController.load();
    });
    refreshDiagnostics?.addEventListener("click", () => {
      diagnosticsController.load();
    });
    markDiagnostics?.addEventListener("click", () => {
      diagnosticsController.mark();
    });
    copyDiagnostics?.addEventListener("click", () => {
      diagnosticsController.copy();
    });
    clearTrustedDevices.addEventListener("click", () => {
      deviceActions.clear(clearTrustedDevices);
    });
    trustedDeviceList.addEventListener("click", event => {
      const button = event.target.closest("[data-device-revoke]");
      if (!button) return;
      deviceActions.revoke(button.dataset.deviceRevoke, button.dataset.deviceName, button);
    });
    pendingPairingList.addEventListener("click", event => {
      const approve = event.target.closest("[data-pair-approve]");
      const deny = event.target.closest("[data-pair-deny]");
      const button = approve || deny;
      if (!button) return;
      button.disabled = true;
      deviceActions.actOnPending(button.dataset.requestId, Boolean(approve)).catch(error => setTrustState(error.message || t("pairingUx.pairActionFailed"), false));
    });
    refresh.addEventListener("click", async () => {
      const originalText = refresh.textContent;
      refresh.disabled = true;
      refresh.setAttribute("aria-busy", "true");
      refresh.textContent = tLegacy("重新連線中");
      try {
        const connectionRecovery = (async () => {
          displaySources.resetSelectionName();
          pairingQrActive = false;
          await loadConnectInfo();
          await loadStreamCapabilities();
          await loadDeviceTrustStatus();
          await customDeckController?.refresh({ silent: true });
          await loadPhoneDisplay();
          connectVideo();
        })();
        await Promise.allSettled([
          connectionRecovery,
          refreshSideboard(),
          customCardsController?.refreshAll?.(),
          refreshQuotas({ force: true, background: true }),
        ]);
      } finally {
        refresh.disabled = false;
        refresh.removeAttribute("aria-busy");
        refresh.textContent = originalText;
      }
    });
    displayStreamRetry?.addEventListener("click", () => refresh.click());
    displayMode.addEventListener("click", () => setMode("display"));
    setupMode?.addEventListener("click", () => setMode("setup"));
    sideboardMode.addEventListener("click", () => setMode("sideboard"));
    const einkModeToggle = document.getElementById("einkModeToggle");
    if (einkModeToggle) {
      einkModeToggle.addEventListener("click", () => {
        setEinkClient(!isEinkClient());
      });
    }
    quotaMode.addEventListener("click", () => setMode("quota"));
    document.querySelectorAll("[data-dashboard-mode]").forEach(button => {
      button.addEventListener("click", () => setMode(button.dataset.dashboardMode));
    });
    for (const button of document.querySelectorAll("[data-side-skin]")) {
      button.addEventListener("click", () => setSideSkin(button.dataset.sideSkin));
    }
    const keepAwakeButton = document.getElementById("keepAwake");
    if (keepAwakeButton) {
      keepAwakeButton.addEventListener("click", keepAwakeController.toggle);
    }
    fullscreen.addEventListener("click", enterLandscapeViewer);
    exitViewer.addEventListener("click", exitLandscapeViewer);
    window.VibeDeckExitViewer = exitLandscapeViewer;
    // Legacy alias for any old injected callers.
    window.VibeDeckExitViewer = exitLandscapeViewer;
    function onFullscreenChromeChange() {
      const inFs = Boolean(document.fullscreenElement || document.webkitFullscreenElement);
      if (!inFs && !isIos()) {
        // Keep CSS immersive for e-ink panel until user taps exit; only drop display
        // stream chrome when the system fullscreen shell closes.
        if (activeMode === "display") {
          document.body.classList.remove("viewer-fullscreen");
          resumeDashboardBackgroundWork();
        }
      }
      updateViewportSize();
      applyRotation();
    }
    document.addEventListener("fullscreenchange", onFullscreenChromeChange);
    document.addEventListener("webkitfullscreenchange", onFullscreenChromeChange);
    document.addEventListener("visibilitychange", async () => {
      if (document.visibilityState === "visible") {
        scheduleDashboardRefresh("sideboard", true);
        scheduleDashboardRefresh("quota", true);
        scheduleDashboardRefresh("customCards", true);
        await keepAwakeController.handleVisibilityChange();
        if (activeMode === "display" && canUseProtectedConnection()) {
          connectVideo();
        }
        return;
      }

      // Background: drop wake helpers + JPEG to save battery/heat.
      await keepAwakeController.handleVisibilityChange();
      if (isMobileClient()) {
        closeJpegStream();
        closeRtcStream();
      }
    });

    // Any user gesture can (re)arm iOS keep-awake.
    document.addEventListener("pointerdown", () => {
      keepAwakeController.handlePointerDown();
    }, { passive: true });

    // Single taps drive PC mouse; double-tap toggles iPhone fullscreen viewer.
    screen.addEventListener("dblclick", event => {
      if (!isIos() && !isMobileClient()) return;
      event.preventDefault();
      if (displayInputController?.isTouchGestureActive?.()) return;
      toggleDisplayViewerFromScreen();
    });
    rtcScreen.addEventListener("dblclick", event => {
      if (!isIos() && !isMobileClient()) return;
      event.preventDefault();
      if (displayInputController?.isTouchGestureActive?.()) return;
      toggleDisplayViewerFromScreen();
    });
    window.addEventListener("beforeinstallprompt", event => {
      event.preventDefault();
      installPromptEvent = event;
      updateInstallState();
    });
    window.addEventListener("appinstalled", () => {
      installPromptEvent = null;
      updateInstallState();
    });
    document.addEventListener("keydown", event => {
      if (activeMode !== "quota") return;
      if (event.key === "ArrowLeft") {
        changeQuotaAccount(quotaActiveTab, -1);
      } else if (event.key === "ArrowRight") {
        changeQuotaAccount(quotaActiveTab, 1);
      }
    });
    window.addEventListener("resize", () => {
      updateViewportSize();
      applyRotation();
      scheduleAutoDisplayMode(350);
    });
    window.addEventListener("orientationchange", () => {
      // Soft keyboard often sticks across rotation; force dismiss so layout can settle.
      displayInputController?.dismissKeyboard?.();
      remoteKeyboardInput?.blur();
      setTimeout(() => {
        updateViewportSize();
        applyRotation();
        lastAutoDisplayModeSignature = "";
        scheduleAutoDisplayMode(120);
      }, 120);
    });
    window.addEventListener("offline", () => setDashboardConnectionState("connecting"));
    window.addEventListener("online", () => {
      setDashboardConnectionState("connecting");
      scheduleDashboardRefresh(activeMode === "quota" ? "quota" : "sideboard", true);
    });
    productUpdate?.addEventListener("click", () => {
      productUpdates.checkOrInstall().catch(error => productUpdates.renderFailure(error));
    });
    window.addEventListener("error", event => {
      const message = event.error?.message || event.message || "";
      if (!message) return;
      reportBrowserFault("runtime", message, event.filename || "", event.lineno || 0, event.colno || 0);
    });
    window.addEventListener("unhandledrejection", event => {
      const message = event.reason?.message || String(event.reason || "");
      if (!message) return;
      reportBrowserFault("unhandled-rejection", message);
    });
    window.addEventListener("devicemotion", handleEinkDeviceMotion, { passive: true });
    if (window.visualViewport) {
      window.visualViewport.addEventListener("resize", () => {
        updateViewportSize();
        applyRotation();
      });
    }
    async function boot() {
      const activeLocale = await initLocale().catch(() => "zh-Hant");
      const pairingLocaleSelect = document.getElementById("pairingLocaleSelect");
      if (pairingLocaleSelect) {
        pairingLocaleSelect.value = activeLocale;
        pairingLocaleSelect.addEventListener("change", () => {
          const url = new URL(location.href);
          url.searchParams.set("lang", pairingLocaleSelect.value || "zh-Hant");
          location.assign(url.toString());
        });
      }
      await (window.vibeDeckServiceWorkerCleanup || Promise.resolve());
      ensureEinkPreferenceSticky();
      // If hardware/cookie says e-ink but URL has no flag, stamp ?eink=1 so a later
      // "Add to Home Screen" is more likely to capture paper mode.
      if (isEinkClient() && readEinkQuery() === null) {
        try {
          const url = new URL(location.href);
          url.searchParams.set("eink", "1");
          history.replaceState({}, "", url.pathname + url.search + url.hash);
        } catch {
          // ignore
        }
      }
      const deckWindow = isDeckWindow();
      document.body.classList.toggle("deck-window", deckWindow);

      // iPhone: one path only — HTTPS. Block HTTP UI entirely (except loopback).
      if (!deckWindow && enforceMobileHttpsPath()) {
        try {
          await loadConnectInfo();
        } catch {
        }
        return;
      }

      try {
        await hostAuthController.loadStatus();
      } catch {
        // Auth status being temporarily unavailable must not prevent the client
        // chrome/device-trust path from booting. The controller defaults to an
        // open gate until the server reports that authentication is required.
      }
      if (hostAuthController.isRequired() && !hostAuthController.isAuthenticated()) {
        setStatus(t("pairingUx.waitingRemoteLogin"), false);
        return;
      }

      applyClientChrome();
      rotation.value = isDevicePreview() ? "auto" : localStorage.getItem("vibeDeckRotation") || "auto";
      orientation.value = isDevicePreview() ? "auto" : localStorage.getItem("vibeDeckOrientation") || "auto";
      if (isEinkClient()) {
        fullscreen.textContent = tLegacy("全螢幕面板");
      }
      streamPreset.value = localStorage.getItem("vibeDeckStreamPreset") || defaultStreamPreset();
      applyStreamPresetFields();
      streamFps.value = localStorage.getItem("vibeDeckStreamFps") || streamFps.value;
      streamQuality.value = localStorage.getItem("vibeDeckStreamQuality") || streamQuality.value;
      if (streamTransport) streamTransport.value = localStorage.getItem("vibeDeckStreamTransport") || "auto";
      updateViewportSize();
      applyRotation();
      applyOrientation();
      setSideSkin(localStorage.getItem("vibeDeckSideSkin") || "command");
      if (deckWindow) {
        document.title = "VibeDeck Deck";
        setMode(getInitialMode() === "quota" ? "quota" : "sideboard");
        connectDashboardEvents();
        sideboardTimer = setInterval(() => scheduleDashboardRefresh("sideboard"), 60000);
        quotaTimer = setInterval(() => scheduleDashboardRefresh("quota"), 120000);
        customCardsTimer = setInterval(() => scheduleDashboardRefresh("customCards"), 5000);
        activityNotificationsTimer = setInterval(refreshActivityNotifications, 5000);
        return;
      }

      describeClient();
      updateInstallState();
      // Re-hydrate token from cookie/session if localStorage was empty (iOS Home Screen cases).
      const again = loadStoredDeviceCredentials();
      if (!deviceToken && again.token) {
        persistDeviceCredentials(again.token, again.id);
      }
      applyClientChrome();
      ensureActionToken().catch(() => {});
      await loadDeviceTrustStatus();
      applyClientChrome();
      if (deviceLocalRequest || deviceTrusted) {
        await customDeckController?.refresh({ silent: true }).catch(() => {});
      }
      setMode(getInitialMode());
      customDeckController?.startPolling();
      if (shouldStartInViewer() || (isIos() && deviceTrusted && getInitialMode() === "display" && isStandaloneApp())) {
        setTimeout(() => enterLandscapeViewer(), 350);
      }
      connectDashboardEvents();
      sideboardTimer = setInterval(() => scheduleDashboardRefresh("sideboard"), 60000);
      quotaTimer = setInterval(() => scheduleDashboardRefresh("quota"), 120000);
      customCardsTimer = setInterval(() => scheduleDashboardRefresh("customCards"), 5000);
      activityNotificationsTimer = setInterval(refreshActivityNotifications, 5000);
      dashboardConnectionTimer = setInterval(() => {
        scheduleDashboardRefresh(activeMode === "quota" ? "quota" : "sideboard", true);
      }, 15000);
      if (isEinkClient()) {
        // Electronic readers are dashboards, not remote displays. Do not run
        // display discovery or let its errors overwrite the connection state.
        displaySources.setAvailability(true);
        driverState.textContent = "";
        setStatus("正在連線", false);
        setDashboardConnectionState("connecting");
      } else {
        await loadModePresets().catch(error => {
          setStatus(error.message || "解析度預設讀取失敗", false);
        });
      }
      loadConnectInfo().catch(error => {
        setTrustState(error.message || "連線資訊讀取失敗。", false);
        setStatus(error.message || "連線資訊讀取失敗", false);
      });
      if (deviceLocalRequest) {
        turnSettingsController.load().catch(turnSettingsController.showLoadError);
      }
      if (deviceLocalRequest && !connectInfoTimer) {
        connectInfoTimer = setInterval(() => loadConnectInfo().catch(() => {}), 5000);
      }
      if (!isEinkClient()) {
        loadStreamCapabilities();
        loadPhoneDisplay().then(async () => {
          await applyAutoDisplayMode();
          await loadDisplayStatus();
        }).finally(() => {
          if (activeMode === "display" && canUseProtectedConnection()) connectVideo();
        });
        connectInput();
      }
      if (isIos()) {
        fullscreen.textContent = tLegacy("全螢幕");
      }
      keepAwakeController.updateCapability();
      keepAwakeController.startWatch();
      // BOOX browsers may be served over LAN HTTP and still need the silent
      // video fallback.
      if (keepAwakeController.isDesired() && (isEinkClient() || (location.protocol === "https:" && (isIos() || isMobileClient() || deviceTrusted)))) {
        keepAwakeController.ensure();
        setTimeout(() => keepAwakeController.ensure(), 800);
      }
    }

    boot();
