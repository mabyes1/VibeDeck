export function readAuditField(entry, pascalName, camelName) {
  return entry?.[camelName] ?? entry?.[pascalName] ?? "";
}

export function formatAuditTime(value, locale) {
  if (!value) return "--";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString(locale, {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

export function buildDiagnosticsText(snapshot, { locale, tLegacy, now = () => new Date() }) {
  const entries = snapshot?.entries || [];
  const header = [
    `VibeDeck ${tLegacy("診斷軌跡")}`,
    `${tLegacy("產生時間")}：${snapshot?.generatedAt || now().toISOString()}`,
    tLegacy(`最近 ${entries.length} 筆操作 · 保留 ${snapshot?.retentionDays || 30} 天`),
  ];
  const lines = entries.map(entry => {
    const details = readAuditField(entry, "Details", "details") || {};
    return [
      formatAuditTime(readAuditField(entry, "Timestamp", "timestamp"), locale),
      `[${readAuditField(entry, "Severity", "severity") || "information"}]`,
      `${readAuditField(entry, "Category", "category") || "--"}/${readAuditField(entry, "Action", "action") || "--"}`,
      readAuditField(entry, "Outcome", "outcome") || "--",
      `${tLegacy("追蹤碼")}: ${readAuditField(entry, "TraceId", "traceId") || "--"}`,
      readAuditField(entry, "Subject", "subject"),
      Object.entries(details).map(([key, value]) => `${key}: ${value}`).join("; "),
    ].filter(Boolean).join(" · ");
  });
  return [...header, "", ...lines].join("\n");
}

export function createDiagnosticsController({
  document,
  navigator,
  elements,
  fetchJsonOrThrow,
  tLegacy,
  getIntlLocale,
  isLocalRequest,
}) {
  const { panel, summary, list, markButton } = elements;
  let loading = false;
  let latest = { entries: [], retentionDays: 30, generatedAt: "" };

  function severityLabel(value) {
    const labels = {
      error: "錯誤",
      warning: "警告",
      information: "資訊",
    };
    return tLegacy(labels[String(value || "").toLowerCase()] || String(value || "資訊"));
  }

  function render(result) {
    const entries = result?.entries || result?.Entries || [];
    const retentionDays = Number(result?.retentionDays ?? result?.RetentionDays) || 30;
    const generatedAt = result?.generatedAt || result?.GeneratedAt || "";
    const storageError = result?.storageError || result?.StorageError || "";
    latest = { entries, retentionDays, generatedAt };

    if (summary) {
      summary.textContent = tLegacy(`最近 ${entries.length} 筆操作 · 保留 ${retentionDays} 天`);
    }
    if (!list) return;

    list.replaceChildren();
    if (storageError) {
      const warning = document.createElement("span");
      warning.className = "diagnostics-storage-error";
      warning.textContent = `${tLegacy("診斷紀錄寫入失敗")}：${storageError}`;
      list.append(warning);
    }
    if (!entries.length) {
      const empty = document.createElement("span");
      empty.className = "diagnostics-empty";
      empty.textContent = tLegacy("尚無可顯示的診斷軌跡。");
      list.append(empty);
      return;
    }

    for (const entry of entries) {
      const row = document.createElement("article");
      row.className = `diagnostics-entry is-${String(readAuditField(entry, "Severity", "severity") || "information").toLowerCase()}`;
      const title = document.createElement("strong");
      const category = readAuditField(entry, "Category", "category");
      const action = readAuditField(entry, "Action", "action");
      const outcome = readAuditField(entry, "Outcome", "outcome");
      title.textContent = [
        severityLabel(readAuditField(entry, "Severity", "severity")),
        [category, action].filter(Boolean).join("/"),
        outcome,
      ].filter(Boolean).join(" · ");

      const meta = document.createElement("span");
      const traceId = readAuditField(entry, "TraceId", "traceId");
      meta.className = "diagnostics-entry-meta";
      meta.textContent = [
        formatAuditTime(readAuditField(entry, "Timestamp", "timestamp"), getIntlLocale()),
        traceId ? `${tLegacy("追蹤碼")} ${traceId}` : "",
      ].filter(Boolean).join(" · ");
      row.append(title, meta);

      const subject = readAuditField(entry, "Subject", "subject");
      if (subject) {
        const subjectElement = document.createElement("span");
        subjectElement.className = "diagnostics-entry-subject";
        subjectElement.textContent = subject;
        row.append(subjectElement);
      }

      const details = readAuditField(entry, "Details", "details") || {};
      const detailText = Object.entries(details)
        .filter(([key, value]) => key && value != null && value !== "")
        .map(([key, value]) => `${key}: ${value}`)
        .join(" · ");
      if (detailText) {
        const detailsElement = document.createElement("span");
        detailsElement.className = "diagnostics-entry-details";
        detailsElement.textContent = detailText;
        row.append(detailsElement);
      }
      list.append(row);
    }
  }

  async function load() {
    if (!isLocalRequest() || !panel || loading) return;
    loading = true;
    if (summary) summary.textContent = tLegacy("正在讀取診斷軌跡…");
    try {
      render(await fetchJsonOrThrow("/api/diagnostics/audit?limit=80"));
    } catch (error) {
      if (summary) summary.textContent = error.message || tLegacy("無法載入診斷軌跡。");
      if (list && !list.childElementCount) {
        const empty = document.createElement("span");
        empty.className = "diagnostics-empty";
        empty.textContent = tLegacy("無法載入診斷軌跡。");
        list.append(empty);
      }
    } finally {
      loading = false;
    }
  }

  function text() {
    return buildDiagnosticsText(latest, {
      locale: getIntlLocale(),
      tLegacy,
    });
  }

  async function copy() {
    const value = text();
    let copied = false;
    try {
      await navigator.clipboard.writeText(value);
      copied = true;
    } catch {
      const textArea = document.createElement("textarea");
      textArea.value = value;
      textArea.setAttribute("readonly", "");
      textArea.style.position = "fixed";
      textArea.style.opacity = "0";
      document.body.append(textArea);
      textArea.select();
      try {
        copied = document.execCommand("copy");
      } catch {
        copied = false;
      }
      textArea.remove();
    }
    if (summary) {
      summary.textContent = copied
        ? tLegacy("診斷摘要已複製")
        : tLegacy("無法複製診斷摘要。");
    }
  }

  async function mark() {
    if (!isLocalRequest() || !markButton) return;
    const originalText = markButton.textContent;
    markButton.disabled = true;
    markButton.textContent = tLegacy("正在標記…");
    try {
      await fetchJsonOrThrow("/api/diagnostics/audit/mark", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ label: `PC checkpoint ${new Date().toISOString()}` }),
      });
      await load();
    } catch (error) {
      if (summary) summary.textContent = error.message || tLegacy("無法標記目前狀態。");
    } finally {
      markButton.disabled = false;
      markButton.textContent = originalText;
    }
  }

  return { render, load, text, copy, mark };
}
