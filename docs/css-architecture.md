# CSS architecture

VibeDeck CSS is organized for ownership and predictable cascade behavior, not
for the smallest possible line count. A shorter stylesheet is only better when
the next human or AI can still tell where a change belongs and why an override
exists.

## Maintenance rules

1. One UI responsibility should have one final owner.
2. Responsive composition is selected by component space and content pressure,
   never by a device model, user agent, tablet range, or orientation class.
3. Prefer deleting dead, unreachable, superseded, or old-mode CSS over creating
   new abstractions.
4. Do not merge unrelated selectors merely because a few declarations happen
   to match.
5. Preserve selector specificity when consolidating rules. A comma-separated
   selector list is safer than `:is()` when alternatives have different
   specificity, because `:is()` adopts the most specific alternative.
6. Do not use native CSS nesting. The old ZenPad/WebView compatibility target is
   intentional.
7. `!important` is reserved for the global `[hidden]` contract and the
   reduced-motion accessibility override. Component layout, access state,
   sensor correction, and responsive behavior must resolve through ownership
   and specificity instead of cascade force.
8. `remote-client` describes product access context for every non-local
   browser, including Linux desktops. `eink-client` is a semantic presentation
   mode. Neither class is a geometry breakpoint.
9. Keep explicit CSS when compression would hide intent. For example,
   `overflow-x` / `overflow-y` may remain separate when that makes the scrolling
   contract easier to understand.

The only remaining declarations are `[hidden]` and the E-Ink reduced-motion
accessibility override. Sensor rotation is imported last as its explicit owner
and therefore needs no specificity escape hatch.

## Responsive layout contract

- Components respond to the space they receive. Use a named container and a
  content breakpoint when a component changes composition; do not infer that
  composition from a model name, user agent, or a `phone-client` selector.
- Grid columns use `minmax(0, 1fr)` (or another explicit content floor), and
  flexible children set `min-width: 0` so long labels cannot widen the page.
- Viewport and document shells own scrolling. A child may use `overflow:hidden`
  for a progress fill, compact preview with an explicit detail route, or an
  intentional media canvas boundary, but never to conceal unreachable content.
- Forced physical rotation remains an isolated viewer compatibility path.
  Ordinary components do not consume portrait/landscape classes.
- E-Ink remains a semantic high-contrast interaction mode. Its paper surface,
  touch sizes and reduced motion may differ, while its geometry still adapts to
  available space and exposes safe scrolling when one screen is insufficient.

## Load order and owners

`index.css` loads foundation/compatibility layers first, then shared primitives,
then final component owners, then narrow overlays.

### Foundation and compatibility

- `00-base-tokens.css`: global tokens and hard document contracts such as
  `[hidden]`. The application palette is exposed here through `--theme-*`
  tokens; runtime values are owned by `modules/app-theme.js` and are shared by
  Setup, Display, Sideboard, Quota, and the app shell rather than by a page skin.
- `10-core.css`: application shell, shared viewer geometry, remote trust gating,
  base controls, and PC console behavior.
- `components/access-gates.css`: remote Host authentication, iOS install hint,
  and mobile HTTP-to-HTTPS blocking UI.
- `components/display-surface.css`: remote-screen surface, container-driven
  toolbar/source controls, stream rotation, unavailable state, and the hard
  fullscreen media-canvas boundary.
- `30-phone-dashboard.css`: legacy filename for the container-selected compact
  dashboard/detail experience; it contains no device-identity geometry.
- `40-eink-sensor-orientation.css`: BOOX/sensor-orientation hard geometry only.
  It is imported last and contains no cascade-force declarations.

### Shared primitives

- `components/shared-primitives.css`: cross-component contracts that are truly
  semantic primitives, not coincidental declaration bundles.
- `components/eink-primitives.css`: global E-Ink typography, controls, viewport
  safety, paper surfaces, and readable dashboard baselines.

### Final component owners

- `sideboard-shell.css`: Sideboard frame and dashboard grid geometry.
- `dashboard-editor.css`: dashboard edit-mode chrome and editing behavior.
- `custom-cards.css`: Custom Cards, Windows notification integration, card
  settings, source management, credentials, and container-driven management
  layout.
