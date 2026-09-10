import { tLegacy } from "./i18n.js?v=4";
import { calculateInboundVideoStats } from "./stream-tuning.js?v=48";
import {
  createWebRtcRetryScheduler,
  webRtcCooldownMs,
} from "./webrtc-retry-scheduler.js?v=1";

const WEBRTC_DISCONNECT_GRACE_MS = 12000;
const WEBRTC_INITIAL_CONNECT_TIMEOUT_MS = 18000;
const WEBRTC_SESSION_REBUILD_LIMIT = 2;
const WEBRTC_REBUILD_STABLE_MS = 30000;
const JPEG_RECONNECT_MAX_MS = 15000;

export function normalizePlayoutDelayMs(value) {
  const delay = Number(value);
  return delay === 20 || delay === 80 ? delay : 40;
}

export function estimateReceiverMaxBitrateKbps(settings = {}, environment = globalThis) {
  const navigatorValue = environment?.navigator || {};
  const userAgent = String(navigatorValue.userAgent || "");
  const mobileReceiver = Boolean(navigatorValue.userAgentData?.mobile) ||
    /Android|iPhone|iPad|iPod|Mobile/i.test(userAgent);
  if (!mobileReceiver) return 0;

  const screenValue = environment?.screen || {};
  const devicePixelRatio = Math.max(1, Math.min(3, Number(environment?.devicePixelRatio) || 1));
  const width = Math.max(320, Number(screenValue.width) || Number(environment?.innerWidth) || 640) * devicePixelRatio;
  const height = Math.max(240, Number(screenValue.height) || Number(environment?.innerHeight) || 480) * devicePixelRatio;
  const fps = Math.max(1, Math.min(60, Number(settings.fps) || 18));
  const quality = Math.max(25, Math.min(85, Number(settings.quality) || 48));
  const qualityRatio = (quality - 25) / 60;
  const bitsPerPixel = 0.060 + qualityRatio * 0.040;
  let limit = width * height * fps * bitsPerPixel / 1000;

  // NetworkInformation is only a hint, but it is useful as a ceiling. Keep
  // enough headroom for RTP/RTCP, input traffic, and 2.4 GHz contention.
  const downlinkMbps = Number(navigatorValue.connection?.downlink);
  if (Number.isFinite(downlinkMbps) && downlinkMbps > 0) {
    limit = Math.min(limit, downlinkMbps * 550);
  }

  limit = Math.max(800, Math.min(2500, limit));
  return Math.round(limit / 50) * 50;
}

