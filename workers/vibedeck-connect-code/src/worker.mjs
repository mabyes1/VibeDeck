const DEFAULT_TTL_SECONDS = 600;
const MIN_TTL_SECONDS = 60;
const MAX_TTL_SECONDS = 900;
const CODE_PATTERN = /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$/;
const INSTALLATION_PATTERN = /^vd-[0-9a-f]{16}$/;
const PROVISIONING_SECRET_PATTERN = /^[A-Za-z0-9_-]{43}$/;
const CLOUDFLARE_API_BASE = "https://api.cloudflare.com/client/v4";
const PROOF_CHALLENGE_TTL_MS = 600_000;
const PROOF_ALGORITHM = "sha256-leading-zero-bits";
const DEFAULT_PROOF_DIFFICULTY_BITS = 20;
const VERIFY_TARGET_TIMEOUT_MS = 5_000;
const DAY_MS = 86_400_000;
const REAP_INTERVAL_MS = 6 * 3_600_000;
const REAP_UNCLAIMED_AFTER_MS = 48 * 3_600_000;
const REMEMBERED_HOST_COOKIE = "vibedeck-default-host";
const REMEMBERED_HOST_MAX_AGE_SECONDS = 365 * 24 * 60 * 60;
const LANDING_SUBMIT_GUARD = 'document.addEventListener("submit",e=>{const f=e.target;if(!f.matches("form[data-once]"))return;if(f.dataset.submitting==="1"){e.preventDefault();return}f.dataset.submitting="1";f.querySelector("button[type=submit]")?.setAttribute("disabled","")});';
const LANDING_SUBMIT_GUARD_CSP_HASH = "'sha256-bfXKPBvv3fl+jHsvWGd3kmxKB0McbscPTDLop6BifXY='";

const translations = {
  "zh-Hant": {
    title: "連接 VibeDeck",
    eyebrow: "VIBEDECK · 私人入口",
    intro: "第一次連線可掃描 Windows 電腦上的 QR Code，或在這裡輸入一次性連線碼。完成後，這個瀏覽器只要開 vibedeck.pp.ua 就會回到自己的 VibeDeck。",
    label: "8 位連線碼",
    placeholder: "例如 ABCD-EFGH",
    submit: "開啟這台電腦",
    note: "這裡只記住這台電腦的地址，不會取得配對權限；新裝置仍必須由電腦端允許。",
    redirecting: "正在開啟你的 VibeDeck…",
    continue: "若未自動開啟，請點這裡繼續。",
    invalid: "連線碼無效、已使用或已過期。請回 Windows 電腦產生新的代碼。"
  },
  en: {
    title: "Connect VibeDeck",
    eyebrow: "VIBEDECK · PRIVATE ENTRY",
    intro: "For the first connection, scan the QR code on the Windows PC or enter its one-time code here. After that, this browser can return to its VibeDeck by opening vibedeck.pp.ua.",
    label: "8-character connection code",
    placeholder: "For example ABCD-EFGH",
    submit: "Open this PC",
    note: "This page remembers only the PC address. It grants no pairing permission; new devices still require approval on the PC.",
    redirecting: "Opening your VibeDeck…",
    continue: "If it does not open automatically, continue here.",
    invalid: "This connection code is invalid, used, or expired. Create a new code on the Windows PC."
  },
  ja: {
    title: "VibeDeck に接続",
    eyebrow: "VIBEDECK · プライベート入口",
    intro: "初回は Windows PC の QR コードを読み取るか、一時接続コードをここに入力します。次回からは、このブラウザーで vibedeck.pp.ua を開くだけで自分の VibeDeck に戻れます。",
    label: "8 文字の接続コード",
    placeholder: "例: ABCD-EFGH",
    submit: "この PC を開く",
    note: "ここで記憶するのは PC のアドレスだけです。ペアリング権限は付与されず、新しいデバイスは引き続き PC 側の許可が必要です。",
    redirecting: "あなたの VibeDeck を開いています…",
    continue: "自動的に開かない場合は、ここを選択してください。",
    invalid: "接続コードが無効、使用済み、または期限切れです。Windows PC で新しいコードを作成してください。"
  }
};

