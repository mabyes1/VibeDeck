const AUTO_MODE_VALUE = "__auto__";

function positiveNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? number : 0;
}

function even(value) {
  return Math.max(2, Math.round(value / 2) * 2);
}

export function getAutoModeValue() {
  return AUTO_MODE_VALUE;
}

export function computeClientDisplayTarget(metrics = {}) {
  const screenWidth = positiveNumber(metrics.screenWidth) || positiveNumber(metrics.viewportWidth);
  const screenHeight = positiveNumber(metrics.screenHeight) || positiveNumber(metrics.viewportHeight);
  if (!screenWidth || !screenHeight) return null;

  const viewportWidth = positiveNumber(metrics.viewportWidth) || screenWidth;
  const viewportHeight = positiveNumber(metrics.viewportHeight) || screenHeight;
  const landscape = viewportWidth >= viewportHeight;
  const longEdge = Math.max(screenWidth, screenHeight);
  const shortEdge = Math.min(screenWidth, screenHeight);
  const dpr = Math.max(1, Math.min(1.5, positiveNumber(metrics.devicePixelRatio) || 1));

  let targetLong = longEdge * dpr;
  if (targetLong > 1600) targetLong = 1600;
  if (targetLong < 800) targetLong = 800;
  const targetShort = targetLong * (shortEdge / longEdge);

  return landscape
    ? { width: even(targetLong), height: even(targetShort) }
    : { width: even(targetShort), height: even(targetLong) };
}

export function chooseAutoDisplayMode(presets, metrics = {}) {
  const target = computeClientDisplayTarget(metrics);
  if (!target) return null;

  const candidates = (Array.isArray(presets) ? presets : [])
    .map(preset => ({
      preset,
      width: positiveNumber(preset?.Width ?? preset?.width),
      height: positiveNumber(preset?.Height ?? preset?.height),
    }))
    .filter(item => item.width && item.height)
    .filter(item => (item.width >= item.height) === (target.width >= target.height));
  if (!candidates.length) return null;

  const targetRatio = target.width / target.height;
  const targetArea = target.width * target.height;
  candidates.sort((left, right) => {
    const leftRatio = left.width / left.height;
    const rightRatio = right.width / right.height;
    const leftScore = Math.abs(Math.log(leftRatio / targetRatio)) * 8
      + Math.abs(Math.log((left.width * left.height) / targetArea));
    const rightScore = Math.abs(Math.log(rightRatio / targetRatio)) * 8
      + Math.abs(Math.log((right.width * right.height) / targetArea));
    return leftScore - rightScore;
  });

  return {
    preset: candidates[0].preset,
    target,
  };
}

export function readClientDisplayMetrics(windowObject = globalThis.window, screenObject = globalThis.screen) {
  return {
    screenWidth: positiveNumber(screenObject?.width),
    screenHeight: positiveNumber(screenObject?.height),
    viewportWidth: positiveNumber(windowObject?.innerWidth),
    viewportHeight: positiveNumber(windowObject?.innerHeight),
    devicePixelRatio: positiveNumber(windowObject?.devicePixelRatio) || 1,
  };
}
