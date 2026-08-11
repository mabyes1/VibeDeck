import assert from "node:assert/strict";
import test from "node:test";
import { createHash } from "node:crypto";
import { ConnectionCodeBroker, InstallationBroker, handleRequest, leadingZeroBits, verifyTargetEndpoint } from "../src/worker.mjs";

const sha256HexSync = value => createHash("sha256").update(value).digest("hex");

function solveProof(challenge, difficulty) {
  for (let nonce = 0; nonce < 5_000_000; nonce += 1) {
    if (leadingZeroBits(sha256HexSync(`${challenge}.${nonce}`)) >= difficulty) return String(nonce);
  }
  throw new Error("no proof found");
}

function makeStorage(values) {
  return {
    get: key => values.get(key),
    put: (key, value) => values.set(key, value),
    delete: key => values.delete(key),
    list: ({ prefix } = {}) => new Map([...values].filter(([key]) => !prefix || key.startsWith(prefix)))
  };
}

test("landing page is available in Traditional Chinese", async () => {
  const response = await handleRequest(new Request("https://vibedeck.pp.ua/?lang=zh-Hant"), {});
  const page = await response.text();

  assert.equal(response.status, 200);
  assert.match(page, /連接 VibeDeck/);
  assert.match(page, /form method="post" action="\/connect\?lang=zh-Hant" data-once/);
  assert.match(response.headers.get("content-security-policy"), /default-src 'none'/);
  assert.match(response.headers.get("content-security-policy"), /script-src 'sha256-bfXKPBvv3fl\+jHsvWGd3kmxKB0McbscPTDLop6BifXY='/);
});

test("landing page accepts HEAD preflight requests", async () => {
  const response = await handleRequest(new Request("https://vibedeck.pp.ua/?lang=zh-Hant", { method: "HEAD" }), {});

  assert.equal(response.status, 200);
  assert.equal(await response.text(), "");
  assert.equal(response.headers.get("cache-control"), "no-store");
});

test("registration rejects an endpoint outside the VibeDeck installation hostname", async () => {
  const response = await handleRequest(new Request("https://vibedeck.pp.ua/api/connect-codes", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: "ABCD2345", publicUrl: "https://example.test/" })
  }), { PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua" });

  assert.equal(response.status, 400);
  assert.deepEqual(await response.json(), { error: "invalid_request" });
});

test("a connection code resolves once and is then deleted", async () => {
  const values = new Map();
  const broker = new ConnectionCodeBroker({
    storage: {
      get: key => values.get(key),
      put: (key, value) => values.set(key, value),
      delete: key => values.delete(key)
    }
  });
  const environment = {
    CONNECTION_CODES: {
      idFromName: name => name,
      get: () => ({
        fetch: (input, init) => broker.fetch(input instanceof Request ? input : new Request(input, init))
      })
    }
  };
  const expiresAt = Date.now() + 60_000;
  const registered = await broker.fetch(new Request("https://connection-code.internal/register", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ code: "ABCD2345", targetUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/", expiresAt })
  }));

  assert.equal(registered.status, 200);
  const firstUse = await handleRequest(new Request("https://vibedeck.pp.ua/connect?lang=en", {
    method: "POST",
    body: new URLSearchParams({ code: "ABCD-2345" })
  }), environment);
  const redirectPage = await firstUse.text();
  assert.equal(firstUse.status, 200);
  assert.match(redirectPage, /http-equiv="refresh" content="0;url=https:\/\/vd-1234567890abcdef\.vibedeck\.pp\.ua\/index\.html\?source=connection-code&amp;autopair=1&amp;lang=en"/);
  assert.doesNotMatch(redirectPage, /[?&]eink=/);
  assert.match(redirectPage, /Opening this PC securely/);

  const replay = await handleRequest(new Request("https://vibedeck.pp.ua/connect?lang=en", {
    method: "POST",
    body: new URLSearchParams({ code: "ABCD-2345" })
  }), environment);
  assert.equal(replay.status, 404);
});

test("connection endpoints rate limit repeated requests by client address", async () => {
  const values = new Map();
  const broker = new ConnectionCodeBroker({
    storage: {
      get: key => values.get(key),
      put: (key, value) => values.set(key, value),
      delete: key => values.delete(key)
    }
  });

  for (let attempt = 0; attempt < 20; attempt += 1) {
    const response = await broker.fetch(new Request("https://connection-code.internal/rate", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ scope: "test", subject: "a".repeat(64), limit: 20, windowMilliseconds: 60_000 })
    }));
    assert.equal(response.status, 200);
  }

  const blocked = await broker.fetch(new Request("https://connection-code.internal/rate", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ scope: "test", subject: "a".repeat(64), limit: 20, windowMilliseconds: 60_000 })
  }));
  assert.equal(blocked.status, 429);
});

