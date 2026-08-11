let activeResolve = null;
let previousFocus = null;
let bound = false;

function elements() {
  return {
    overlay: document.getElementById("vibeConfirmOverlay"),
    title: document.getElementById("vibeConfirmTitle"),
    message: document.getElementById("vibeConfirmMessage"),
    cancel: document.getElementById("vibeConfirmCancel"),
    accept: document.getElementById("vibeConfirmAccept"),
    close: document.getElementById("vibeConfirmClose"),
  };
}

function finish(result) {
  const { overlay } = elements();
  if (!overlay || overlay.hidden) return;
  overlay.hidden = true;
  overlay.setAttribute("hidden", "");
  document.body.classList.remove("confirm-open");
  const resolve = activeResolve;
  activeResolve = null;
  const focusTarget = previousFocus;
  previousFocus = null;
  if (focusTarget instanceof HTMLElement && focusTarget.isConnected) focusTarget.focus({ preventScroll: true });
  resolve?.(Boolean(result));
}

function bind() {
  if (bound) return;
  const { overlay, cancel, accept, close } = elements();
  if (!overlay || !cancel || !accept || !close) return;
  bound = true;
  cancel.addEventListener("click", () => finish(false));
  close.addEventListener("click", () => finish(false));
  accept.addEventListener("click", () => finish(true));
  overlay.addEventListener("pointerdown", event => {
    if (event.target === overlay) finish(false);
  });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && !overlay.hidden) {
      event.preventDefault();
      finish(false);
    }
  });
}

export function confirmAction({
  title = "Confirm action",
  message = "",
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  tone = "default",
} = {}) {
  bind();
  const { overlay, title: titleNode, message: messageNode, cancel, accept } = elements();
  if (!overlay || !titleNode || !messageNode || !cancel || !accept) {
    return Promise.resolve(window.confirm(message || title));
  }

  if (activeResolve) finish(false);
  titleNode.textContent = title;
  messageNode.textContent = message;
  cancel.textContent = cancelLabel;
  accept.textContent = confirmLabel;
  accept.classList.toggle("danger", tone === "danger");
  overlay.dataset.tone = tone;
  previousFocus = document.activeElement;
  overlay.hidden = false;
  overlay.removeAttribute("hidden");
  document.body.classList.add("confirm-open");

  return new Promise(resolve => {
    activeResolve = resolve;
    requestAnimationFrame(() => accept.focus({ preventScroll: true }));
  });
}
