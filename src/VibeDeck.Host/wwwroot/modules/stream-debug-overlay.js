function finite(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function nonNegative(value) {
  const number = finite(value);
  return number !== null && number >= 0 ? number : null;
}

function firstValue(source, ...keys) {
  for (const key of keys) {
    const value = source?.[key];
    if (value !== undefined && value !== null) return value;
  }
  return null;
}

function metric(source, ...keys) {
  return nonNegative(firstValue(source, ...keys));
}

function text(source, ...keys) {
  const value = firstValue(source, ...keys);
  return value == null ? "" : String(value);
}

function formatNumber(value, digits = 0) {
  return value === null ? "?" : value.toFixed(digits);
}

function pushMetric(parts, label, value, suffix = "", digits = 0) {
  if (value === null) return;
  parts.push(`${label} ${formatNumber(value, digits)}${suffix}`);
}

export function classifyStreamBottleneck({ client = null, host = null, transport = null } = {}) {
  const pacingDebtMs = metric(transport, "PacingDebtMs", "pacingDebtMs");
  if (pacingDebtMs !== null && pacingDebtMs >= 120) {
    return { code: "pacer", label: "PACER", detail: `排程積壓 ${pacingDebtMs.toFixed(0)}ms` };
  }

  const targetFps = metric(host, "TargetFps", "targetFps");
  const hostFps = metric(host, "RecentQueuedFps", "recentQueuedFps");
  if (targetFps && hostFps !== null && hostFps < targetFps * 0.8) {
    return { code: "capture-encode", label: "CAPTURE/ENCODE", detail: `Host ${hostFps.toFixed(0)}/${targetFps.toFixed(0)}fps` };
  }

  const bufferMs = metric(client, "bufferMs");
  if (bufferMs !== null && bufferMs >= 100) {
    return { code: "jitter-buffer", label: "JITTER BUFFER", detail: `${bufferMs.toFixed(0)}ms` };
  }

  const clientFps = metric(client, "fps");
  const decodeMs = metric(client, "decodeMs");
  const dropped = metric(client, "dropped");
  const frameBudgetMs = clientFps && clientFps > 0 ? 1000 / clientFps : 55.6;
  if ((decodeMs !== null && decodeMs >= Math.max(12, frameBudgetMs * 0.7)) ||
      (dropped !== null && dropped > 2 && clientFps !== null && targetFps && clientFps < targetFps * 0.8)) {
    return { code: "decoder", label: "DECODER", detail: decodeMs !== null ? `${decodeMs.toFixed(1)}ms/frame` : `drop ${dropped}` };
  }

  const rttMs = metric(client, "rttMs");
  const jitterMs = metric(client, "jitterMs");
  const packetsLost = metric(client, "packetsLost");
  const nack = metric(client, "nack");
  if ((rttMs !== null && rttMs >= 80) ||
      (jitterMs !== null && jitterMs >= 30) ||
      (packetsLost !== null && packetsLost > 0) ||
      (nack !== null && nack >= 8)) {
    return { code: "network", label: "NETWORK", detail: `RTT ${formatNumber(rttMs)}ms · jitter ${formatNumber(jitterMs)}ms` };
  }

  if (!client && !host && !transport) {
    return { code: "waiting", label: "WAITING", detail: "等待串流資料" };
  }
  return { code: "healthy", label: "OK", detail: "目前無明顯瓶頸" };
}

export function buildStreamDebugView({ client = null, host = null, session = null, path = "" } = {}) {
  const transport = session?.Transport ?? session?.transport ?? null;
  const clientParts = [];
  if (path) clientParts.push(path);
  pushMetric(clientParts, "RX", metric(client, "fps"), "fps");
  pushMetric(clientParts, "", metric(client, "bitrateMbps"), "Mbps", 1);
  const width = metric(client, "width");
  const height = metric(client, "height");
  if (width !== null && height !== null) clientParts.push(`${width.toFixed(0)}×${height.toFixed(0)}`);
  pushMetric(clientParts, "RTT", metric(client, "rttMs"), "ms");
  pushMetric(clientParts, "jitter", metric(client, "jitterMs"), "ms");
  pushMetric(clientParts, "buffer", metric(client, "bufferMs"), "ms");
  pushMetric(clientParts, "decode", metric(client, "decodeMs"), "ms", 1);
  pushMetric(clientParts, "drop", metric(client, "dropped"));
  pushMetric(clientParts, "loss", metric(client, "packetsLost"));
  pushMetric(clientParts, "nack", metric(client, "nack"));
  pushMetric(clientParts, "pli", metric(client, "pli"));

  const hostParts = [];
  const hostFps = metric(host, "RecentQueuedFps", "recentQueuedFps");
  const targetFps = metric(host, "TargetFps", "targetFps");
  if (hostFps !== null || targetFps !== null) {
    hostParts.push(`HOST ${formatNumber(hostFps)}/${formatNumber(targetFps)}fps`);
  }
  pushMetric(hostParts, "", metric(host, "RecentMbps", "recentMbps"), "Mbps", 1);
  pushMetric(hostParts, "skip", metric(host, "RecentSkippedFps", "recentSkippedFps"), "fps", 1);
  const hostWidth = metric(host, "Width", "width");
  const hostHeight = metric(host, "Height", "height");
  if (hostWidth !== null && hostHeight !== null) hostParts.push(`${hostWidth.toFixed(0)}×${hostHeight.toFixed(0)}`);
  for (const value of [
    text(host, "CapturePath", "capturePath"),
    text(host, "Encoder", "encoder"),
    text(host, "QualityTier", "qualityTier"),
  ]) {
    if (value) hostParts.push(value);
  }

  const transportParts = [];
  const pacingDebtMs = metric(transport, "PacingDebtMs", "pacingDebtMs");
  pushMetric(transportParts, "PACER", pacingDebtMs, "ms");
  pushMetric(transportParts, "target", metric(transport, "TargetBitrateKbps", "targetBitrateKbps"), "kbps");
  pushMetric(transportParts, "NACK", metric(transport, "NackRequests", "nackRequests"));
  pushMetric(transportParts, "RTX", metric(transport, "PacketsRetransmitted", "packetsRetransmitted"));
  pushMetric(transportParts, "PLI", metric(transport, "PliRequests", "pliRequests"));

  const rttMs = metric(client, "rttMs");
  const bufferMs = metric(client, "bufferMs");
  const decodeMs = metric(client, "decodeMs");
  const latencyParts = [pacingDebtMs, rttMs === null ? null : rttMs / 2, bufferMs, decodeMs]
    .filter(value => value !== null);
  const estimatedLatencyMs = latencyParts.length
    ? latencyParts.reduce((total, value) => total + value, 0)
    : null;
  const bottleneck = classifyStreamBottleneck({ client, host, transport });

  return {
    summary: estimatedLatencyMs === null
      ? `疑似瓶頸：${bottleneck.label} · ${bottleneck.detail}`
      : `估計管線延遲 ${estimatedLatencyMs.toFixed(0)}ms · 疑似瓶頸：${bottleneck.label} · ${bottleneck.detail}`,
    client: clientParts.length ? clientParts.join(" · ") : "CLIENT 等待 WebRTC stats",
    host: hostParts.length ? hostParts.join(" · ") : "HOST 等待串流 metrics",
    transport: transportParts.length ? transportParts.join(" · ") : "TRANSPORT 等待 RTP metrics",
    estimatedLatencyMs,
    bottleneck,
  };
}

export function createStreamDebugOverlay({
  elements,
  fetchJsonOrThrow,
  getDeviceName,
  storage = globalThis.localStorage,
  setIntervalFn = globalThis.setInterval?.bind(globalThis),
  clearIntervalFn = globalThis.clearInterval?.bind(globalThis),
  documentObject = globalThis.document,
  pollMs = 2500,
} = {}) {
  const STORAGE_KEY = "vibeDeck.streamDebug.v1";
  let enabled = false;
  let timer = null;
  let client = null;
  let hostSnapshot = null;
  let session = null;
  let path = "";

  function render() {
    const view = buildStreamDebugView({ client, host: hostSnapshot, session, path });
    if (elements?.summary) elements.summary.textContent = view.summary;
    if (elements?.client) elements.client.textContent = view.client;
    if (elements?.host) elements.host.textContent = view.host;
    if (elements?.transport) elements.transport.textContent = view.transport;
    return view;
  }

  async function refreshHost() {
    if (!enabled || typeof fetchJsonOrThrow !== "function") return;
    try {
      const deviceName = String(getDeviceName?.() || "");
      const query = deviceName ? `?deviceName=${encodeURIComponent(deviceName)}` : "";
      const snapshot = await fetchJsonOrThrow(`/api/stream/client-diagnostics${query}`);
      if (!enabled) return;
      hostSnapshot = snapshot?.h264 ?? snapshot?.H264 ?? null;
      session = snapshot?.session ?? snapshot?.Session ?? null;
      render();
    } catch (error) {
      if (elements?.host) elements.host.textContent = `HOST diagnostics unavailable · ${error?.message || "error"}`;
    }
  }

  function syncTimer() {
    if (timer && clearIntervalFn) clearIntervalFn(timer);
    timer = null;
    if (enabled && setIntervalFn) {
      timer = setIntervalFn(() => { void refreshHost(); }, pollMs);
    }
  }

  function setEnabled(next, persist = true) {
    enabled = Boolean(next);
    if (elements?.bar) elements.bar.hidden = !enabled;
    if (elements?.toggle) elements.toggle.setAttribute("aria-pressed", enabled ? "true" : "false");
    documentObject?.body?.classList?.toggle("stream-debug-visible", enabled);
    if (persist) {
      try { storage?.setItem(STORAGE_KEY, enabled ? "1" : "0"); } catch { }
    }
    syncTimer();
    if (enabled) {
      render();
      void refreshHost();
    }
  }

  function wire() {
    elements?.toggle?.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      setEnabled(!enabled);
    });
    let stored = false;
    try { stored = storage?.getItem(STORAGE_KEY) === "1"; } catch { }
    setEnabled(stored, false);
  }

  return {
    wire,
    setEnabled,
    isEnabled: () => enabled,
    updateClientStats(stats) {
      client = stats || null;
      path = String(stats?.path || path || "");
      if (enabled) render();
    },
    refreshHost,
    render,
    stop() {
      if (timer && clearIntervalFn) clearIntervalFn(timer);
      timer = null;
    },
  };
}