test("installation provisioning creates a tunnel, ingress, and DNS record", async () => {
  const originalFetch = globalThis.fetch;
  const apiCalls = [];
  const tunnelId = "11111111-2222-4333-8444-555555555555";
  const tunnelToken = `eyJ${"x".repeat(100)}`;
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(String(input));
    apiCalls.push({ method: init.method || "GET", path: `${url.pathname}${url.search}` });
    let result = {};
    if (url.pathname.endsWith("/cfd_tunnel") && (init.method || "GET") === "GET") result = [];
    if (url.pathname.endsWith("/cfd_tunnel") && init.method === "POST") result = { id: tunnelId, token: tunnelToken };
    if (url.pathname === "/client/v4/zones") result = [{ id: "f".repeat(32), name: "vibedeck.pp.ua" }];
    if (url.pathname.endsWith("/dns_records") && (init.method || "GET") === "GET") result = [];
    return new Response(JSON.stringify({ success: true, result }), {
      status: 200,
      headers: { "content-type": "application/json" }
    });
  };

  try {
    const values = new Map();
    const environment = {
      PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
      CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
      CLOUDFLARE_ZONE_ID: "f".repeat(32),
      CLOUDFLARE_API_TOKEN: "test-token-with-enough-length",
      INSTALLATIONS: null
    };
    const broker = new InstallationBroker({
      storage: {
        get: key => values.get(key),
        put: (key, value) => values.set(key, value)
      }
    }, environment);
    environment.INSTALLATIONS = {
      idFromName: name => name,
      get: () => ({
        fetch: (input, init) => broker.fetch(input instanceof Request ? input : new Request(input, init))
      })
    };

    const response = await handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
      method: "POST",
      headers: { "content-type": "application/json", "cf-connecting-ip": "203.0.113.8" },
      body: JSON.stringify({
        installationId: "vd-1234567890abcdef",
        provisioningSecret: "A".repeat(43),
        productVersion: "0.1.29",
        platform: "windows-x64"
      })
    }), environment);
    const payload = await response.json();

    assert.equal(response.status, 201);
    assert.equal(payload.publicUrl, "https://vd-1234567890abcdef.vibedeck.pp.ua/");
    assert.equal(payload.tunnelId, tunnelId);
    assert.equal(payload.tunnelToken, tunnelToken);
    assert.ok(apiCalls.some(call => call.method === "PUT" && call.path.includes("/configurations")));
    assert.ok(apiCalls.some(call => call.method === "POST" && call.path.endsWith("/dns_records")));
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("provisioning issues a signed proof-of-work challenge on request", async () => {
  const response = await handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ installationId: "vd-1234567890abcdef", provisioningSecret: "A".repeat(43), requestChallenge: true })
  }), {
    PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
    CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
    CLOUDFLARE_ZONE_ID: "f".repeat(32),
    CLOUDFLARE_API_TOKEN: "test-token-with-enough-length",
    INSTALLATIONS: {}
  });
  const payload = await response.json();

  assert.equal(response.status, 428);
  assert.equal(payload.error, "proof_of_work_required");
  assert.match(payload.challenge, /^v1\.[A-Za-z0-9_-]+\.[a-f0-9]{64}$/);
  assert.equal(payload.algorithm, "sha256-leading-zero-bits");
  assert.ok(Number(payload.difficulty) >= 8);
});