export class ConnectionCodeBroker {
  constructor(state) {
    this.state = state;
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (request.method === "POST" && url.pathname === "/register") {
      const payload = await request.json();
      const code = normalizeCode(payload.code);
      const targetUrl = String(payload.targetUrl || "");
      const expiresAt = Number(payload.expiresAt || 0);
      if (!code || !targetUrl || !Number.isFinite(expiresAt) || expiresAt <= Date.now()) {
        return json({ error: "invalid_request" }, 400);
      }

      const key = `code:${code}`;
      const existing = await this.state.storage.get(key);
      if (existing && Number(existing.expiresAt || 0) > Date.now()) {
        return json({ error: "code_collision" }, 409);
      }

      await this.state.storage.put(key, { targetUrl, expiresAt });
      return json({ code, expiresAt });
    }

    if (request.method === "POST" && url.pathname === "/resolve") {
      const payload = await request.json();
      const code = normalizeCode(payload.code);
      if (!code) return json({ error: "invalid_code" }, 404);

      const key = `code:${code}`;
      const stored = await this.state.storage.get(key);
      if (!stored || Number(stored.expiresAt || 0) <= Date.now()) {
        await this.state.storage.delete(key);
        return json({ error: "code_unavailable" }, 404);
      }

      await this.state.storage.delete(key);
      return json({ targetUrl: stored.targetUrl });
    }

    if (request.method === "POST" && url.pathname === "/rate") {
      const payload = await request.json();
      const scope = String(payload.scope || "").replace(/[^a-z0-9-]/gi, "").slice(0, 32);
      const subject = String(payload.subject || "").replace(/[^a-f0-9]/gi, "").slice(0, 64);
      const limit = Math.min(1000, Math.max(1, Number(payload.limit || 1)));
      const windowMilliseconds = Math.min(86_400_000, Math.max(10_000, Number(payload.windowMilliseconds || 60_000)));
      if (!scope || !subject) return json({ error: "invalid_rate_key" }, 400);

      const key = `rate:${scope}:${subject}`;
      const now = Date.now();
      let rate = await this.state.storage.get(key);
      if (!rate || Number(rate.expiresAt || 0) <= now) {
        rate = { count: 0, expiresAt: now + windowMilliseconds };
      }
      if (Number(rate.count || 0) >= limit) {
        return json({ error: "rate_limited", retryAfter: Math.max(1, Math.ceil((rate.expiresAt - now) / 1000)) }, 429);
      }

      rate.count = Number(rate.count || 0) + 1;
      await this.state.storage.put(key, rate);
      return json({ allowed: true, remaining: Math.max(0, limit - rate.count), expiresAt: rate.expiresAt });
    }

    return json({ error: "not_found" }, 404);
  }
}

export class InstallationBroker {
  constructor(state, env) {
    this.state = state;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (request.method === "POST" && url.pathname === "/verify") {
      return this.verifyInstallation(await request.json());
    }
    if (request.method !== "POST" || url.pathname !== "/provision") {
      return json({ error: "not_found" }, 404);
    }

    const payload = await request.json();
    const installationId = normalizeInstallationId(payload.installationId);
    const secretHash = String(payload.secretHash || "").toLowerCase();
    const ipHash = String(payload.ipHash || "").toLowerCase();
    if (!installationId || !/^[a-f0-9]{64}$/.test(secretHash) || !/^[a-f0-9]{64}$/.test(ipHash)) {
      return json({ error: "invalid_request" }, 400);
    }
    const prefixHashCandidate = String(payload.prefixHash || "").toLowerCase();
    const prefixHash = /^[a-f0-9]{64}$/.test(prefixHashCandidate) ? prefixHashCandidate : "";
    const asn = String(payload.asn || "").replace(/[^0-9]/g, "").slice(0, 10);
    const proofVerified = payload.proofVerified === true;

    const key = `installation:${installationId}`;
    const existing = await this.state.storage.get(key);
    if (existing && !timingSafeEqual(String(existing.secretHash || ""), secretHash)) {
      return json({ error: "installation_claimed" }, 409);
    }

    if (!existing) {
      const limited = await this.enforceProvisionLimits(ipHash, prefixHash, asn, proofVerified);
      if (limited) return limited;
    }

    try {
      const provisioned = await ensureCloudflareTunnel(this.env, installationId, existing?.tunnelId || "");
      const now = new Date().toISOString();
      await this.state.storage.put(key, {
        secretHash,
        tunnelId: provisioned.tunnelId,
        publicUrl: provisioned.publicUrl,
        // Pre-upgrade records have no createdAt; treat any re-provision by the
        // rightful secret holder as proof of life so upgrades are never reaped.
        createdAt: existing?.createdAt || now,
        confirmedAt: existing ? (existing.confirmedAt || now) : "",
        updatedAt: now
      });
      await this.ensureReapAlarm();
      return json({ installationId, ...provisioned }, existing ? 200 : 201);
    } catch (error) {
      return json({ error: error?.code || "cloudflare_provisioning_failed" }, 503);
    }
  }

  async verifyInstallation(payload) {
    const installationId = normalizeInstallationId(payload?.installationId);
    if (!installationId) return json({ exists: false, secretMatches: false });
    const key = `installation:${installationId}`;
    const record = await this.state.storage.get(key);
    if (!record) return json({ exists: false, secretMatches: false });
    const secretHash = String(payload?.secretHash || "").toLowerCase();
    const secretMatches = /^[a-f0-9]{64}$/.test(secretHash) &&
      timingSafeEqual(String(record.secretHash || ""), secretHash);
    if (secretMatches && !record.confirmedAt) {
      await this.state.storage.put(key, { ...record, confirmedAt: new Date().toISOString() });
    }
    return json({ exists: true, secretMatches });
  }