- `dashboard-navigation.css`: dashboard mode switching and connection chrome.
- `pairing-setup.css`: pairing, trust/rescue UI, setup QR, locale and success UI.
- `setup-diagnostics.css`: setup diagnostics.
- `device-management.css`: trusted-device management.
- `app-controls.css`: utility/display controls and header-container policies.
- `metric-card.css`: metric card internals.
- `quota-mini-card.css`: Sideboard quota mini card.
- `activity-feed.css`: Activity Feed card internals.
- `quota-page.css`: full Quota page.
- `eink-dashboard-content.css`: final E-Ink dashboard content/paper overrides.

If a component selector appears outside its owner, first decide whether it is a
real application-shell/compatibility contract. If it is only restating the
component's appearance, move or delete it instead of adding another override.

## Regression gates

After structural CSS changes, start the source Host and run:

```powershell
.\scripts\test-product-flow.ps1 -Source -Responsive
```

This is a continuous viewport and aspect-ratio sweep rather than one CSS file
per known device. It covers Sideboard read/edit/detail, Quota, Display, Setup,
Custom Deck and Custom Cards management across narrow, square, short-wide and
tall surfaces. PC, generic mobile, iPhone safe-area, ASUS tablet and E-Ink
profiles plus remote Linux desktop exercise environment semantics; component composition still follows
the space measured by its container. `device-lab.html` remains the interactive
spot-check surface.

For the fast, non-browser gate used during ordinary local work, run
`.\scripts\test-product-flow.ps1 -Source` (or `npm test` for only the browser
unit and CSS contract layer). The `-Responsive` switch intentionally remains an
explicit full-layout gate because it launches all 169 Chrome scenarios.

Important Sideboard gates include:

- dashboard cards must not overlap each other;
- visible Sideboard chrome must not collapse to zero height;
- Sideboard chrome must not overlap the dashboard page;
- metric secondary text must not overlap its bar.
- Quota cards and their controls must not overlap at short viewport heights;
- expanded Display controls must remain inside their canvas;
- the last Setup, Quota, Deck and management control must be reachable through
  an explicit scrolling route;
- visible structural containers may not collapse to zero size or hide excess
  content with `overflow:hidden`/`clip`.

Run the smallest relevant mode first, then the full matrix for changes that
alter shared primitives, import order, layout ownership, or device-class
interaction.

`tmp/css-dead-cascade-audit.mjs` should stay empty for guaranteed same-selector
dead declarations. `tmp/css-subset-audit.mjs` is a candidate generator only:
matching declarations are not, by themselves, a reason to merge selectors.
Likewise, `duplicateSelectors` in `tmp/css-audit.mjs` is a raw cross-context
diagnostic: a selector repeated in base, media, container or device contexts is
often intentional. It is not a slimming KPI.
`tmp/css-owner-audit.mjs` is a conservative ownership guard for component
selector families with well-established owners. If it reports a violation,
either move the rule back to its owner or document a deliberate exception in
the audit instead of silently spreading ownership.
`tmp/css-reference-audit.mjs` lists class/id selectors with no literal HTML/JS
reference as dead-code candidates. It is intentionally advisory: runtime-built
class names must be verified manually before deleting anything.

`tmp/css-variable-audit.mjs` lists custom properties that are defined but have
no `var(...)` consumer and no literal runtime reference. It is also advisory:
verify that a candidate is not an intentional external theming API before
deleting it.

E-Ink `select`/`summary` touch height is a hard primitive contract. Component
owners that need a taller control should set `--eink-control-min-height`
instead of adding another `min-height: ... !important` override.

## Change checklist

Before keeping a cleanup, be able to answer:

- Which owner should this rule belong to?
- Is the override expressing a real difference or repeating the base rule?
- Does consolidation preserve specificity and source-order behavior?
- Is this a product capability/state difference, or geometry that belongs in a
  container query?
- Which visual-matrix scenarios prove the change?
- Would the next maintainer understand the rule faster after this change?

If the last answer is no, do not keep the cleanup just to reduce a metric.
