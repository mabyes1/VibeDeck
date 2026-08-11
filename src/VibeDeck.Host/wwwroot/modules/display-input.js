export function createDisplayInputController({
  targets,
  getInputSocket,
  getDeviceName,
  getActiveStreamElement,
  getMediaWidth,
  getMediaHeight,
  resolveRotation,
  isMobileClient,
  enterLandscapeViewer,
  keyboardButton = null,
  keyboardInput = null,
  translate = key => key,
  touchLongPressMs = 460,
  touchDragThresholdPx = 12,
}) {
  let touchInputState = null;
  let keyboardWired = false;
  let keyboardComposing = false;
  let ignoreNextKeyboardInput = false;
  const specialKeys = new Set([
    "Backspace", "Tab", "Enter", "Escape", "PageUp", "PageDown",
    "End", "Home", "ArrowLeft", "ArrowUp", "ArrowRight", "ArrowDown",
    "Insert", "Delete", "F1", "F2", "F3", "F4", "F5", "F6",
    "F7", "F8", "F9", "F10", "F11", "F12",
  ]);

  function clamp01(value) {
    return Math.max(0, Math.min(1, value));
  }

  function getScreenContentBox() {
    const media = getActiveStreamElement();
    const rect = media.getBoundingClientRect();
    const naturalWidth = getMediaWidth(media) || rect.width;
    const naturalHeight = getMediaHeight(media) || rect.height;
    const fit = getComputedStyle(media).objectFit || "contain";

    if (fit === "cover" && naturalWidth && naturalHeight && rect.width && rect.height) {
      const scale = Math.max(rect.width / naturalWidth, rect.height / naturalHeight);
      const width = naturalWidth * scale;
      const height = naturalHeight * scale;
      return {
        left: rect.left + ((rect.width - width) / 2),
        top: rect.top + ((rect.height - height) / 2),
        width,
        height,
      };
    }

    if (fit !== "contain" || !naturalWidth || !naturalHeight || !rect.width || !rect.height) {
      return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
    }

    const elementAspect = rect.width / rect.height;
    const contentAspect = naturalWidth / naturalHeight;
    if (contentAspect > elementAspect) {
      const height = rect.width / contentAspect;
      return { left: rect.left, top: rect.top + ((rect.height - height) / 2), width: rect.width, height };
    }

    const width = rect.height * contentAspect;
    return { left: rect.left + ((rect.width - width) / 2), top: rect.top, width, height: rect.height };
  }

  function mapPointerToDisplay(event) {
    const box = getScreenContentBox();
    if (!box.width || !box.height) return null;

    const rawX = (event.clientX - box.left) / box.width;
    const rawY = (event.clientY - box.top) / box.height;
    if (rawX < 0 || rawX > 1 || rawY < 0 || rawY > 1) return null;

    let x = rawX;
    let y = rawY;
    const rotation = resolveRotation();
    if (rotation === "90") {
      x = rawY;
      y = 1 - rawX;
    } else if (rotation === "180") {
      x = 1 - rawX;
      y = 1 - rawY;
    } else if (rotation === "270") {
      x = 1 - rawY;
      y = rawX;
    }

    return { x: clamp01(x), y: clamp01(y) };
  }

  function buildPayload(event) {
    const point = mapPointerToDisplay(event);
    return point ? { deviceName: getDeviceName(), ...point, buttons: event.buttons || 0 } : null;
  }

  function sendMessage(type, payload, buttonsOverride) {
    const socket = getInputSocket();
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify({
      type,
      deviceName: payload.deviceName,
      x: payload.x,
      y: payload.y,
      buttons: buttonsOverride ?? payload.buttons ?? 0,
    }));
  }

  function sendKeyboardMessage(message) {
    const socket = getInputSocket();
    if (!socket || socket.readyState !== WebSocket.OPEN) return false;
    socket.send(JSON.stringify({
      deviceName: getDeviceName(),
      ...message,
    }));
    return true;
  }

  function sendText(text) {
    const value = String(text || "");
    if (!value) return false;
    let sent = false;
    for (let offset = 0; offset < value.length; offset += 256) {
      sent = sendKeyboardMessage({ type: "text", text: value.slice(offset, offset + 256) }) || sent;
    }
    return sent;
  }

  function sendKey(eventOrKey, overrides = {}) {
    const event = typeof eventOrKey === "string" ? null : eventOrKey;
    const key = typeof eventOrKey === "string" ? eventOrKey : event?.key;
    return sendKeyboardMessage({
      type: "key",
      key: key || "",
      code: overrides.code ?? event?.code ?? "",
      ctrlKey: overrides.ctrlKey ?? Boolean(event?.ctrlKey),
      altKey: overrides.altKey ?? Boolean(event?.altKey),
      shiftKey: overrides.shiftKey ?? Boolean(event?.shiftKey),
      metaKey: overrides.metaKey ?? Boolean(event?.metaKey),
    });
  }

  function isKeyboardFocused() {
    return Boolean(keyboardInput && document.activeElement === keyboardInput);
  }

  function updateKeyboardButton(active = isKeyboardFocused()) {
    if (!keyboardButton) return;
    keyboardButton.setAttribute("aria-pressed", active ? "true" : "false");
    keyboardButton.textContent = translate(active ? "ui.remoteKeyboardActive" : "ui.remoteKeyboard");
  }

  function resetKeyboardInput() {
    if (keyboardInput) keyboardInput.value = "";
  }

  function dismissKeyboard() {
    if (!keyboardInput) return false;
    keyboardComposing = false;
    if (isKeyboardFocused()) {
      keyboardInput.blur();
      return true;
    }
    resetKeyboardInput();
    updateKeyboardButton(false);
    return false;
  }

  function wireKeyboard() {
    if (keyboardWired || !keyboardButton || !keyboardInput) return;
    keyboardWired = true;

    keyboardButton.addEventListener("click", () => {
      if (isKeyboardFocused()) {
        dismissKeyboard();
        return;
      }
      resetKeyboardInput();
      keyboardInput.focus({ preventScroll: true });
      updateKeyboardButton(true);
    });
    keyboardInput.addEventListener("focus", () => updateKeyboardButton(true));
    keyboardInput.addEventListener("blur", () => {
      keyboardComposing = false;
      resetKeyboardInput();
      updateKeyboardButton(false);
    });
    keyboardInput.addEventListener("compositionstart", () => {
      keyboardComposing = true;
      resetKeyboardInput();
    });
    keyboardInput.addEventListener("compositionend", event => {
      keyboardComposing = false;
      const text = event.data || keyboardInput.value;
      if (text) sendText(text);
      resetKeyboardInput();
      ignoreNextKeyboardInput = true;
      setTimeout(() => { ignoreNextKeyboardInput = false; }, 0);
    });
    keyboardInput.addEventListener("keydown", event => {
      if (event.isComposing || keyboardComposing || event.key === "Process") return;
      const modified = event.ctrlKey || event.altKey || event.metaKey;
      if (!specialKeys.has(event.key) && !modified) return;
      event.preventDefault();
      sendKey(event);
      resetKeyboardInput();
    });
    keyboardInput.addEventListener("beforeinput", event => {
      if (keyboardComposing || event.isComposing) return;
      if (ignoreNextKeyboardInput) {
        event.preventDefault();
        ignoreNextKeyboardInput = false;
        resetKeyboardInput();
        return;
      }
      if (event.inputType === "deleteContentBackward") {
        event.preventDefault();
        sendKey("Backspace");
      } else if (event.inputType === "deleteContentForward") {
        event.preventDefault();
        sendKey("Delete");
      } else if (event.inputType === "insertLineBreak" || event.inputType === "insertParagraph") {
        event.preventDefault();
        sendKey("Enter");
      } else if (event.inputType?.startsWith("insert") && event.data) {
        event.preventDefault();
        sendText(event.data);
      }
      resetKeyboardInput();
    });
    keyboardInput.addEventListener("input", () => {
      if (keyboardComposing) return;
      if (ignoreNextKeyboardInput) {
        ignoreNextKeyboardInput = false;
        resetKeyboardInput();
        return;
      }
      if (keyboardInput.value) sendText(keyboardInput.value);
      resetKeyboardInput();
    });
    updateKeyboardButton(false);
  }

  function sendPointer(type, event, buttonsOverride) {
    const payload = buildPayload(event);
    if (!payload) return false;
    event.preventDefault();
    sendMessage(type, payload, buttonsOverride);
    return true;
  }

  function clearTouchState() {
    if (touchInputState?.timer) clearTimeout(touchInputState.timer);
    touchInputState = null;
  }

  function beginTouch(event) {
    const payload = buildPayload(event);
    if (!payload) return;
    clearTouchState();
    touchInputState = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      lastPayload: payload,
      dragStarted: false,
      longPressTriggered: false,
      timer: setTimeout(() => {
        if (!touchInputState || touchInputState.pointerId !== event.pointerId || touchInputState.dragStarted) return;
        touchInputState.longPressTriggered = true;
        sendMessage("rightclick", touchInputState.lastPayload, 2);
      }, touchLongPressMs),
    };
    event.preventDefault();
  }

  function updateTouch(event) {
    if (!touchInputState || touchInputState.pointerId !== event.pointerId) return;
    const payload = buildPayload(event);
    if (!payload) return;
    touchInputState.lastPayload = payload;
    const moved = Math.hypot(event.clientX - touchInputState.startX, event.clientY - touchInputState.startY) >= touchDragThresholdPx;
    if (touchInputState.longPressTriggered) {
      event.preventDefault();
      return;
    }
    if (!moved && !touchInputState.dragStarted) return;
    if (touchInputState.timer) {
      clearTimeout(touchInputState.timer);
      touchInputState.timer = null;
    }
    if (!touchInputState.dragStarted) {
      touchInputState.dragStarted = true;
      sendMessage("pointerdown", payload, 1);
    }
    sendMessage("pointermove", payload, 1);
    event.preventDefault();
  }

  function endTouch(event, cancelled) {
    if (!touchInputState || touchInputState.pointerId !== event.pointerId) return;
    const payload = buildPayload(event) || touchInputState.lastPayload;
    if (touchInputState.timer) clearTimeout(touchInputState.timer);
    if (touchInputState.longPressTriggered) {
      clearTouchState();
      event.preventDefault();
      return;
    }
    if (touchInputState.dragStarted) {
      if (payload) sendMessage(cancelled ? "pointercancel" : "pointerup", payload, 0);
      clearTouchState();
      event.preventDefault();
      return;
    }
    if (payload) {
      sendMessage("pointerdown", payload, 1);
      sendMessage("pointerup", payload, 0);
    }
    clearTouchState();
    event.preventDefault();
  }

  function wire(target) {
    target.addEventListener("dragstart", event => event.preventDefault());
    target.addEventListener("contextmenu", event => event.preventDefault());
    target.addEventListener("pointerdown", event => {
      // Soft keyboard stays open until focus leaves the hidden textarea.
      // Tapping the stream surface is the natural dismiss gesture on phones.
      if (isKeyboardFocused()) {
        dismissKeyboard();
      }
      target.setPointerCapture(event.pointerId);
      if (event.pointerType === "touch") return beginTouch(event);
      sendPointer("pointerdown", event);
    });
    target.addEventListener("pointermove", event => {
      if (event.pointerType === "touch") return updateTouch(event);
      // A desktop hover must not move the real Windows cursor onto the virtual
      // display. Only relay movement while a mouse/pen button is held so the
      // embedded display behaves like a deliberate drag target, not a focus trap.
      if (!event.buttons) return;
      sendPointer("pointermove", event);
    });
    target.addEventListener("pointerup", event => {
      if (event.pointerType === "touch") return endTouch(event, false);
      sendPointer("pointerup", event);
    });
    target.addEventListener("pointercancel", event => {
      if (event.pointerType === "touch") return endTouch(event, true);
      sendPointer("pointercancel", event);
    });
    target.addEventListener("dblclick", event => {
      if (!isMobileClient()) enterLandscapeViewer(event);
    });
  }

  return {
    wire,
    wireAll() {
      targets.filter(Boolean).forEach(wire);
      wireKeyboard();
    },
    clearTouchState,
    dismissKeyboard,
    isKeyboardFocused,
    focusKeyboard() {
      keyboardInput?.focus({ preventScroll: true });
    },
    isTouchGestureActive() {
      return Boolean(touchInputState && (touchInputState.dragStarted || touchInputState.longPressTriggered));
    },
  };
}