  // Rate limiting strategy: abuse pressure is absorbed by narrow dimensions
  // (IP, network prefix, ASN, and a tiny budget for legacy proofless clients)
  // so a single attacker can no longer exhaust one global counter and deny
  // service to every new installation. The global counter only ALERTS at the
  // alarm threshold and blocks at a far higher hard capacity ceiling.
  async enforceProvisionLimits(ipHash, prefixHash, asn, proofVerified) {
    if (!proofVerified) {
      // Legacy path: hosts that predate proof-of-work support. Kept working
      // for compatibility, but under a small isolated budget so saturating it
      // never affects proof-carrying clients.
      if (!(await this.consumeRate(`provision-legacy-ip:${ipHash}`, this.limitFromEnv("PROVISION_LEGACY_PER_IP", 2), DAY_MS))) {
        return json({ error: "rate_limited", scope: "legacy-ip", upgrade: "proof_of_work" }, 429);
      }
      if (!(await this.consumeRate("provision-legacy-global", this.limitFromEnv("PROVISION_LEGACY_GLOBAL_PER_DAY", 25), DAY_MS))) {
        return json({ error: "rate_limited", scope: "legacy-global", upgrade: "proof_of_work" }, 429);
      }
    }

    if (!(await this.consumeRate(`provision-ip:${ipHash}`, this.limitFromEnv("PROVISION_PER_IP", 3), DAY_MS))) {
      return json({ error: "rate_limited", scope: "ip" }, 429);
    }
    if (prefixHash && !(await this.consumeRate(`provision-prefix:${prefixHash}`, this.limitFromEnv("PROVISION_PER_PREFIX", 6), DAY_MS))) {
      return json({ error: "rate_limited", scope: "prefix" }, 429);
    }
    if (asn && !(await this.consumeRate(`provision-asn:${asn}`, this.limitFromEnv("PROVISION_PER_ASN", 20), DAY_MS))) {
      return json({ error: "rate_limited", scope: "asn" }, 429);
    }

    const globalCount = await this.incrementCounter("provision-global-count", DAY_MS);
    const alarmThreshold = this.limitFromEnv("PROVISION_GLOBAL_ALARM", 100);
    const hardCap = this.limitFromEnv("PROVISION_GLOBAL_HARD_CAP", 400);
    if (globalCount === alarmThreshold) {
      console.error(`vibedeck provisioning abuse alarm: ${globalCount} new installations within the 24h window`);
    }
    if (globalCount > hardCap) {
      console.error(`vibedeck provisioning hard capacity reached: ${globalCount} new installations within the 24h window`);
      return json({ error: "provision_capacity", scope: "global" }, 429);
    }
    return null;
  }

  limitFromEnv(name, fallback) {
    const value = Number.parseInt(String(this.env?.[name] ?? ""), 10);
    return Number.isFinite(value) && value >= 1 && value <= 1_000_000 ? value : fallback;
  }

  async consumeRate(key, limit, windowMilliseconds) {
    const now = Date.now();
    let rate = await this.state.storage.get(key);
    if (!rate || Number(rate.expiresAt || 0) <= now) {
      rate = { count: 0, expiresAt: now + windowMilliseconds };
    }
    if (Number(rate.count || 0) >= limit) return false;
    rate.count = Number(rate.count || 0) + 1;
    await this.state.storage.put(key, rate);
    return true;
  }

  async incrementCounter(key, windowMilliseconds) {
    const now = Date.now();
    let rate = await this.state.storage.get(key);
    if (!rate || Number(rate.expiresAt || 0) <= now) {
      rate = { count: 0, expiresAt: now + windowMilliseconds };
    }
    rate.count = Number(rate.count || 0) + 1;
    await this.state.storage.put(key, rate);
    return rate.count;
  }

  async ensureReapAlarm() {
    try {
      if (typeof this.state.storage.getAlarm !== "function" || typeof this.state.storage.setAlarm !== "function") return;
      const scheduled = await this.state.storage.getAlarm();
      if (!scheduled) await this.state.storage.setAlarm(Date.now() + REAP_INTERVAL_MS);
    } catch {
      // Alarm scheduling is best-effort; provisioning must not fail because of it.
    }
  }

  async alarm() {
    try {
      await this.reapUnclaimedInstallations();
    } finally {
      try {
        if (typeof this.state.storage.setAlarm === "function") {
          await this.state.storage.setAlarm(Date.now() + REAP_INTERVAL_MS);
        }
      } catch {
        // Rescheduling is best-effort.
      }
    }
  }

  async reapUnclaimedInstallations() {
    if (typeof this.state.storage.list !== "function") return;
    if (!/^[a-f0-9]{32}$/i.test(String(this.env?.CLOUDFLARE_ACCOUNT_ID || "")) ||
      String(this.env?.CLOUDFLARE_API_TOKEN || "").length < 20) {
      return;
    }
    const entries = await this.state.storage.list({ prefix: "installation:" });
    const now = Date.now();
    for (const [key, record] of entries) {
      try {
        if (record?.confirmedAt) continue;
        if (!record?.createdAt) {
          // Record predates the reaper: it belongs to an already-running
          // install from before this upgrade. Grandfather it as confirmed so
          // an existing install can never lose its tunnel or DNS record.
          await this.state.storage.put(key, { ...record, confirmedAt: new Date().toISOString() });
          continue;
        }
        const createdAt = Date.parse(record.createdAt);
        if (!Number.isFinite(createdAt) || now - createdAt < REAP_UNCLAIMED_AFTER_MS) continue;

        const tunnelId = String(record?.tunnelId || "");
        if (/^[a-f0-9-]{36}$/i.test(tunnelId)) {
          const tunnel = await cloudflareApi(this.env, `/accounts/${this.env.CLOUDFLARE_ACCOUNT_ID}/cfd_tunnel/${tunnelId}`);
          const everConnected = Boolean(tunnel?.conns_active_at) || Boolean(tunnel?.conns_inactive_at) ||
            (Array.isArray(tunnel?.connections) && tunnel.connections.length > 0) ||
            tunnel?.status === "healthy" || tunnel?.status === "degraded";
          if (everConnected) {
            await this.state.storage.put(key, { ...record, confirmedAt: new Date().toISOString() });
            continue;
          }
          await cloudflareApi(this.env, `/accounts/${this.env.CLOUDFLARE_ACCOUNT_ID}/cfd_tunnel/${tunnelId}`, { method: "DELETE" });
        }

        const installationId = key.slice("installation:".length);
        const hostname = `${installationId}.${normalizeBaseDomain(this.env.PUBLIC_BASE_DOMAIN)}`;
        const zoneId = await resolveZoneId(this.env);
        const records = await cloudflareApi(this.env, `/zones/${zoneId}/dns_records?type=CNAME&name=${encodeURIComponent(hostname)}&per_page=10`);
        for (const dns of Array.isArray(records) ? records : []) {
          if (dns?.id) {
            await cloudflareApi(this.env, `/zones/${zoneId}/dns_records/${dns.id}`, { method: "DELETE" });
          }
        }
        await this.state.storage.delete(key);
      } catch {
        // Leave the record in place; the next alarm cycle retries.
      }
    }
  }
}

