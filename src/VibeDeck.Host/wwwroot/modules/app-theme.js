const STORAGE_KEY = "vibeDeckAppThemeV1";
const BACKGROUND_IMAGE_KEY = "vibeDeckAppBackgroundV1";
const LEGACY_THEME_KEY = "vibeDeckSideThemeV1";
const LEGACY_SKIN_KEY = "vibeDeckSideSkin";

export const DEFAULT_APP_THEME = Object.freeze({
  backgroundMode: "gradient",
  colorA: "#4b1f66",
  colorB: "#17344d",
  angle: 132,
  intensity: 72,
  backgroundFit: "cover",
  backgroundPosition: "center",
  backgroundBlur: 0,
  backgroundDim: 24,
  glassOpacity: 7,
  glassBlur: 20,
  glassBorder: 12,
  backgroundImage: "",
});

export const APP_PALETTE_PRESETS = Object.freeze({
  nebula: { colorA: "#4b1f66", colorB: "#17344d", angle: 132, intensity: 72 },
  aurora: { colorA: "#155d4a", colorB: "#173d57", angle: 142, intensity: 70 },
  ember: { colorA: "#6b3327", colorB: "#3d244f", angle: 128, intensity: 68 },
  midnight: { colorA: "#25265c", colorB: "#102f45", angle: 118, intensity: 62 },
});

