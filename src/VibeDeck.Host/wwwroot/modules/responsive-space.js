const SIDEBOARD_SPACE_CLASSES = [
  "space-sideboard-micro",
  "space-sideboard-compact",
  "space-sideboard-mid",
  "space-sideboard-wide",
];

const HEADER_SPACE_CLASSES = [
  "space-header-max-32",
  "space-header-max-36",
  "space-header-max-48",
  "space-header-min-48",
];

function positiveWidth(element) {
  const rectWidth = Number(element?.getBoundingClientRect?.().width) || 0;
  return rectWidth > 0 ? rectWidth : Number(element?.clientWidth) || 0;
}

export function sideboardSpaceClasses(width, rootFontSize = 16) {
  const rem = Math.max(1, Number(rootFontSize) || 16);
  const compact = width <= 44 * rem;
  return [
    width <= 18 * rem ? "space-sideboard-micro" : "",
    compact ? "space-sideboard-compact" : "",
    !compact && width <= 64 * rem ? "space-sideboard-mid" : "",
    width > 64 * rem ? "space-sideboard-wide" : "",
  ].filter(Boolean);
}

export function headerSpaceClasses(width, rootFontSize = 16) {
  const rem = Math.max(1, Number(rootFontSize) || 16);
  return [
    width <= 32 * rem ? "space-header-max-32" : "",
    width <= 36 * rem ? "space-header-max-36" : "",
    width <= 48 * rem ? "space-header-max-48" : "",
    width >= 48 * rem ? "space-header-min-48" : "",
  ].filter(Boolean);
}

function replaceSpaceClasses(element, knownClasses, nextClasses) {
  if (!element) return;
  element.classList.remove(...knownClasses);
  element.classList.add(...nextClasses);
}

export function createResponsiveSpaceController({
  documentObject = document,
  windowObject = window,
} = {}) {
  const root = documentObject.documentElement;
  const sideboard = documentObject.getElementById("sideboardView");
  const header = documentObject.querySelector("header");
  const observed = [sideboard, header].filter(Boolean);

  function rootFontSize() {
    const value = Number.parseFloat(windowObject.getComputedStyle?.(root)?.fontSize || "16");
    return Number.isFinite(value) && value > 0 ? value : 16;
  }

  function refresh() {
    const rem = rootFontSize();
    const sideboardWidth = positiveWidth(sideboard);
    if (sideboardWidth > 0) {
      replaceSpaceClasses(sideboard, SIDEBOARD_SPACE_CLASSES, sideboardSpaceClasses(sideboardWidth, rem));
    }
    const headerWidth = positiveWidth(header);
    if (headerWidth > 0) {
      replaceSpaceClasses(header, HEADER_SPACE_CLASSES, headerSpaceClasses(headerWidth, rem));
    }
  }

  const ResizeObserverType = windowObject.ResizeObserver;
  const observer = typeof ResizeObserverType === "function"
    ? new ResizeObserverType(refresh)
    : null;
  observed.forEach(element => observer?.observe(element));
  windowObject.addEventListener?.("resize", refresh, { passive: true });
  refresh();
  windowObject.requestAnimationFrame?.(refresh);

  return {
    refresh,
    disconnect() {
      observer?.disconnect();
      windowObject.removeEventListener?.("resize", refresh);
    },
  };
}