export default {
  fetch(request, env) {
    return handleRequest(request, env);
  }
};

export async function handleRequest(request, env) {
  const url = new URL(request.url);
  if (url.pathname === "/api/installations/provision" && request.method === "POST") {
    return provisionInstallation(request, env);
  }
  if (url.pathname === "/api/connect-codes" && request.method === "POST") {
    return registerConnectionCode(request, env);
  }
  if (url.pathname === "/connect" && request.method === "POST") {
    return resolveConnectionCode(request, env);
  }
  const rememberedRoute = url.pathname.match(/^\/to\/(vd-[0-9a-f]{16})\/?$/i);
  if (rememberedRoute && request.method === "GET") {
    return rememberHostAndRedirect(request, env, rememberedRoute[1], "qr");
  }
  if (url.pathname === "/" && (request.method === "GET" || request.method === "HEAD")) {
    if (request.method === "GET" && url.searchParams.get("forget") === "1") {
      const response = landingPage(request, env);
      response.headers.set("set-cookie", clearRememberedHostCookie());
      return response;
    }
    if (request.method === "GET" && url.searchParams.get("connect") !== "1") {
      const rememberedInstallationId = readRememberedHost(request);
      if (rememberedInstallationId) {
        return rememberHostAndRedirect(request, env, rememberedInstallationId, "remembered-host", false);
      }
    }
    return landingPage(request, env);
  }
  return new Response("Not found", { status: 404, headers: securityHeaders("text/plain; charset=utf-8") });
}

async function registerConnectionCode(request, env) {
  const rateLimited = await enforceRateLimit(request, env, "connect-register", 30, 60_000);
  if (rateLimited) return rateLimited;
  if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json")) {
    return json({ error: "json_required" }, 415);
  }

  let payload;
  try {
    payload = await request.json();
  } catch {
    return json({ error: "invalid_json" }, 400);
  }

  const code = normalizeCode(payload.code);
  const targetUrl = normalizeTargetUrl(payload.publicUrl, env.PUBLIC_BASE_DOMAIN);
  if (!code || !targetUrl) {
    return json({ error: "invalid_request" }, 400);
  }

  // A connection code is only an address-routing hint. It never grants Host
  // trust or pairing permission, so installation ownership is intentionally
  // not part of this flow. The target is still constrained to this VibeDeck
  // zone and must answer as a live VibeDeck Host before the code is issued.
  if (!(await verifyTargetEndpoint(targetUrl))) {
    return json({ error: "unverified_endpoint" }, 422);
  }

  const expiresAt = Date.now() + getTtlMilliseconds(env);
  const response = await callBroker(env, "/register", { code, targetUrl, expiresAt });
  if (!response.ok) return response;
  return json({ code, expiresAt: new Date(expiresAt).toISOString() }, 201);
}

async function resolveConnectionCode(request, env) {
  const rateLimited = await enforceRateLimit(request, env, "connect-resolve", 20, 60_000);
  if (rateLimited) {
    const locale = localeFor(request, new URL(request.url));
    return landingPage(request, env, translations[locale].invalid, 429);
  }
  const form = await request.formData();
  const code = normalizeCode(form.get("code"));
  const locale = localeFor(request, new URL(request.url));
  if (!code) return landingPage(request, env, translations[locale].invalid, 400);

  const response = await callBroker(env, "/resolve", { code });
  if (!response.ok) return landingPage(request, env, translations[locale].invalid, 404);
  const payload = await response.json();
  const baseDomain = publicBaseDomain(env, new URL(request.url));
  const normalizedTarget = normalizeTargetUrl(payload.targetUrl, baseDomain);
  if (!normalizedTarget) return landingPage(request, env, translations[locale].invalid, 404);
  const target = new URL(normalizedTarget);
  target.pathname = "/index.html";
  target.searchParams.set("source", "connection-code");
  target.searchParams.set("autopair", "1");
  target.searchParams.set("lang", locale);
  const installationId = normalizeInstallationId(target.hostname.split(".")[0]);
  return connectionRedirectPage(locale, target.toString(), installationId);
}

async function rememberHostAndRedirect(request, env, installationIdValue, source, remember = true) {
  const installationId = normalizeInstallationId(installationIdValue);
  const requestUrl = new URL(request.url);
  const baseDomain = publicBaseDomain(env, requestUrl);
  const locale = localeFor(request, requestUrl);
  if (!installationId || !baseDomain) {
    return landingPage(request, env, translations[locale].invalid, 404);
  }

  const target = new URL(`https://${installationId}.${baseDomain}/index.html`);
  target.searchParams.set("source", source);
  target.searchParams.set("lang", locale);
  return connectionRedirectPage(locale, target.toString(), remember ? installationId : "");
}