const LEGACY_SKIN_PALETTES = Object.freeze({
  command: APP_PALETTE_PRESETS.nebula,
  dial: APP_PALETTE_PRESETS.midnight,
  focus: APP_PALETTE_PRESETS.aurora,
  hajimi: APP_PALETTE_PRESETS.nebula,
});

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function normalizeHex(value, fallback) {
  const candidate = String(value || "").trim();
  if (/^#[0-9a-f]{6}$/i.test(candidate)) return candidate.toLowerCase();
  if (/^#[0-9a-f]{3}$/i.test(candidate)) {
    const [r, g, b] = candidate.slice(1).split("");
    return `#${r}${r}${g}${g}${b}${b}`.toLowerCase();
  }
  return fallback;
}

function hexToRgb(hex) {
  const normalized = normalizeHex(hex, "#000000").slice(1);
  return {
    r: Number.parseInt(normalized.slice(0, 2), 16),
    g: Number.parseInt(normalized.slice(2, 4), 16),
    b: Number.parseInt(normalized.slice(4, 6), 16),
  };
}

function mixRgb(rgb, target, amount) {
  const mix = clamp(amount, 0, 1);
  return {
    r: Math.round(rgb.r + (target.r - rgb.r) * mix),
    g: Math.round(rgb.g + (target.g - rgb.g) * mix),
    b: Math.round(rgb.b + (target.b - rgb.b) * mix),
  };
}

function rgbToHex(rgb) {
  const channel = value => clamp(Math.round(value), 0, 255).toString(16).padStart(2, "0");
  return `#${channel(rgb.r)}${channel(rgb.g)}${channel(rgb.b)}`;
}

function rgba(rgb, alpha) {
  return `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${clamp(alpha, 0, 1).toFixed(3)})`;
}

function normalizeChoice(value, choices, fallback) {
  return choices.includes(value) ? value : fallback;
}

export function normalizeAppTheme(value = {}) {
  return {
    backgroundMode: normalizeChoice(value.backgroundMode, ["solid", "gradient", "image"], DEFAULT_APP_THEME.backgroundMode),
    colorA: normalizeHex(value.colorA, DEFAULT_APP_THEME.colorA),
    colorB: normalizeHex(value.colorB, DEFAULT_APP_THEME.colorB),
    angle: clamp(Number.isFinite(Number(value.angle)) ? Number(value.angle) : DEFAULT_APP_THEME.angle, 0, 360),
    intensity: clamp(Number.isFinite(Number(value.intensity)) ? Number(value.intensity) : DEFAULT_APP_THEME.intensity, 30, 100),
    backgroundFit: normalizeChoice(value.backgroundFit, ["cover", "contain"], DEFAULT_APP_THEME.backgroundFit),
    backgroundPosition: normalizeChoice(value.backgroundPosition, ["center", "top", "bottom"], DEFAULT_APP_THEME.backgroundPosition),
    backgroundBlur: clamp(Number.isFinite(Number(value.backgroundBlur)) ? Number(value.backgroundBlur) : DEFAULT_APP_THEME.backgroundBlur, 0, 30),
    backgroundDim: clamp(Number.isFinite(Number(value.backgroundDim)) ? Number(value.backgroundDim) : DEFAULT_APP_THEME.backgroundDim, 0, 70),
    glassOpacity: clamp(Number.isFinite(Number(value.glassOpacity)) ? Number(value.glassOpacity) : DEFAULT_APP_THEME.glassOpacity, 2, 20),
    glassBlur: clamp(Number.isFinite(Number(value.glassBlur)) ? Number(value.glassBlur) : DEFAULT_APP_THEME.glassBlur, 0, 40),
    glassBorder: clamp(Number.isFinite(Number(value.glassBorder)) ? Number(value.glassBorder) : DEFAULT_APP_THEME.glassBorder, 4, 30),
    backgroundImage: typeof value.backgroundImage === "string" ? value.backgroundImage : "",
  };
}

export function buildAppThemeVars(value) {
  const theme = normalizeAppTheme(value);
  const a = hexToRgb(theme.colorA);
  const b = hexToRgb(theme.colorB);
  const intensity = theme.intensity / 100;
  const accentA = mixRgb(a, { r: 255, g: 255, b: 255 }, 0.36);
  const accentB = mixRgb(b, { r: 255, g: 255, b: 255 }, 0.44);
  const glassAlpha = theme.glassOpacity / 100;
  const borderAlpha = theme.glassBorder / 100;

  return {
    "--theme-palette-a": theme.colorA,
    "--theme-palette-b": theme.colorB,
    "--theme-palette-a-soft": rgba(a, 0.16 + intensity * 0.22),
    "--theme-palette-b-soft": rgba(b, 0.15 + intensity * 0.20),
    "--theme-palette-a-faint": rgba(a, 0.055 + intensity * 0.075),
    "--theme-palette-b-faint": rgba(b, 0.05 + intensity * 0.07),
    "--theme-accent": rgbToHex(accentA),
    "--theme-accent-2": rgbToHex(accentB),
    "--theme-accent-soft": rgba(accentA, 0.18),
    "--theme-accent-2-soft": rgba(accentB, 0.18),
    "--theme-accent-line": rgba(accentA, 0.30),
    "--theme-accent-2-line": rgba(accentB, 0.30),
    "--theme-gradient-angle": `${theme.angle}deg`,
    "--theme-background-blur": `${theme.backgroundBlur}px`,
    "--theme-background-dim": (theme.backgroundDim / 100).toFixed(3),
    "--theme-background-fit": theme.backgroundFit,
    "--theme-background-position": theme.backgroundPosition,
    "--theme-glass": `rgba(255, 255, 255, ${glassAlpha.toFixed(3)})`,
    "--theme-glass-strong": `rgba(8, 10, 15, ${(0.22 + glassAlpha * 2.4).toFixed(3)})`,
    "--theme-line": `rgba(255, 255, 255, ${borderAlpha.toFixed(3)})`,
    "--theme-line-strong": `rgba(255, 255, 255, ${Math.min(borderAlpha * 1.65, .46).toFixed(3)})`,
    "--theme-glass-blur": `${theme.glassBlur}px`,
  };
}

export function applyAppTheme(target, value) {
  const theme = normalizeAppTheme(value);
  if (!target) return theme;
  const vars = buildAppThemeVars(theme);
  Object.entries(vars).forEach(([name, cssValue]) => target.style.setProperty(name, cssValue));
  target.dataset.themeBackground = theme.backgroundMode;
  target.style.setProperty("--theme-background-image", theme.backgroundImage ? `url("${theme.backgroundImage}")` : "none");
  return theme;
}

function readJsonTheme(storage, key) {
  try {
    const raw = storage?.getItem?.(key);
    if (!raw) return null;
    return normalizeAppTheme(JSON.parse(raw));
  } catch {
    return null;
  }
}

function readInitialTheme(storage) {
  const backgroundImage = storage?.getItem?.(BACKGROUND_IMAGE_KEY) || "";
  const current = readJsonTheme(storage, STORAGE_KEY);
  if (current) return normalizeAppTheme({ ...current, backgroundImage });
  const oldTheme = readJsonTheme(storage, LEGACY_THEME_KEY);
  if (oldTheme) return normalizeAppTheme({ ...oldTheme, backgroundImage });
  const legacySkin = storage?.getItem?.(LEGACY_SKIN_KEY);
  return normalizeAppTheme({ ...(LEGACY_SKIN_PALETTES[legacySkin] || DEFAULT_APP_THEME), backgroundImage });
}

function themeSettingsOnly(theme) {
  const { backgroundImage, ...settings } = normalizeAppTheme(theme);
  return settings;
}

function hasMeaningfulLegacyTheme(theme) {
  const normalized = normalizeAppTheme(theme);
  if (normalized.backgroundImage) return true;
  const current = themeSettingsOnly(normalized);
  const defaults = themeSettingsOnly(DEFAULT_APP_THEME);
  return Object.keys(defaults).some(key => current[key] !== defaults[key]);
}

function writeStoredTheme(storage, theme) {
  try {
    const { backgroundImage, ...settings } = theme;
    storage?.setItem?.(STORAGE_KEY, JSON.stringify(settings));
    storage?.removeItem?.(LEGACY_THEME_KEY);
    storage?.removeItem?.(LEGACY_SKIN_KEY);
  } catch {
    // Appearance preferences are cosmetic and must never break the app shell.
  }
}

function readBlobAsDataUrl(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ""));
    reader.onerror = () => reject(reader.error || new Error("Unable to read image."));
    reader.readAsDataURL(blob);
  });
}