export function createStreamController({
  elements,
  getWsBase,
  appendDeviceToken,
  getSelectedDisplayName,
  getStreamSettings,
  canUseProtectedConnection,
  loadDisplays,
  prefersWebRtcDisplay,
  isLoopbackHost,
  setStatus,
  applyRotation,
  resetJpegStats,
  recordJpegFrame,
  fetchJsonOrThrow,
  tuneVideoReceiver,
  reportDiagnostic = () => {},
  onStreamStats = () => {},
  createRetryScheduler = createWebRtcRetryScheduler,
  getNow = () => Date.now(),
  setRecoveryTimer = setTimeout,
  clearRecoveryTimer = clearTimeout,
}) {
  let videoSocket = null;
  let rtcPeer = null;
  let rtcActive = false;
  let connectGeneration = 0;
  let fallbackReason = "";
  let disconnectTimer = null;
  let rtcStableTimer = null;
  let jpegReconnectTimer = null;
  let statsTimer = null;
  let pendingJpegFrame = null;
  let activeJpegObjectUrl = null;
  let jpegFrameDecoding = false;
  let jpegReconnectAttempts = 0;
  let webrtcCooldownUntil = 0;
  let rtcSessionRebuildAttempts = 0;
  let rebuildingRtcSession = false;
  let initialConnectTimer = null;
  let initialConnectDeadline = 0;
  let selectedPath = "";
  let lastDiagnosticKey = "";
  const webrtcRetryScheduler = createRetryScheduler({
    getGeneration: () => connectGeneration,
    getTransportMode: transportMode,
    prefersWebRtcDisplay,
    retry: () => { void connect(); },
    now: getNow,
  });

  function getActiveStreamElement() {
    return rtcActive ? elements.rtcScreen : elements.screen;
  }

  function transportMode() {
    const mode = String(getStreamSettings().transportMode || "auto").toLowerCase();
    return ["auto", "webrtc", "jpeg"].includes(mode) ? mode : "auto";
  }

  function clearRtcRecoveryTimers() {
    if (initialConnectTimer) {
      clearRecoveryTimer(initialConnectTimer);
      initialConnectTimer = null;
    }
    initialConnectDeadline = 0;
    if (disconnectTimer) {
      clearRecoveryTimer(disconnectTimer);
      disconnectTimer = null;
    }
    if (rtcStableTimer) {
      clearRecoveryTimer(rtcStableTimer);
      rtcStableTimer = null;
    }
  }

  function clearJpegReconnectTimer() {
    if (jpegReconnectTimer) {
      clearTimeout(jpegReconnectTimer);
      jpegReconnectTimer = null;
    }
  }

  function report(event, state, details = {}, force = false) {
    const payload = {
      event,
      state,
      mode: transportMode(),
      path: details.path || selectedPath || "",
      connectionState: details.connectionState || rtcPeer?.connectionState || "",
      iceState: details.iceState || rtcPeer?.iceConnectionState || "",
      reason: details.reason || "",
    };
    const key = [payload.event, payload.state, payload.mode, payload.path, payload.connectionState, payload.iceState, payload.reason].join("|");
    if (!force && key === lastDiagnosticKey) return;
    lastDiagnosticKey = key;
    try { reportDiagnostic(payload); } catch { }
  }

  function closeJpegStream() {
    clearJpegReconnectTimer();
    if (videoSocket) {
      const socket = videoSocket;
      videoSocket = null;
      try { socket.close(); } catch { }
    }
    clearJpegFrameQueue();
  }

  function closeRtcStream(invalidate = true) {
    if (invalidate) {
      connectGeneration += 1;
      webrtcRetryScheduler.cancel();
    }
    const peer = rtcPeer;
    rtcPeer = null;
    rtcActive = false;
    clearRtcRecoveryTimers();
    if (peer) {
      try { peer.close(); } catch { }
    }
    if (statsTimer) {
      clearInterval(statsTimer);
      statsTimer = null;
    }
    try { onStreamStats(null); } catch { }
    elements.rtcScreen.srcObject = null;
    elements.rtcScreen.hidden = true;
    elements.screen.hidden = false;
  }

  function clearJpegFrameQueue() {
    pendingJpegFrame = null;
    if (activeJpegObjectUrl) {
      URL.revokeObjectURL(activeJpegObjectUrl);
      activeJpegObjectUrl = null;
    }
    jpegFrameDecoding = false;
  }

  function presentPendingJpegFrame() {
    if (jpegFrameDecoding || !pendingJpegFrame) return;
    const frame = pendingJpegFrame;
    pendingJpegFrame = null;
    jpegFrameDecoding = true;
    const url = URL.createObjectURL(frame);
    activeJpegObjectUrl = url;

    if (typeof elements.screen.decode === "function") {
      elements.screen.src = url;
      elements.screen.decode().then(() => finishJpegFrameDecode(url), () => finishJpegFrameDecode(url));
      return;
    }

    const complete = () => {
      elements.screen.removeEventListener("load", complete);
      elements.screen.removeEventListener("error", complete);
      finishJpegFrameDecode(url);
    };
    elements.screen.addEventListener("load", complete, { once: true });
    elements.screen.addEventListener("error", complete, { once: true });
    elements.screen.src = url;
  }

  function finishJpegFrameDecode(url) {
    if (!jpegFrameDecoding || url !== activeJpegObjectUrl) return;
    if (activeJpegObjectUrl) {
      URL.revokeObjectURL(activeJpegObjectUrl);
      activeJpegObjectUrl = null;
    }
    jpegFrameDecoding = false;
    requestAnimationFrame(presentPendingJpegFrame);
  }

  async function connect(options = {}) {
    if (options.preserveRecoveryBudget !== true) {
      rtcSessionRebuildAttempts = 0;
    }
    const generation = ++connectGeneration;
    webrtcRetryScheduler.cancel();
    fallbackReason = "";
    selectedPath = "";
    lastDiagnosticKey = "";
    jpegReconnectAttempts = 0;
    clearJpegReconnectTimer();
    closeJpegStream();
    closeRtcStream(false);

    if (!canUseProtectedConnection()) {
      setStatus(tLegacy("請先配對手機"), false);
      return;
    }
    if (!getSelectedDisplayName()) await loadDisplays();
    if (generation !== connectGeneration || !getSelectedDisplayName()) return;

    const mode = transportMode();
    const shouldTryWebRtc = mode !== "jpeg" && (mode === "webrtc" || prefersWebRtcDisplay());
    if (!shouldTryWebRtc) {
      fallbackReason = mode === "jpeg" ? tLegacy("穩定 JPEG 模式") : "";
      report("stream", "jpeg", { path: "jpeg", reason: fallbackReason });
      connectJpegVideo(fallbackReason);
      return;
    }
    const currentTime = getNow();
    if (currentTime < webrtcCooldownUntil) {
      const remaining = Math.ceil((webrtcCooldownUntil - currentTime) / 1000);
      fallbackReason = `${tLegacy("WebRTC 暫停重試，保持 JPEG 穩定串流")} (${remaining}s)`;
      report("webrtc", "cooldown", { path: "jpeg", reason: fallbackReason });
      webrtcRetryScheduler.schedule(generation, webrtcCooldownUntil);
      connectJpegVideo(fallbackReason);
      return;
    }
    if (!window.RTCPeerConnection) {
      fallbackReason = tLegacy("WebRTC API 不可用");
      report("webrtc", "unavailable", { path: "jpeg", reason: fallbackReason });
      connectJpegVideo(fallbackReason);
      return;
    }

    try {
      const connected = await connectRtcVideo(generation);
      if (connected && generation === connectGeneration) return;
    } catch (error) {
      console.warn("WebRTC negotiation failed; using JPEG fallback", error);
      if (generation === connectGeneration) {
        fallbackToJpeg(generation, `WebRTC ${tLegacy("無法連線，切回 JPEG")}：${error.message || tLegacy("未知錯誤")}`);
      }
      return;
    }
    if (generation === connectGeneration) {
      fallbackToJpeg(generation, tLegacy("WebRTC negotiation did not complete"));
    }
  }

  function waitForIceGatheringComplete(peer, timeoutMs = 3500) {
    if (peer.iceGatheringState === "complete") return Promise.resolve();
    return new Promise(resolve => {
      let finished = false;
      const finish = () => {
        if (finished) return;
        finished = true;
        clearTimeout(timer);
        peer.removeEventListener("icegatheringstatechange", onStateChange);
        resolve();
      };
      const onStateChange = () => {
        if (peer.iceGatheringState === "complete") finish();
      };
      const timer = setTimeout(finish, timeoutMs);
      peer.addEventListener("icegatheringstatechange", onStateChange);
    });
  }

  function normalizeIceServers(value) {
    const candidates = value?.iceServers || value?.IceServers || [];
    if (!Array.isArray(candidates)) return [];
    return candidates.map(server => ({
      urls: server?.urls || server?.Urls || [],
      username: server?.username || server?.Username || undefined,
      credential: server?.credential || server?.Credential || undefined,
    })).filter(server => Array.isArray(server.urls) ? server.urls.length > 0 : Boolean(server.urls));
  }

  async function loadIceServers() {
    const result = await fetchJsonOrThrow("/api/stream/ice");
    const iceServers = normalizeIceServers(result);
    const turnAvailable = Boolean(result?.turnAvailable ?? result?.TurnAvailable);
    const warning = String(result?.warning || result?.Warning || "");
    return { iceServers, turnAvailable, warning };
  }

  async function connectRtcVideo(generation) {
    if (generation !== connectGeneration) return false;
    if (!window.isSecureContext && !isLoopbackHost()) throw new Error(tLegacy("WebRTC 需要 HTTPS"));

    const ice = await loadIceServers();
    if (generation !== connectGeneration) return false;
    const peer = new RTCPeerConnection({ iceServers: ice.iceServers });
    rtcPeer = peer;
    rtcActive = true;
    selectedPath = ice.turnAvailable ? "direct-or-turn" : "direct-stun";
    report("webrtc", "negotiating", { path: selectedPath, reason: ice.warning || "" });
    elements.screen.hidden = true;
    elements.rtcScreen.hidden = false;
    elements.rtcScreen.onloadedmetadata = () => {
      applyRotation();
      elements.rtcScreen.play().catch(() => {});
      setStatus(tLegacy("WebRTC H.264 已連線"), true);
    };
    peer.ontrack = event => {
      if (generation !== connectGeneration || rtcPeer !== peer) return;
      const stream = event.streams?.[0] || new MediaStream([event.track]);
      elements.rtcScreen.srcObject = stream;
      elements.rtcScreen.play().catch(() => {});
      tuneVideoReceiver(event.receiver);
      startRtcStats(peer, generation);
    };
    peer.onconnectionstatechange = () => {
      if (generation !== connectGeneration || rtcPeer !== peer) return;
      report("webrtc", peer.connectionState, { path: selectedPath });
      if (peer.connectionState === "connected") {
        clearRtcRecoveryTimers();
        webrtcRetryScheduler.cancel();
        webrtcCooldownUntil = 0;
        rtcStableTimer = setRecoveryTimer(() => {
          rtcStableTimer = null;
          if (generation === connectGeneration && rtcPeer === peer && peer.connectionState === "connected") {
            rtcSessionRebuildAttempts = 0;
          }
        }, WEBRTC_REBUILD_STABLE_MS);
        return;
      }
      if (peer.connectionState === "disconnected" || peer.connectionState === "failed") {
        scheduleRtcRecovery(peer, generation, peer.connectionState);
        return;
      }
      if (peer.connectionState === "closed") {
        void rebuildRtcSession(peer, generation, "connection-closed");
      }
    };
    peer.oniceconnectionstatechange = () => {
      if (generation !== connectGeneration || rtcPeer !== peer) return;
      report("ice", peer.iceConnectionState, { path: selectedPath });
      if (peer.iceConnectionState === "failed") {
        scheduleRtcRecovery(peer, generation, "ice-failed");
      }
    };

    peer.addTransceiver("video", { direction: "recvonly" });
    await negotiateRtc(peer, generation, false);
    return true;
  }

  async function negotiateRtc(peer, generation, iceRestart) {
    const offer = await peer.createOffer(iceRestart ? { iceRestart: true } : undefined);
    if (generation !== connectGeneration || rtcPeer !== peer || peer.signalingState === "closed") return false;
    await peer.setLocalDescription(offer);
    await waitForIceGatheringComplete(peer);
    if (generation !== connectGeneration || rtcPeer !== peer || peer.signalingState === "closed") return false;

    const settings = getStreamSettings();
    const receiverMaxBitrateKbps = estimateReceiverMaxBitrateKbps(settings);
    const playoutDelayMs = normalizePlayoutDelayMs(settings.playoutDelayMs);
    const answer = await fetchJsonOrThrow("/api/stream/webrtc/offer", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        sdp: peer.localDescription?.sdp || offer.sdp,
        deviceName: getSelectedDisplayName(),
        fps: settings.fps,
        quality: settings.quality,
        receiverMaxBitrateKbps,
        playoutDelayMs
      })
    });
    if (generation !== connectGeneration || rtcPeer !== peer || peer.signalingState === "closed") return false;
    await peer.setRemoteDescription({
      type: answer.Type || answer.type || "answer",
      sdp: answer.Sdp || answer.sdp || ""
    });
    scheduleInitialConnectRecovery(peer, generation);
    return true;
  }

  function scheduleInitialConnectRecovery(peer, generation) {
    if (generation !== connectGeneration || rtcPeer !== peer || peer.connectionState === "connected") return;
    if (initialConnectTimer) clearRecoveryTimer(initialConnectTimer);

    initialConnectDeadline = getNow() + WEBRTC_INITIAL_CONNECT_TIMEOUT_MS;
    const tick = () => {
      initialConnectTimer = null;
      if (generation !== connectGeneration || rtcPeer !== peer) return;
      if (peer.connectionState === "connected") {
        initialConnectDeadline = 0;
        return;
      }

      const remainingMs = initialConnectDeadline - getNow();
      if (remainingMs <= 0) {
        initialConnectDeadline = 0;
        report("webrtc", "initial-connect-timeout", {
          path: selectedPath,
          reason: "initial-connect-timeout",
        }, true);
        void rebuildRtcSession(peer, generation, "initial-connect-timeout");
        return;
      }

      const remainingSeconds = Math.ceil(remainingMs / 1000);
      setStatus(`${tLegacy("正在建立 WebRTC 連線")} · ${remainingSeconds}s ${tLegacy("後自動重試")}`, false);
      initialConnectTimer = setRecoveryTimer(tick, Math.min(1000, remainingMs));
    };

    tick();
  }

  function scheduleRtcRecovery(peer, generation, reason) {
    if (disconnectTimer || rebuildingRtcSession || generation !== connectGeneration || rtcPeer !== peer) return;
    const waitSeconds = Math.round(WEBRTC_DISCONNECT_GRACE_MS / 1000);
    setStatus(`${tLegacy("WebRTC 路徑中斷，保留連線並於")}${waitSeconds}s ${tLegacy("後重建串流")}`, false);
    report("webrtc", "recovering", { path: selectedPath, reason });
    disconnectTimer = setRecoveryTimer(() => {
      disconnectTimer = null;
      if (generation !== connectGeneration || rtcPeer !== peer || peer.connectionState === "connected") return;
      void rebuildRtcSession(peer, generation, reason);
    }, WEBRTC_DISCONNECT_GRACE_MS);
  }

  async function rebuildRtcSession(peer, generation, reason) {
    if (rebuildingRtcSession || generation !== connectGeneration || rtcPeer !== peer) return;
    if (rtcSessionRebuildAttempts >= WEBRTC_SESSION_REBUILD_LIMIT) {
      fallbackToJpeg(generation, tLegacy("WebRTC 串流重建仍失敗，保持 JPEG 穩定串流"));
      return;
    }

    rebuildingRtcSession = true;
    rtcSessionRebuildAttempts += 1;
    const attempt = rtcSessionRebuildAttempts;
    report("webrtc", "session-rebuild", {
      path: selectedPath,
      reason: `${reason || "connection-lost"};attempt=${attempt}`,
    }, true);
    setStatus(`${tLegacy("WebRTC 串流已中斷，正在建立全新連線")} (${attempt}/${WEBRTC_SESSION_REBUILD_LIMIT})`, false);
    closeRtcStream(false);
    try {
      await connect({ preserveRecoveryBudget: true });
    } finally {
      rebuildingRtcSession = false;
    }
  }

  function fallbackToJpeg(generation, reason) {
    if (generation !== connectGeneration) return;
    const mode = transportMode();
    if (mode !== "jpeg") {
      webrtcCooldownUntil = getNow() + webRtcCooldownMs(mode);
    }
    fallbackReason = reason || tLegacy("WebRTC 暫時不可用");
    report("webrtc", "fallback", { path: "jpeg", reason: fallbackReason }, true);
    closeRtcStream(false);
    if (mode !== "jpeg") {
      webrtcRetryScheduler.schedule(generation, webrtcCooldownUntil);
    } else {
      webrtcRetryScheduler.cancel();
    }
    setStatus(`${fallbackReason} · ${tLegacy("JPEG 備援")}`, false);
    connectJpegVideo(fallbackReason);
  }

  function resolveSelectedPath(reports) {
    const selectedPair = resolveSelectedCandidatePair(reports);
    if (!selectedPair || typeof selectedPair !== "object") return "";
    let localCandidate = null;
    reports.forEach(report => {
      if (report.type === "local-candidate" && report.id === selectedPair.localCandidateId) localCandidate = report;
    });
    const candidateType = String(localCandidate?.candidateType || "").toLowerCase();
    if (candidateType === "relay") return "turn-relay";
    if (candidateType === "srflx" || candidateType === "prflx") return "direct-stun";
    if (candidateType === "host") return "direct-lan";
    return "direct";
  }

  function resolveSelectedCandidatePair(reports) {
    let selectedPair = null;
    let selectedPairId = "";
    reports.forEach(report => {
      if (report.type === "transport" && report.selectedCandidatePairId) selectedPairId = report.selectedCandidatePairId;
    });
    reports.forEach(report => {
      if (report.type !== "candidate-pair") return;
      if (report.id === selectedPairId || (report.nominated && report.state === "succeeded")) {
        selectedPair = report;
      }
    });
    return selectedPair;
  }

  function resolveVideoRttSeconds(reports, selectedPair) {
    const selectedRtt = Number(selectedPair?.currentRoundTripTime);
    if (Number.isFinite(selectedRtt) && selectedRtt >= 0) return selectedRtt;
    let remoteRtt = null;
    reports.forEach(report => {
      if (report.type !== "remote-inbound-rtp" || (report.kind !== "video" && report.mediaType !== "video")) return;
      const value = Number(report.roundTripTime);
      if (Number.isFinite(value) && value >= 0) remoteRtt = value;
    });
    return remoteRtt;
  }

  function pathLabel(path) {
    if (path === "turn-relay") return "TURN relay";
    if (path === "direct-stun") return "WebRTC direct";
    if (path === "direct-lan") return "WebRTC LAN";
    return "WebRTC H.264";
  }

  function startRtcStats(peer, generation) {
    if (statsTimer) clearInterval(statsTimer);
    let previous = null;
    statsTimer = setInterval(async () => {
      if (generation !== connectGeneration || rtcPeer !== peer || peer.connectionState === "closed") return;
      try {
        const reports = await peer.getStats();
        const path = resolveSelectedPath(reports);
        if (path && path !== selectedPath) {
          selectedPath = path;
          report("transport", "selected", { path }, true);
        }
        let inbound = null;
        reports.forEach(report => {
          if (report.type === "inbound-rtp" && (report.kind === "video" || report.mediaType === "video")) inbound = report;
        });
        if (!inbound) {
          previous = null;
          return;
        }
        const now = Number(inbound.timestamp || performance.now());
        const selectedPair = resolveSelectedCandidatePair(reports);
        const stats = calculateInboundVideoStats(
          inbound,
          previous,
          now,
          resolveVideoRttSeconds(reports, selectedPair));
        if (stats.interval) {
          try { onStreamStats({ ...stats.interval, path: selectedPath }); } catch { }
        }
        const fullscreenDisplay = globalThis.document?.body?.classList?.contains("viewer-fullscreen");
        if (stats.interval && !fullscreenDisplay) {
          const interval = stats.interval;
          const statusParts = [pathLabel(selectedPath)];
          if (interval.fps !== null) statusParts.push(`${Math.max(0, interval.fps).toFixed(0)}fps`);
          if (interval.bitrateMbps !== null) statusParts.push(`${Math.max(0, interval.bitrateMbps).toFixed(1)}Mbps`);
          if (interval.width !== null && interval.height !== null) statusParts.push(`${interval.width}×${interval.height}`);
          if (interval.rttMs !== null) statusParts.push(`RTT ${interval.rttMs.toFixed(0)}ms`);
          if (interval.jitterMs !== null) statusParts.push(`jitter ${interval.jitterMs.toFixed(0)}ms`);
          if (interval.bufferMs !== null) statusParts.push(`buffer ${interval.bufferMs.toFixed(0)}ms`);
          if (interval.decodeMs !== null) statusParts.push(`decode ${interval.decodeMs.toFixed(1)}ms`);
          if (interval.dropped !== null && interval.dropped > 0) statusParts.push(`drop ${interval.dropped}`);
          if (interval.packetsLost !== null && interval.packetsLost > 0) statusParts.push(`loss ${interval.packetsLost}`);
          if (interval.nack !== null && interval.nack > 0) statusParts.push(`nack ${interval.nack}`);
          if (interval.pli !== null && interval.pli > 0) statusParts.push(`pli ${interval.pli}`);
          setStatus(
            `${tLegacy("影像已連線")} · ${pathLabel(selectedPath)}`,
            true,
            statusParts.join(" · "));
        }
        previous = stats.snapshot;
      } catch { }
    }, 2500);
  }

  function scheduleJpegReconnect(socket) {
    if (jpegReconnectTimer || videoSocket !== socket) return;
    const delay = Math.min(JPEG_RECONNECT_MAX_MS, 1000 * (2 ** Math.min(jpegReconnectAttempts, 4)));
    jpegReconnectAttempts += 1;
    setStatus(`${tLegacy("JPEG 重新連線中")} (${Math.ceil(delay / 1000)}s)`, false);
    report("jpeg", "reconnecting", { path: "jpeg", reason: `retry-${jpegReconnectAttempts}` });
    jpegReconnectTimer = setTimeout(() => {
      jpegReconnectTimer = null;
      if (videoSocket !== socket && videoSocket !== null) return;
      connectJpegVideo(fallbackReason || tLegacy("JPEG 備援"));
    }, delay);
  }

  function connectJpegVideo(reason = "") {
    fallbackReason = reason || fallbackReason;
    clearJpegReconnectTimer();
    if (videoSocket) {
      const current = videoSocket;
      videoSocket = null;
      try { current.close(); } catch { }
    }
    elements.screen.hidden = false;
    applyRotation();
    const params = appendDeviceToken(new URLSearchParams({
      deviceName: getSelectedDisplayName(),
      fps: getStreamSettings().fps,
      quality: getStreamSettings().quality
    }));
    const socket = new WebSocket(`${getWsBase()}/ws/display?${params.toString()}`);
    videoSocket = socket;
    socket.binaryType = "blob";
    resetJpegStats();
    socket.onopen = () => {
      if (videoSocket !== socket) return;
      jpegReconnectAttempts = 0;
      report("jpeg", "connected", { path: "jpeg", reason: fallbackReason });
      setStatus(fallbackReason ? `${fallbackReason} · ${tLegacy("JPEG 備援")}` : tLegacy("影像已連線"), true);
    };
    socket.onclose = () => {
      if (videoSocket !== socket) return;
      scheduleJpegReconnect(socket);
    };
    socket.onerror = () => {
      if (videoSocket !== socket) return;
      setStatus(tLegacy("影像連線錯誤"), false);
      report("jpeg", "error", { path: "jpeg" });
    };
    socket.onmessage = event => {
      pendingJpegFrame = event.data;
      presentPendingJpegFrame();
      recordJpegFrame(event.data.size, fallbackReason);
      if (getStreamSettings().rotationIsAuto) requestAnimationFrame(applyRotation);
    };
  }

  return {
    connect,
    closeJpegStream,
    closeRtcStream,
    getActiveStreamElement,
  };
}