async function provisionInstallation(request, env) {
  if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json")) {
    return json({ error: "json_required" }, 415);
  }
  if (!isProvisioningConfigured(env)) {
    return json({ error: "provisioning_not_configured" }, 503);
  }

  let payload;
  try {
    payload = await request.json();
  } catch {
    return json({ error: "invalid_json" }, 400);
  }

  const installationId = normalizeInstallationId(payload.installationId);
  const provisioningSecret = String(payload.provisioningSecret || "");
  if (!installationId || !PROVISIONING_SECRET_PATTERN.test(provisioningSecret)) {
    return json({ error: "invalid_request" }, 400);
  }

  // Proof-of-work gate: resource creation (tunnel + DNS) must cost real
  // compute, not merely a well-formatted request. Challenges are stateless,
  // HMAC-signed, bound to the installation id, and short-lived.
  const difficulty = proofDifficulty(env);
  const requireProof = String(env.PROVISION_REQUIRE_PROOF || "0") === "1";
  let proofVerified = false;
  if (payload.proof && typeof payload.proof === "object") {
    if (!(await verifyProvisioningProof(env, installationId, payload.proof, difficulty))) {
      return json({ error: "invalid_proof", ...(await challengeFields(env, installationId, difficulty)) }, 403);
    }
    proofVerified = true;
  } else if (payload.requestChallenge === true) {
    return json({ error: "proof_of_work_required", ...(await challengeFields(env, installationId, difficulty)) }, 428);
  } else if (requireProof) {
    // An installation already claimed by this exact secret proves ownership
    // with the secret itself; proof-of-work only gates NEW installation ids.
    // Without this, flipping PROVISION_REQUIRE_PROOF would break old binaries
    // re-provisioning an existing claimed id (e.g. after losing tunnel state).
    const ownership = await verifyInstallationOwnership(env, installationId, await sha256Hex(provisioningSecret));
    if (!ownership.exists || !ownership.secretMatches) {
      return json({ error: "proof_of_work_required", ...(await challengeFields(env, installationId, difficulty)) }, 428);
    }
  }

  const address = clientAddress(request);
  const secretHash = await sha256Hex(provisioningSecret);
  const ipHash = await sha256Hex(address);
  const prefixHash = await sha256Hex(clientNetworkPrefix(address));
  const asn = String(Number(request.cf?.asn) || "").replace(/[^0-9]/g, "").slice(0, 10);
  const id = env.INSTALLATIONS.idFromName("vibedeck-installation-broker");
  const stub = env.INSTALLATIONS.get(id);
  const response = await stub.fetch("https://installation.internal/provision", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ installationId, secretHash, ipHash, prefixHash, asn, proofVerified })
  });
  if (response.status === 429 && !proofVerified) {
    // A rate-limited legacy client is told how to retry with proof so the
    // legacy budget saturating never becomes a dead end for updated hosts.
    let body = {};
    try {
      body = await response.json();
    } catch {
      body = { error: "rate_limited" };
    }
    return json({ ...body, ...(await challengeFields(env, installationId, difficulty)) }, 429);
  }
  return response;
}

async function verifyInstallationOwnership(env, installationId, secretHash) {
  try {
    const id = env.INSTALLATIONS.idFromName("vibedeck-installation-broker");
    const stub = env.INSTALLATIONS.get(id);
    const response = await stub.fetch("https://installation.internal/verify", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ installationId, secretHash })
    });
    if (!response.ok) return { exists: false, secretMatches: false };
    const payload = await response.json();
    return { exists: payload?.exists === true, secretMatches: payload?.secretMatches === true };
  } catch {
    return { exists: false, secretMatches: false };
  }
}

async function callBroker(env, path, payload) {
  const id = env.CONNECTION_CODES.idFromName("vibedeck-connection-code-broker");
  const stub = env.CONNECTION_CODES.get(id);
  return stub.fetch(`https://connection-code.internal${path}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(payload)
  });
}

async function enforceRateLimit(request, env, scope, limit, windowMilliseconds) {
  if (!env.CONNECTION_CODES) return null;
  const response = await callBroker(env, "/rate", {
    scope,
    subject: await sha256Hex(clientAddress(request)),
    limit,
    windowMilliseconds
  });
  return response.status === 429 ? response : null;
}

async function ensureCloudflareTunnel(env, installationId, knownTunnelId) {
  const hostname = `${installationId}.${normalizeBaseDomain(env.PUBLIC_BASE_DOMAIN)}`;
  const tunnelName = `vibedeck-${installationId}`;
  let tunnelId = String(knownTunnelId || "");
  let tunnelToken = "";

  if (!tunnelId) {
    const listed = await cloudflareApi(env, `/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/cfd_tunnel?name=${encodeURIComponent(tunnelName)}&is_deleted=false&per_page=10`);
    const match = Array.isArray(listed) ? listed.find(tunnel => tunnel?.name === tunnelName && tunnel?.id) : null;
    tunnelId = String(match?.id || "");
  }

  if (!tunnelId) {
    const created = await cloudflareApi(env, `/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/cfd_tunnel`, {
      method: "POST",
      body: JSON.stringify({ name: tunnelName, config_src: "cloudflare" })
    });
    tunnelId = String(created?.id || "");
    tunnelToken = String(created?.token || "");
  }
  if (!/^[a-f0-9-]{36}$/i.test(tunnelId)) throw provisioningError("invalid_tunnel_response");

  await cloudflareApi(env, `/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/cfd_tunnel/${tunnelId}/configurations`, {
    method: "PUT",
    body: JSON.stringify({
      config: {
        ingress: [
          { hostname, service: "http://127.0.0.1:5000" },
          { service: "http_status:404" }
        ]
      }
    })
  });
  await upsertTunnelDns(env, hostname, tunnelId);

  if (!tunnelToken) {
    tunnelToken = String(await cloudflareApi(env, `/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/cfd_tunnel/${tunnelId}/token`) || "");
  }
  if (tunnelToken.length < 80) throw provisioningError("invalid_tunnel_token");

  return {
    publicUrl: `https://${hostname}/`,
    tunnelId,
    tunnelToken
  };
}

