// Shared rerender guard for every secondary-card consumer. CSS owns the
// top-layer presentation; data renderers use this guard before replacing DOM.
export function hasActiveSecondaryCardInteraction(root, ownerDocument = root?.ownerDocument) {
  if (!root) return false;
  return Boolean(root.querySelector(".secondary-card-dialog[open]"));
}

export function onSecondaryCardInteractionEnd(root, callback) {
  const dialog = root?.querySelector(".secondary-card-dialog[open]");
  if (!dialog || typeof callback !== "function") return false;
  dialog.addEventListener("close", callback, { once: true });
  return true;
}

export function wireSecondaryCardDialog(root) {
  const trigger = root?.querySelector("[data-secondary-card-trigger]");
  const dialog = root?.querySelector(".secondary-card-dialog");
  const closeButton = dialog?.querySelector("[data-secondary-card-close]");
  if (!trigger || !dialog) return;

  const close = () => {
    if (dialog.open) dialog.close();
  };

  trigger.addEventListener("click", () => {
    if (typeof dialog.showModal === "function") dialog.showModal();
    else dialog.setAttribute("open", "");
  });
  closeButton?.addEventListener("click", close);
  dialog.addEventListener("click", event => {
    if (event.target === dialog) close();
  });
  dialog.addEventListener("close", () => trigger.focus({ preventScroll: true }));
}