test("a solved proof-of-work challenge provisions when proof is mandatory", async () => {
  const originalFetch = globalThis.fetch;
  const tunnelId = "11111111-2222-4333-8444-555555555555";
  const tunnelToken = `eyJ${"x".repeat(100)}`;
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(String(input));
    let result = {};
    if (url.pathname.endsWith("/cfd_tunnel") && (init.method || "GET") === "GET") result = [];
    if (url.pathname.endsWith("/cfd_tunnel") && init.method === "POST") result = { id: tunnelId, token: tunnelToken };
    if (url.pathname.endsWith("/dns_records") && (init.method || "GET") === "GET") result = [];
    return new Response(JSON.stringify({ success: true, result }), {
      status: 200,
      headers: { "content-type": "application/json" }
    });
  };

  try {
    const values = new Map();
    const environment = {
      PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
      CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
      CLOUDFLARE_ZONE_ID: "f".repeat(32),
      CLOUDFLARE_API_TOKEN: "test-token-with-enough-length",
      PROVISION_REQUIRE_PROOF: "1",
      PROVISION_PROOF_DIFFICULTY: "8",
      INSTALLATIONS: null
    };
    const broker = new InstallationBroker({ storage: makeStorage(values) }, environment);
    environment.INSTALLATIONS = {
      idFromName: name => name,
      get: () => ({
        fetch: (input, init) => broker.fetch(input instanceof Request ? input : new Request(input, init))
      })
    };

    const withoutProof = await handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
      method: "POST",
      headers: { "content-type": "application/json", "cf-connecting-ip": "203.0.113.8" },
      body: JSON.stringify({ installationId: "vd-1234567890abcdef", provisioningSecret: "A".repeat(43) })
    }), environment);
    const challengePayload = await withoutProof.json();
    assert.equal(withoutProof.status, 428);
    assert.equal(challengePayload.error, "proof_of_work_required");

    const nonce = solveProof(challengePayload.challenge, Number(challengePayload.difficulty));
    const withProof = await handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
      method: "POST",
      headers: { "content-type": "application/json", "cf-connecting-ip": "203.0.113.8" },
      body: JSON.stringify({
        installationId: "vd-1234567890abcdef",
        provisioningSecret: "A".repeat(43),
        proof: { challenge: challengePayload.challenge, nonce }
      })
    }), environment);
    const provisioned = await withProof.json();

    assert.equal(withProof.status, 201);
    assert.equal(provisioned.publicUrl, "https://vd-1234567890abcdef.vibedeck.pp.ua/");
    assert.equal(provisioned.tunnelId, tunnelId);

    const badProof = await handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
      method: "POST",
      headers: { "content-type": "application/json", "cf-connecting-ip": "203.0.113.9" },
      body: JSON.stringify({
        installationId: "vd-aaaaaaaaaaaaaaaa",
        provisioningSecret: "B".repeat(43),
        proof: { challenge: challengePayload.challenge, nonce }
      })
    }), environment);
    assert.equal(badProof.status, 403);
    assert.equal((await badProof.json()).error, "invalid_proof");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("a claimed installation re-provisions with its secret even when proof is mandatory", async () => {
  const originalFetch = globalThis.fetch;
  const tunnelId = "11111111-2222-4333-8444-555555555555";
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(String(input));
    let result = {};
    if (url.pathname.endsWith(`/cfd_tunnel/${tunnelId}/token`) && (init.method || "GET") === "GET") result = "t".repeat(100);
    if (url.pathname.endsWith("/dns_records") && (init.method || "GET") === "GET") result = [];
    return new Response(JSON.stringify({ success: true, result }), {
      status: 200,
      headers: { "content-type": "application/json" }
    });
  };

  try {
    const provisioningSecret = "A".repeat(43);
    const values = new Map();
    values.set("installation:vd-1234567890abcdef", {
      secretHash: sha256HexSync(provisioningSecret),
      tunnelId,
      publicUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/",
      updatedAt: new Date().toISOString()
    });
    const environment = {
      PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
      CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
      CLOUDFLARE_ZONE_ID: "f".repeat(32),
      CLOUDFLARE_API_TOKEN: "test-token-with-enough-length",
      PROVISION_REQUIRE_PROOF: "1",
      INSTALLATIONS: null
    };
    const broker = new InstallationBroker({ storage: makeStorage(values) }, environment);
    environment.INSTALLATIONS = {
      idFromName: name => name,
      get: () => ({
        fetch: (input, init) => broker.fetch(input instanceof Request ? input : new Request(input, init))
      })
    };
    const provision = body => handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
      method: "POST",
      headers: { "content-type": "application/json", "cf-connecting-ip": "203.0.113.8" },
      body: JSON.stringify(body)
    }), environment);

    const reProvisioned = await provision({ installationId: "vd-1234567890abcdef", provisioningSecret });
    const payload = await reProvisioned.json();
    assert.equal(reProvisioned.status, 200);
    assert.equal(payload.tunnelId, tunnelId);
    assert.equal(payload.publicUrl, "https://vd-1234567890abcdef.vibedeck.pp.ua/");

    const wrongSecret = await provision({ installationId: "vd-1234567890abcdef", provisioningSecret: "B".repeat(43) });
    assert.equal(wrongSecret.status, 428);
    assert.equal((await wrongSecret.json()).error, "proof_of_work_required");

    const unclaimed = await provision({ installationId: "vd-eeeeeeeeeeeeeeee", provisioningSecret: "C".repeat(43) });
    assert.equal(unclaimed.status, 428);
    assert.equal((await unclaimed.json()).error, "proof_of_work_required");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("saturating the legacy budget cannot starve proof-verified provisioning", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(String(input));
    let result = {};
    if (url.pathname.endsWith("/cfd_tunnel") && (init.method || "GET") === "GET") result = [];
    if (url.pathname.endsWith("/cfd_tunnel") && init.method === "POST") {
      result = { id: "11111111-2222-4333-8444-555555555555", token: `eyJ${"x".repeat(100)}` };
    }
    if (url.pathname.endsWith("/dns_records") && (init.method || "GET") === "GET") result = [];
    return new Response(JSON.stringify({ success: true, result }), {
      status: 200,
      headers: { "content-type": "application/json" }
    });
  };

  try {
    const values = new Map();
    const environment = {
      PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
      CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
      CLOUDFLARE_ZONE_ID: "f".repeat(32),
      CLOUDFLARE_API_TOKEN: "test-token-with-enough-length",
      PROVISION_LEGACY_PER_IP: "1",
      PROVISION_LEGACY_GLOBAL_PER_DAY: "2"
    };
    const broker = new InstallationBroker({ storage: makeStorage(values) }, environment);
    const provision = body => broker.fetch(new Request("https://installation.internal/provision", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body)
    }));

    const first = await provision({ installationId: "vd-0000000000000001", secretHash: sha256HexSync("s1"), ipHash: "a".repeat(64), proofVerified: false });
    const second = await provision({ installationId: "vd-0000000000000002", secretHash: sha256HexSync("s2"), ipHash: "b".repeat(64), proofVerified: false });
    assert.equal(first.status, 201);
    assert.equal(second.status, 201);

    const legacyBlocked = await provision({ installationId: "vd-0000000000000003", secretHash: sha256HexSync("s3"), ipHash: "c".repeat(64), proofVerified: false });
    const legacyBlockedPayload = await legacyBlocked.json();
    assert.equal(legacyBlocked.status, 429);
    assert.equal(legacyBlockedPayload.scope, "legacy-global");
    assert.equal(legacyBlockedPayload.upgrade, "proof_of_work");

    const proofVerified = await provision({ installationId: "vd-0000000000000004", secretHash: sha256HexSync("s4"), ipHash: "d".repeat(64), proofVerified: true });
    assert.equal(proofVerified.status, 201);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("connect-code registration is bound to the installation that owns the target", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async input => ({ ok: true, url: String(input), json: async () => ({}) });

  try {
    const codeValues = new Map();
    const codeBroker = new ConnectionCodeBroker({ storage: makeStorage(codeValues) });
    const installationValues = new Map();
    const provisioningSecret = "B".repeat(43);
    installationValues.set("installation:vd-1234567890abcdef", {
      secretHash: sha256HexSync(provisioningSecret),
      tunnelId: "11111111-2222-4333-8444-555555555555",
      publicUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/",
      updatedAt: new Date().toISOString()
    });
    const installationBroker = new InstallationBroker({ storage: makeStorage(installationValues) }, {});
    const environment = {
      PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
      CONNECTION_CODES: {
        idFromName: name => name,
        get: () => ({
          fetch: (input, init) => codeBroker.fetch(input instanceof Request ? input : new Request(input, init))
        })
      },
      INSTALLATIONS: {
        idFromName: name => name,
        get: () => ({
          fetch: (input, init) => installationBroker.fetch(input instanceof Request ? input : new Request(input, init))
        })
      }
    };
    const register = body => handleRequest(new Request("https://vibedeck.pp.ua/api/connect-codes", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body)
    }), environment);

    const unknown = await register({ code: "ABCD2345", publicUrl: "https://vd-aaaaaaaaaaaaaaaa.vibedeck.pp.ua/" });
    assert.equal(unknown.status, 403);
    assert.equal((await unknown.json()).error, "unknown_installation");

    const wrongSecret = await register({
      code: "ABCD2345",
      publicUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/",
      installationId: "vd-1234567890abcdef",
      provisioningSecret: "C".repeat(43)
    });
    assert.equal(wrongSecret.status, 403);
    assert.equal((await wrongSecret.json()).error, "installation_unauthorized");

    const authorized = await register({
      code: "ABCD2345",
      publicUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/",
      installationId: "vd-1234567890abcdef",
      provisioningSecret
    });
    assert.equal(authorized.status, 201);
    assert.equal((await authorized.json()).code, "ABCD2345");
    assert.ok(installationValues.get("installation:vd-1234567890abcdef").confirmedAt);

    const legacyKnown = await register({ code: "EFGH2345", publicUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/" });
    assert.equal(legacyKnown.status, 201);

    environment.CONNECT_REGISTER_REQUIRE_AUTH = "1";
    const legacyRejected = await register({ code: "JKLM2345", publicUrl: "https://vd-1234567890abcdef.vibedeck.pp.ua/" });
    assert.equal(legacyRejected.status, 401);
    assert.equal((await legacyRejected.json()).error, "installation_auth_required");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("target endpoint verification aborts a slow target", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (input, init = {}) => new Promise((resolve, reject) => {
    init.signal?.addEventListener("abort", () => reject(new Error("aborted")));
  });

  try {
    const startedAt = Date.now();
    const verified = await verifyTargetEndpoint("https://vd-1234567890abcdef.vibedeck.pp.ua/", 50);
    assert.equal(verified, false);
    assert.ok(Date.now() - startedAt < 3_000);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("the reaper deletes never-connected tunnels and grandfathers pre-upgrade records", async () => {
  const originalFetch = globalThis.fetch;
  const apiCalls = [];
  const staleTunnelId = "22222222-3333-4444-8555-666666666666";
  globalThis.fetch = async (input, init = {}) => {
    const url = new URL(String(input));
    apiCalls.push({ method: init.method || "GET", path: `${url.pathname}${url.search}` });
    let result = {};
    if (url.pathname.endsWith(`/cfd_tunnel/${staleTunnelId}`) && (init.method || "GET") === "GET") {
      result = { id: staleTunnelId, status: "inactive", connections: [], conns_active_at: null, conns_inactive_at: null };
    }
    if (url.pathname.endsWith("/dns_records") && (init.method || "GET") === "GET") result = [{ id: "dnsrec1" }];
    return new Response(JSON.stringify({ success: true, result }), {
      status: 200,
      headers: { "content-type": "application/json" }
    });
  };

  try {
    const values = new Map();
    const staleCreatedAt = new Date(Date.now() - 3 * 86_400_000).toISOString();
    values.set("installation:vd-aaaaaaaaaaaaaaaa", {
      secretHash: "a".repeat(64),
      tunnelId: staleTunnelId,
      publicUrl: "https://vd-aaaaaaaaaaaaaaaa.vibedeck.pp.ua/",
      createdAt: staleCreatedAt,
      confirmedAt: "",
      updatedAt: staleCreatedAt
    });
    values.set("installation:vd-bbbbbbbbbbbbbbbb", {
      secretHash: "b".repeat(64),
      tunnelId: "33333333-4444-5555-8666-777777777777",
      publicUrl: "https://vd-bbbbbbbbbbbbbbbb.vibedeck.pp.ua/",
      updatedAt: staleCreatedAt
    });

    let alarmScheduled = 0;
    const storage = makeStorage(values);
    storage.setAlarm = () => { alarmScheduled += 1; };
    storage.getAlarm = () => null;
    const broker = new InstallationBroker({ storage }, {
      PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
      CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
      CLOUDFLARE_ZONE_ID: "f".repeat(32),
      CLOUDFLARE_API_TOKEN: "test-token-with-enough-length"
    });

    await broker.alarm();

    assert.ok(apiCalls.some(call => call.method === "DELETE" && call.path.endsWith(`/cfd_tunnel/${staleTunnelId}`)));
    assert.ok(apiCalls.some(call => call.method === "DELETE" && call.path.endsWith("/dns_records/dnsrec1")));
    assert.equal(values.has("installation:vd-aaaaaaaaaaaaaaaa"), false);

    const grandfathered = values.get("installation:vd-bbbbbbbbbbbbbbbb");
    assert.ok(grandfathered.confirmedAt);
    assert.ok(!apiCalls.some(call => call.path.includes("33333333-4444-5555-8666-777777777777")));
    assert.ok(alarmScheduled >= 1);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("installation provisioning stays unavailable without the Cloudflare API secret", async () => {
  const response = await handleRequest(new Request("https://vibedeck.pp.ua/api/installations/provision", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ installationId: "vd-1234567890abcdef", provisioningSecret: "A".repeat(43) })
  }), {
    PUBLIC_BASE_DOMAIN: "vibedeck.pp.ua",
    CLOUDFLARE_ACCOUNT_ID: "a".repeat(32),
    INSTALLATIONS: {}
  });

  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: "provisioning_not_configured" });
});