async function upsertTunnelDns(env, hostname, tunnelId) {
  const zoneId = await resolveZoneId(env);
  const target = `${tunnelId}.cfargotunnel.com`;
  const records = await cloudflareApi(env, `/zones/${zoneId}/dns_records?type=CNAME&name=${encodeURIComponent(hostname)}&per_page=10`);
  const existing = Array.isArray(records) ? records[0] : null;
  const body = JSON.stringify({ type: "CNAME", name: hostname, content: target, proxied: true, ttl: 1 });
  if (existing?.id) {
    await cloudflareApi(env, `/zones/${zoneId}/dns_records/${existing.id}`, { method: "PATCH", body });
    return;
  }
  await cloudflareApi(env, `/zones/${zoneId}/dns_records`, { method: "POST", body });
}

async function resolveZoneId(env) {
  const configuredZoneId = String(env.CLOUDFLARE_ZONE_ID || "");
  if (/^[a-f0-9]{32}$/i.test(configuredZoneId)) return configuredZoneId;
  const domain = normalizeBaseDomain(env.PUBLIC_BASE_DOMAIN);
  const zones = await cloudflareApi(env, `/zones?name=${encodeURIComponent(domain)}&status=active&per_page=5`);
  const zone = Array.isArray(zones) ? zones.find(candidate => candidate?.name === domain && candidate?.id) : null;
  if (!zone?.id) throw provisioningError("cloudflare_zone_not_found");
  return zone.id;
}

