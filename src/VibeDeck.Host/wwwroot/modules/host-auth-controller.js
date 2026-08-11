export function buildHostAuthView(status = {}) {
  const required = Boolean(status?.Required ?? status?.required);
  const authenticated = Boolean(status?.Authenticated ?? status?.authenticated);
  return {
    required,
    authenticated,
    gateVisible: required && !authenticated,
  };
}

export function createHostAuthController({
  elements,
  fetch,
  parseJsonResponse,
  t,
  tLegacy,
}) {
  const { gate, passwordInput, submitButton, errorElement } = elements;
  let required = false;
  let authenticated = false;

  function updateGate(message = "") {
    if (!gate) return;
    const visible = required && !authenticated;
    gate.hidden = !visible;
    document.body.classList.toggle("host-auth-required", visible);
    if (errorElement) errorElement.textContent = message || "";
  }

  function applyStatus(status = {}) {
    const view = buildHostAuthView(status);
    required = view.required;
    authenticated = view.authenticated;
    updateGate();
    return view;
  }

  async function loadStatus() {
    const response = await fetch("/api/auth/status", { cache: "no-store" });
    const result = parseJsonResponse(await response.text(), "/api/auth/status");
    if (!response.ok) throw new Error(result.error || result.message || tLegacy("無法讀取登入狀態。"));
    applyStatus(result);
    return result;
  }

  async function login(password) {
    if (!required || authenticated) return true;
    if (submitButton) submitButton.disabled = true;
    if (errorElement) errorElement.textContent = "";
    try {
      const response = await fetch("/api/auth/login", {
        method: "POST",
        cache: "no-store",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password: password || "" }),
      });
      const result = parseJsonResponse(await response.text(), "/api/auth/login");
      if (!response.ok) {
        updateGate(result.error || result.message || t("gates.hostAuthFailed"));
        return false;
      }
      authenticated = true;
      required = Boolean(result.Required ?? result.required ?? required);
      updateGate();
      if (passwordInput) passwordInput.value = "";
      return true;
    } catch (error) {
      updateGate(error.message || t("gates.hostAuthFailed"));
      return false;
    } finally {
      if (submitButton) submitButton.disabled = false;
    }
  }

  function submitCurrentPassword() {
    return login(passwordInput?.value || "");
  }

  return {
    applyStatus,
    loadStatus,
    login,
    submitCurrentPassword,
    updateGate,
    isRequired: () => required,
    isAuthenticated: () => authenticated,
    canBypassGate: () => !required || authenticated,
  };
}
