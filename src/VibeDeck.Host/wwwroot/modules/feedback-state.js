const STATE_ALIASES = new Map([
  ["", "info"],
  ["info", "info"],
  ["neutral", "info"],
  ["working", "working"],
  ["busy", "working"],
  ["loading", "working"],
  ["success", "success"],
  ["ok", "success"],
  ["warning", "warning"],
  ["warn", "warning"],
  ["error", "error"],
  ["danger", "error"],
  ["muted", "muted"],
]);

export function normalizeFeedbackState(state = "info") {
  return STATE_ALIASES.get(String(state || "").trim().toLowerCase()) || "info";
}

export function applyFeedbackState(element, options = {}) {
  if (!element) return "info";
  const state = normalizeFeedbackState(options.state);
  if (Object.prototype.hasOwnProperty.call(options, "message")) {
    element.textContent = options.message || "";
  }
  element.title = options.detail || "";
  element.dataset.feedbackState = state;
  if (state === "working") element.setAttribute("aria-busy", "true");
  else element.removeAttribute("aria-busy");
  return state;
}