async function cloudflareApi(env, path, init = {}) {
  const response = await fetch(`${CLOUDFLARE_API_BASE}${path}`, {
    ...init,
    headers: {
      authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN}`,
      "content-type": "application/json",
      ...(init.headers || {})
    }
  });
  let payload;
  try {
    payload = await response.json();
  } catch {
    throw provisioningError("cloudflare_invalid_response");
  }
  if (!response.ok || payload?.success !== true) {
    throw provisioningError("cloudflare_api_failed");
  }
  return payload.result;
}

function isProvisioningConfigured(env) {
  return Boolean(env.INSTALLATIONS &&
    /^[a-f0-9]{32}$/i.test(String(env.CLOUDFLARE_ACCOUNT_ID || "")) &&
    /^[a-f0-9]{32}$/i.test(String(env.CLOUDFLARE_ZONE_ID || "")) &&
    String(env.CLOUDFLARE_API_TOKEN || "").length >= 20 &&
    normalizeBaseDomain(env.PUBLIC_BASE_DOMAIN));
}

function provisioningError(code) {
  const error = new Error(code);
  error.code = code;
  return error;
}

// Liveness/shape check only: the target must answer JSON on the exact host,
// without redirecting elsewhere. This proves only that the remembered address
// is currently a VibeDeck endpoint; pairing/auth remains exclusively Host-side.
export async function verifyTargetEndpoint(targetUrl, timeoutMilliseconds = VERIFY_TARGET_TIMEOUT_MS) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMilliseconds);
  try {
    const endpoint = new URL("/api/connect", targetUrl);
    const response = await fetch(endpoint.toString(), {
      headers: { accept: "application/json" },
      signal: controller.signal
    });
    if (!response.ok) return false;
    if (new URL(response.url).hostname.toLowerCase() !== endpoint.hostname.toLowerCase()) return false;
    await response.json();
    return true;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

function proofDifficulty(env) {
  const candidate = Number.parseInt(String(env.PROVISION_PROOF_DIFFICULTY || ""), 10);
  return Number.isFinite(candidate) ? Math.min(30, Math.max(8, candidate)) : DEFAULT_PROOF_DIFFICULTY_BITS;
}

async function proofHmacHex(env, data) {
  const secret = String(env.PROVISION_PROOF_SECRET || "") ||
    await sha256Hex(`vibedeck-provision-proof:${env.CLOUDFLARE_API_TOKEN || ""}`);
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]);
  const signature = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(String(data)));
  return Array.from(new Uint8Array(signature), byte => byte.toString(16).padStart(2, "0")).join("");
}

export async function createProvisioningChallenge(env, installationId) {
  const nonce = Array.from(crypto.getRandomValues(new Uint8Array(16)), byte => byte.toString(16).padStart(2, "0")).join("");
  const body = base64UrlEncode(JSON.stringify({ installationId, issuedAt: Date.now(), nonce }));
  const signedPart = `v1.${body}`;
  return `${signedPart}.${await proofHmacHex(env, signedPart)}`;
}

async function challengeFields(env, installationId, difficulty) {
  return {
    challenge: await createProvisioningChallenge(env, installationId),
    difficulty,
    algorithm: PROOF_ALGORITHM,
    expiresInSeconds: Math.floor(PROOF_CHALLENGE_TTL_MS / 1000)
  };
}

export async function verifyProvisioningProof(env, installationId, proof, difficulty) {
  const challenge = String(proof?.challenge || "");
  const nonce = String(proof?.nonce || "");
  if (!/^v1\.[A-Za-z0-9_-]+\.[a-f0-9]{64}$/.test(challenge) || !/^[A-Za-z0-9_-]{1,64}$/.test(nonce)) return false;
  const lastDot = challenge.lastIndexOf(".");
  const signedPart = challenge.slice(0, lastDot);
  const signature = challenge.slice(lastDot + 1);
  if (!timingSafeEqual(await proofHmacHex(env, signedPart), signature)) return false;
  let claims;
  try {
    claims = JSON.parse(base64UrlDecode(signedPart.slice(3)));
  } catch {
    return false;
  }
  if (String(claims?.installationId || "") !== installationId) return false;
  const issuedAt = Number(claims?.issuedAt);
  if (!Number.isFinite(issuedAt) || Date.now() - issuedAt > PROOF_CHALLENGE_TTL_MS || issuedAt - Date.now() > 60_000) return false;
  const digest = await sha256Hex(`${challenge}.${nonce}`);
  return leadingZeroBits(digest) >= difficulty;
}

export function leadingZeroBits(hexString) {
  let bits = 0;
  for (const character of String(hexString || "")) {
    const value = Number.parseInt(character, 16);
    if (Number.isNaN(value)) break;
    if (value === 0) {
      bits += 4;
      continue;
    }
    bits += Math.clz32(value) - 28;
    break;
  }
  return bits;
}

function base64UrlEncode(value) {
  return btoa(String(value)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function base64UrlDecode(value) {
  const padded = String(value).replace(/-/g, "+").replace(/_/g, "/");
  return atob(padded + "=".repeat((4 - (padded.length % 4)) % 4));
}

function clientNetworkPrefix(address) {
  const candidate = String(address || "").trim().toLowerCase();
  if (candidate.includes(":")) {
    // Roughly a /48 for IPv6; coarse but sufficient as a rate dimension.
    return `v6:${candidate.split(":").slice(0, 3).join(":")}`;
  }
  const octets = candidate.split(".");
  return octets.length === 4 ? `v4:${octets.slice(0, 3).join(".")}` : `raw:${candidate}`;
}

function normalizeTargetUrl(value, baseDomain) {
  try {
    const endpoint = new URL(String(value || ""));
    const normalizedBaseDomain = String(baseDomain || "").trim().toLowerCase().replace(/^\.+|\.+$/g, "");
    const hostPattern = new RegExp(`^vd-[0-9a-f]{16}\\.${escapeRegExp(normalizedBaseDomain)}$`, "i");
    if (endpoint.protocol !== "https:" || endpoint.port || endpoint.username || endpoint.password ||
      endpoint.pathname !== "/" || endpoint.search || endpoint.hash || !hostPattern.test(endpoint.hostname)) {
      return "";
    }
    return `https://${endpoint.hostname.toLowerCase()}/`;
  } catch {
    return "";
  }
}

function normalizeInstallationId(value) {
  const installationId = String(value || "").trim().toLowerCase();
  return INSTALLATION_PATTERN.test(installationId) ? installationId : "";
}

function publicBaseDomain(env, requestUrl) {
  return normalizeBaseDomain(env?.PUBLIC_BASE_DOMAIN) || normalizeBaseDomain(requestUrl?.hostname);
}

function readRememberedHost(request) {
  const cookieHeader = request.headers.get("cookie") || "";
  for (const part of cookieHeader.split(";")) {
    const separator = part.indexOf("=");
    if (separator < 0) continue;
    const name = part.slice(0, separator).trim();
    if (name !== REMEMBERED_HOST_COOKIE) continue;
    const rawValue = part.slice(separator + 1).trim();
    try {
      return normalizeInstallationId(decodeURIComponent(rawValue));
    } catch {
      return "";
    }
  }
  return "";
}

function rememberedHostCookie(installationId) {
  const normalized = normalizeInstallationId(installationId);
  if (!normalized) return "";
  return `${REMEMBERED_HOST_COOKIE}=${encodeURIComponent(normalized)}; Path=/; Max-Age=${REMEMBERED_HOST_MAX_AGE_SECONDS}; Secure; HttpOnly; SameSite=Lax`;
}

function clearRememberedHostCookie() {
  return `${REMEMBERED_HOST_COOKIE}=; Path=/; Max-Age=0; Secure; HttpOnly; SameSite=Lax`;
}

function normalizeBaseDomain(value) {
  const domain = String(value || "").trim().toLowerCase().replace(/^\.+|\.+$/g, "");
  return /^[a-z0-9.-]+\.[a-z]{2,}$/i.test(domain) ? domain : "";
}

function clientAddress(request) {
  return String(request.headers.get("cf-connecting-ip") || request.headers.get("x-forwarded-for") || "unknown")
    .split(",")[0]
    .trim()
    .slice(0, 80);
}

async function sha256Hex(value) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(String(value || "")));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
}

