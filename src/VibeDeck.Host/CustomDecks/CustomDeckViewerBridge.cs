using System;

namespace VibeDeck.Host.CustomDecks
{
    internal static class CustomDeckViewerBridge
    {
        internal const string Script = """
<script>(() => {
  let viewerActive = false;
  const interactiveSelector = "button,a,input,select,textarea,[contenteditable='true'],[role='button']";

  window.addEventListener("message", event => {
    const message = event.data;
    if (!message || message.type !== "vibedeck:deck-environment") return;
    viewerActive = Boolean(message.environment?.viewerActive);
  });

  document.addEventListener("dblclick", async event => {
    if (event.target instanceof Element && event.target.closest(interactiveSelector)) return;
    event.preventDefault();

    if (!viewerActive) {
      const root = document.documentElement;
      const candidates = [
        () => root.requestFullscreen?.({ navigationUI: "hide" }),
        () => root.webkitRequestFullscreen?.(),
        () => root.webkitRequestFullScreen?.(),
      ];
      for (const tryEnter of candidates) {
        try {
          const result = tryEnter();
          if (result && typeof result.then === "function") await result;
          if (document.fullscreenElement || document.webkitFullscreenElement) break;
        } catch {}
      }
    }

    parent.postMessage({ type: "vibedeck:deck-command", action: "toggle-viewer" }, "*");
  }, { capture: true, passive: false });
})();</script>
""";

        internal static string Inject(string html, string prefix = "")
        {
            var injection = prefix + Script;
            var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
            var close = head >= 0 ? html.IndexOf('>', head) : -1;
            return close >= 0 ? html.Insert(close + 1, injection) : injection + html;
        }
    }
}