async function compressBackgroundFile(file) {
  if (!file?.type?.startsWith?.("image/")) throw new Error("請選擇圖片檔。");
  if (file.size > 25 * 1024 * 1024) throw new Error("圖片太大，請使用 25 MB 以下的圖片。");

  const bitmap = await createImageBitmap(file);
  const maxDimension = 2560;
  const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height));
  const width = Math.max(1, Math.round(bitmap.width * scale));
  const height = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d", { alpha: false });
  context.drawImage(bitmap, 0, 0, width, height);
  bitmap.close?.();

  for (const quality of [.88, .80, .72, .64]) {
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/webp", quality));
    if (blob && (blob.size <= 2_600_000 || quality === .64)) return readBlobAsDataUrl(blob);
  }
  throw new Error("圖片壓縮失敗。");
}

export function createAppThemeController({
  target = document.documentElement,
  root = document,
  storage = localStorage,
  requestJson = null,
} = {}) {
  const colorA = root?.getElementById?.("appThemeColorA");
  const colorB = root?.getElementById?.("appThemeColorB");
  const angle = root?.getElementById?.("appThemeAngle");
  const angleValue = root?.getElementById?.("appThemeAngleValue");
  const intensity = root?.getElementById?.("appThemeIntensity");
  const intensityValue = root?.getElementById?.("appThemeIntensityValue");
  const backgroundMode = root?.getElementById?.("appThemeBackgroundMode");
  const backgroundUpload = root?.getElementById?.("appThemeBackgroundUpload");
  const backgroundClear = root?.getElementById?.("appThemeBackgroundClear");
  const backgroundFit = root?.getElementById?.("appThemeBackgroundFit");
  const backgroundPosition = root?.getElementById?.("appThemeBackgroundPosition");
  const backgroundBlur = root?.getElementById?.("appThemeBackgroundBlur");
  const backgroundBlurValue = root?.getElementById?.("appThemeBackgroundBlurValue");
  const backgroundDim = root?.getElementById?.("appThemeBackgroundDim");
  const backgroundDimValue = root?.getElementById?.("appThemeBackgroundDimValue");
  const glassOpacity = root?.getElementById?.("appThemeGlassOpacity");
  const glassOpacityValue = root?.getElementById?.("appThemeGlassOpacityValue");
  const glassBlur = root?.getElementById?.("appThemeGlassBlur");
  const glassBlurValue = root?.getElementById?.("appThemeGlassBlurValue");
  const glassBorder = root?.getElementById?.("appThemeGlassBorder");
  const glassBorderValue = root?.getElementById?.("appThemeGlassBorderValue");
  const backgroundStatus = root?.getElementById?.("appThemeBackgroundStatus");
  const reset = root?.getElementById?.("appThemeReset");
  const preview = root?.getElementById?.("appThemePreview");
  const legacyInitial = readInitialTheme(storage);
  let current = legacyInitial;
  let remoteReady = false;
  let saveTimer = null;
  let pollTimer = null;
  let lastLocalEditAt = 0;

  function syncControls() {
    if (colorA) colorA.value = current.colorA;
    if (colorB) colorB.value = current.colorB;
    if (angle) angle.value = String(current.angle);
    if (angleValue) angleValue.textContent = `${Math.round(current.angle)}°`;
    if (intensity) intensity.value = String(current.intensity);
    if (intensityValue) intensityValue.textContent = `${Math.round(current.intensity)}%`;
    if (backgroundMode) backgroundMode.value = current.backgroundMode;
    if (backgroundFit) backgroundFit.value = current.backgroundFit;
    if (backgroundPosition) backgroundPosition.value = current.backgroundPosition;
    if (backgroundBlur) backgroundBlur.value = String(current.backgroundBlur);
    if (backgroundBlurValue) backgroundBlurValue.textContent = `${Math.round(current.backgroundBlur)}px`;
    if (backgroundDim) backgroundDim.value = String(current.backgroundDim);
    if (backgroundDimValue) backgroundDimValue.textContent = `${Math.round(current.backgroundDim)}%`;
    if (glassOpacity) glassOpacity.value = String(current.glassOpacity);
    if (glassOpacityValue) glassOpacityValue.textContent = `${Math.round(current.glassOpacity)}%`;
    if (glassBlur) glassBlur.value = String(current.glassBlur);
    if (glassBlurValue) glassBlurValue.textContent = `${Math.round(current.glassBlur)}px`;
    if (glassBorder) glassBorder.value = String(current.glassBorder);
    if (glassBorderValue) glassBorderValue.textContent = `${Math.round(current.glassBorder)}%`;
    if (backgroundClear) backgroundClear.disabled = !current.backgroundImage;
    if (backgroundStatus && !backgroundStatus.dataset.busy) {
      backgroundStatus.textContent = current.backgroundImage ? "已儲存自訂背景" : "尚未選擇自訂背景";
    }
    if (preview) {
      preview.style.setProperty("--preview-a", current.colorA);
      preview.style.setProperty("--preview-b", current.colorB);
      preview.style.setProperty("--preview-angle", `${current.angle}deg`);
      preview.style.setProperty("--preview-image", current.backgroundImage ? `url("${current.backgroundImage}")` : "none");
      preview.dataset.mode = current.backgroundMode;
    }
  }

  function setTheme(next, { persist = true, remote = true } = {}) {
    current = applyAppTheme(target, { ...current, ...next });
    syncControls();
    if (persist) writeStoredTheme(storage, current);
    if (remote && remoteReady) scheduleRemoteSave();
    return current;
  }

  function responseTheme(response) {
    const theme = response?.theme || response?.Theme || {};
    const backgroundUrl = response?.backgroundUrl ?? response?.BackgroundUrl ?? "";
    const hasBackground = Boolean(response?.hasBackgroundImage ?? response?.HasBackgroundImage);
    return normalizeAppTheme({
      ...theme,
      backgroundImage: hasBackground ? backgroundUrl : "",
    });
  }

  function applyRemoteResponse(response) {
    current = applyAppTheme(target, responseTheme(response));
    syncControls();
    writeStoredTheme(storage, current);
    if (current.backgroundImage?.startsWith?.("/api/appearance/background")) {
      try { storage?.removeItem?.(BACKGROUND_IMAGE_KEY); } catch { }
    }
    return current;
  }

  async function saveThemeToHost() {
    if (!requestJson || !remoteReady) return current;
    const response = await requestJson("/api/appearance/theme", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(themeSettingsOnly(current)),
    });
    return applyRemoteResponse(response);
  }

  function scheduleRemoteSave() {
    lastLocalEditAt = Date.now();
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      saveTimer = null;
      saveThemeToHost().catch(error => {
        if (backgroundStatus) backgroundStatus.textContent = error?.message || "布景設定儲存失敗。";
      });
    }, 220);
  }

  async function migrateLegacyTheme() {
    if (!requestJson || !hasMeaningfulLegacyTheme(legacyInitial)) return null;
    let response = null;
    if (legacyInitial.backgroundImage) {
      response = await requestJson("/api/appearance/background", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ dataUrl: legacyInitial.backgroundImage }),
      });
    }
    response = await requestJson("/api/appearance/theme", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(themeSettingsOnly(legacyInitial)),
    });
    return response;
  }

  async function loadFromHost({ migrate = true } = {}) {
    if (!requestJson) return current;
    const response = await requestJson("/api/appearance/theme");
    const configured = Boolean(response?.configured ?? response?.Configured);
    let resolved = response;
    if (!configured && migrate && hasMeaningfulLegacyTheme(legacyInitial)) {
      resolved = await migrateLegacyTheme() || response;
    }
    remoteReady = true;
    return applyRemoteResponse(resolved);
  }

  function startPolling(intervalMs = 10000) {
    if (!requestJson || pollTimer) return;
    pollTimer = setInterval(() => {
      if (Date.now() - lastLocalEditAt < 2500) return;
      if (typeof document !== "undefined" && document.visibilityState === "hidden") return;
      loadFromHost({ migrate: false }).catch(() => {});
    }, intervalMs);
  }

  function readControls() {
    return {
      colorA: colorA?.value || current.colorA,
      colorB: colorB?.value || current.colorB,
      angle: angle?.value ?? current.angle,
      intensity: intensity?.value ?? current.intensity,
      backgroundMode: backgroundMode?.value || current.backgroundMode,
      backgroundFit: backgroundFit?.value || current.backgroundFit,
      backgroundPosition: backgroundPosition?.value || current.backgroundPosition,
      backgroundBlur: backgroundBlur?.value ?? current.backgroundBlur,
      backgroundDim: backgroundDim?.value ?? current.backgroundDim,
      glassOpacity: glassOpacity?.value ?? current.glassOpacity,
      glassBlur: glassBlur?.value ?? current.glassBlur,
      glassBorder: glassBorder?.value ?? current.glassBorder,
    };
  }

  [colorA, colorB, angle, intensity, backgroundMode, backgroundFit, backgroundPosition, backgroundBlur, backgroundDim, glassOpacity, glassBlur, glassBorder].forEach(control => {
    control?.addEventListener?.("input", () => setTheme(readControls()));
  });

  backgroundUpload?.addEventListener?.("change", async () => {
    const file = backgroundUpload.files?.[0];
    if (!file) return;
    if (backgroundStatus) {
      backgroundStatus.dataset.busy = "true";
      backgroundStatus.textContent = "正在處理背景圖片…";
    }
    try {
      const dataUrl = await compressBackgroundFile(file);
      if (requestJson && remoteReady) {
        const response = await requestJson("/api/appearance/background", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ dataUrl }),
        });
        applyRemoteResponse(response);
      } else {
        storage?.setItem?.(BACKGROUND_IMAGE_KEY, dataUrl);
        setTheme({ backgroundImage: dataUrl, backgroundMode: "image" }, { remote: false });
      }
      if (backgroundStatus) backgroundStatus.textContent = "背景圖片已儲存到 VibeDeck Host";
    } catch (error) {
      if (backgroundStatus) backgroundStatus.textContent = error?.message || "背景圖片處理失敗。";
    } finally {
      if (backgroundStatus) delete backgroundStatus.dataset.busy;
      backgroundUpload.value = "";
    }
  });

  backgroundClear?.addEventListener?.("click", async () => {
    try {
      if (requestJson && remoteReady) {
        const response = await requestJson("/api/appearance/background", { method: "DELETE" });
        applyRemoteResponse(response);
      } else {
        storage?.removeItem?.(BACKGROUND_IMAGE_KEY);
        setTheme({ backgroundImage: "", backgroundMode: "gradient" }, { remote: false });
      }
    } catch (error) {
      if (backgroundStatus) backgroundStatus.textContent = error?.message || "背景圖片刪除失敗。";
    }
  });

  root?.querySelectorAll?.("[data-app-palette]").forEach(button => {
    button.addEventListener("click", () => {
      const preset = APP_PALETTE_PRESETS[button.dataset.appPalette];
      if (preset) setTheme(preset);
    });
  });

  reset?.addEventListener?.("click", async () => {
    try {
      if (requestJson && remoteReady) {
        await requestJson("/api/appearance/background", { method: "DELETE" });
        const response = await requestJson("/api/appearance/theme", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(themeSettingsOnly(DEFAULT_APP_THEME)),
        });
        applyRemoteResponse(response);
      } else {
        storage?.removeItem?.(BACKGROUND_IMAGE_KEY);
        setTheme(DEFAULT_APP_THEME, { remote: false });
      }
    } catch (error) {
      if (backgroundStatus) backgroundStatus.textContent = error?.message || "布景重設失敗。";
    }
  });

  current = applyAppTheme(target, current);
  syncControls();
  writeStoredTheme(storage, current);

  return {
    getTheme: () => ({ ...current }),
    setTheme,
    loadFromHost,
    startPolling,
  };
}