function timingSafeEqual(left, right) {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

function normalizeCode(value) {
  const code = String(value || "").toUpperCase().replace(/[^A-Z0-9]/g, "");
  return CODE_PATTERN.test(code) ? code : "";
}

function getTtlMilliseconds(env) {
  const candidate = Number.parseInt(env.CODE_TTL_SECONDS || "", 10);
  const seconds = Number.isFinite(candidate)
    ? Math.min(MAX_TTL_SECONDS, Math.max(MIN_TTL_SECONDS, candidate))
    : DEFAULT_TTL_SECONDS;
  return seconds * 1000;
}

function localeFor(request, url) {
  const requested = url.searchParams.get("lang");
  if (requested === "zh-Hant" || requested === "en" || requested === "ja") return requested;
  const accepted = request.headers.get("accept-language") || "";
  if (/\bja(?:[-_,;]|$)/i.test(accepted)) return "ja";
  if (/\bzh(?:[-_,;]|$)/i.test(accepted)) return "zh-Hant";
  return "en";
}

function landingPage(request, env, error = "", status = 200) {
  const locale = localeFor(request, new URL(request.url));
  const copy = translations[locale];
  const errorBlock = error ? `<p class="error" role="alert">${escapeHtml(error)}</p>` : "";
  const page = `<!doctype html><html lang="${locale}" dir="ltr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="theme-color" content="#101820"><title>${escapeHtml(copy.title)}</title><style>body{margin:0;background:#101820;color:#edf4fb;font:18px/1.5 system-ui,sans-serif}.shell{max-width:560px;margin:0 auto;padding:48px 24px}.card{padding:28px;border:1px solid #324559;border-radius:18px;background:#17232e}.eyebrow{margin:0 0 12px;color:#8fd1ff;font-size:13px;font-weight:700;letter-spacing:.08em}.intro,.note{color:#b9c8d6}.note{font-size:14px}.error{padding:12px 14px;border:1px solid #d98b81;border-radius:10px;background:#482522;color:#ffe9e4}label{display:grid;gap:8px;margin:24px 0 14px;font-weight:700}input,button{box-sizing:border-box;width:100%;min-height:52px;border-radius:10px;font:inherit}input{border:1px solid #70869a;padding:10px 14px;background:#0d151c;color:#fff;text-transform:uppercase;letter-spacing:.1em}button{border:0;background:#dbeeff;color:#10202d;font-weight:800}button:disabled{cursor:wait;opacity:.65}</style></head><body><main class="shell"><section class="card"><p class="eyebrow">${escapeHtml(copy.eyebrow)}</p><h1>${escapeHtml(copy.title)}</h1><p class="intro">${escapeHtml(copy.intro)}</p>${errorBlock}<form method="post" action="/connect?lang=${encodeURIComponent(locale)}" data-once><label>${escapeHtml(copy.label)}<input name="code" inputmode="text" autocomplete="one-time-code" autocapitalize="characters" spellcheck="false" maxlength="9" pattern="[A-Za-z2-9-]{8,9}" placeholder="${escapeHtml(copy.placeholder)}" required autofocus></label><button type="submit">${escapeHtml(copy.submit)}</button></form><p class="note">${escapeHtml(copy.note)}</p></section></main><script>${LANDING_SUBMIT_GUARD}</script></body></html>`;
  return new Response(request.method === "HEAD" ? null : page, { status, headers: securityHeaders("text/html; charset=utf-8") });
}

function connectionRedirectPage(locale, targetUrl, installationId = "") {
  const copy = translations[locale];
  const safeTargetUrl = escapeHtml(targetUrl);
  const page = `<!doctype html><html lang="${locale}" dir="ltr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta http-equiv="refresh" content="0;url=${safeTargetUrl}"><meta name="theme-color" content="#101820"><title>${escapeHtml(copy.title)}</title><style>body{margin:0;background:#101820;color:#edf4fb;font:18px/1.5 system-ui,sans-serif}.shell{max-width:560px;margin:0 auto;padding:48px 24px}.card{padding:28px;border:1px solid #324559;border-radius:18px;background:#17232e}.eyebrow{margin:0 0 12px;color:#8fd1ff;font-size:13px;font-weight:700;letter-spacing:.08em}a{color:#10202d;display:inline-block;margin-top:14px;padding:12px 16px;border-radius:10px;background:#dbeeff;font-weight:800;text-decoration:none}</style></head><body><main class="shell"><section class="card"><p class="eyebrow">${escapeHtml(copy.eyebrow)}</p><h1>${escapeHtml(copy.redirecting)}</h1><a href="${safeTargetUrl}">${escapeHtml(copy.continue)}</a></section></main></body></html>`;
  const headers = securityHeaders("text/html; charset=utf-8");
  const cookie = rememberedHostCookie(installationId);
  if (cookie) headers["set-cookie"] = cookie;
  return new Response(page, { status: 200, headers });
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: securityHeaders("application/json; charset=utf-8") });
}

function securityHeaders(contentType) {
  return {
    "content-type": contentType,
    "cache-control": "no-store",
    "content-security-policy": `default-src 'none'; style-src 'unsafe-inline'; script-src ${LANDING_SUBMIT_GUARD_CSP_HASH}; form-action 'self'; base-uri 'none'; frame-ancestors 'none'`,
    "referrer-policy": "no-referrer",
    "x-content-type-options": "nosniff",
    "x-frame-options": "DENY"
  };
}

function escapeHtml(value) {
  return String(value || "").replace(/[&<>\"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" })[character]);
}

function escapeRegExp(value) {
  return String(value || "").replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
