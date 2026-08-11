function readNonNegative(value) {
  if (value === null || value === undefined) return null;
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? number : null;
}

function readPositive(value) {
  const number = readNonNegative(value);
  return number !== null && number > 0 ? number : null;
}

function counterDelta(currentValue, previousValue) {
  const current = readNonNegative(currentValue);
  if (current === null) return null;
  const previous = readNonNegative(previousValue);
  if (previous === null || current < previous) return current;
  return Math.max(0, current - previous);
}

function snapshotInboundVideoStats(inbound, fallbackTimestamp) {
  return {
    time: readNonNegative(inbound.timestamp) ?? readNonNegative(fallbackTimestamp) ?? 0,
    frames: readNonNegative(inbound.framesDecoded),
    bytes: readNonNegative(inbound.bytesReceived),
    dropped: readNonNegative(inbound.framesDropped),
    jitterBufferDelay: readNonNegative(inbound.jitterBufferDelay),
    jitterBufferEmitted: readNonNegative(inbound.jitterBufferEmittedCount),
    totalDecodeTime: readNonNegative(inbound.totalDecodeTime),
    packetsLost: readNonNegative(inbound.packetsLost),
    nackCount: readNonNegative(inbound.nackCount),
    pliCount: readNonNegative(inbound.pliCount),
  };
}

/**
 * Converts cumulative inbound-rtp counters into a recent-interval snapshot.
 * Missing fields and browser counter resets are treated as unknown/reset,
 * never as negative or NaN values.
 */
export function calculateInboundVideoStats(inbound, previous, fallbackTimestamp = 0, rttSeconds = null) {
  if (!inbound || typeof inbound !== "object") {
    return { snapshot: null, interval: null };
  }

  const snapshot = snapshotInboundVideoStats(inbound, fallbackTimestamp);
  if (!previous || typeof previous !== "object") {
    return { snapshot, interval: null };
  }

  const previousTime = readNonNegative(previous.time);
  const elapsedMilliseconds = previousTime === null ? 0 : snapshot.time - previousTime;
  const seconds = Number.isFinite(elapsedMilliseconds) && elapsedMilliseconds > 0
    ? Math.max(0.001, elapsedMilliseconds / 1000)
    : 1;
  const frames = counterDelta(snapshot.frames, previous.frames);
  const bytes = counterDelta(snapshot.bytes, previous.bytes);
  const dropped = counterDelta(snapshot.dropped, previous.dropped);
  const jitterBufferDelay = counterDelta(snapshot.jitterBufferDelay, previous.jitterBufferDelay);
  const jitterBufferEmitted = counterDelta(snapshot.jitterBufferEmitted, previous.jitterBufferEmitted);
  const totalDecodeTime = counterDelta(snapshot.totalDecodeTime, previous.totalDecodeTime);
  const packetsLost = counterDelta(snapshot.packetsLost, previous.packetsLost);
  const nack = counterDelta(snapshot.nackCount, previous.nackCount);
  const pli = counterDelta(snapshot.pliCount, previous.pliCount);
  const jitter = readNonNegative(inbound.jitter);
  const rtt = readNonNegative(rttSeconds);

  return {
    snapshot,
    interval: {
      seconds,
      fps: frames === null ? null : frames / seconds,
      bitrateMbps: bytes === null ? null : bytes * 8 / seconds / 1e6,
      dropped,
      jitterMs: jitter === null ? null : jitter * 1000,
      bufferMs: jitterBufferDelay !== null && jitterBufferEmitted !== null && jitterBufferEmitted > 0
        ? Math.max(0, jitterBufferDelay / jitterBufferEmitted * 1000)
        : null,
      decodeMs: totalDecodeTime !== null && frames !== null && frames > 0
        ? Math.max(0, totalDecodeTime / frames * 1000)
        : null,
      width: readPositive(inbound.frameWidth),
      height: readPositive(inbound.frameHeight),
      rttMs: rtt === null ? null : rtt * 1000,
      packetsLost,
      nack,
      pli,
    },
  };
}

export function tuneVideoReceiver(receiver) {
  if (!receiver) return;

  // These hints are optional and differ across Safari/Chromium releases.
  // Feature-detect each field so a newer browser can reduce latency without
  // breaking older WebRTC implementations.
  try {
    if ("jitterBufferTarget" in receiver) receiver.jitterBufferTarget = 0;
    if ("playoutDelayHint" in receiver) receiver.playoutDelayHint = 0;
  } catch {
    // Receiver hints are best-effort; media should continue with defaults.
  }
}
