# CSS architecture

VibeDeck CSS is organized for ownership and predictable cascade behavior, not
for the smallest possible line count. A shorter stylesheet is only better when
the next human or AI can still tell where a change belongs and why an override
exists.

## Maintenance rules

1. One UI responsibility should have one final owner.
2. Device and responsive overrides should state only the values that actually
   differ from the owner's base rule.
3. Prefer deleting dead, unreachable, superseded, or old-mode CSS over creating
   new abstractions.
4. Do not merge unrelated selectors merely because a few declarations happen
   to match.
5. Preserve selector specificity when consolidating rules. A comma-separated
   selector list is safer than `:is()` when alternatives have different
   specificity, because `:is()` adopts the most specific alternative.
6. Do not use native CSS nesting. The old ZenPad/WebView compatibility target is
   intentional.
7. `!important` is allowed for explainable hard contracts such as sensor
   rotation geometry, `[hidden]`, or runtime/inline-style boundaries. Zero
   `!important` is not a goal.
8. E-Ink devices may also carry `phone-client`. Ordinary-phone visual rules must
   exclude `.eink-client` when the two visual systems differ. Shared geometry
   may intentionally apply to both.
9. Keep explicit CSS when compression would hide intent. For example,
   `overflow-x` / `overflow-y` may remain separate when that makes the scrolling
   contract easier to understand.

The remaining `!important` declarations are intentionally concentrated at hard
boundaries rather than ordinary component styling. Examples include `[hidden]`,
sensor-orientation geometry, E-Ink reduced-motion/control/viewer contracts,
Dashboard Editor's explicit escape from the one-screen E-Ink read mode, and
application-state exclusions such as trust/access gates or background scroll
locks. Do not treat the raw count as cleanup debt without first removing the
underlying boundary that makes the hard override necessary.

## Load order and owners

`index.css` loads foundation/compatibility layers first, then shared primitives,
then final component owners, then narrow overlays.

### Foundation and compatibility

- `00-base-tokens.css`: global tokens and hard document contracts such as
  `[hidden]`.
- `10-core.css`: application shell, shared viewer geometry, phone orientation,
  trust gating, base controls, and PC console behavior.
- `components/access-gates.css`: remote Host authentication, iOS install hint,
  and mobile HTTP-to-HTTPS blocking UI.
- `components/display-surface.css`: remote-screen surface, Display toolbar and
  source selection, stream rotation, unavailable state, and Display-specific
  phone/PC/fullscreen/tablet geometry.
- `30-phone-dashboard.css`: normal-phone compact dashboard/detail experience.
- `40-eink-sensor-orientation.css`: BOOX/sensor-orientation hard geometry only.
  Its `!important` declarations are intentional runtime geometry contracts.

### Shared primitives

- `components/shared-primitives.css`: cross-component contracts that are truly
  semantic primitives, not coincidental declaration bundles.
- `components/eink-primitives.css`: global E-Ink typography, controls, viewport
  safety, paper surfaces, and readable dashboard baselines.

### Final component owners

- `sideboard-shell.css`: Sideboard frame and dashboard grid geometry.
- `dashboard-editor.css`: dashboard edit-mode chrome and editing behavior.
- `custom-cards.css`: Custom Cards, Windows notification integration, card
  settings, source management, credentials, and phone-portrait layout.
- `dashboard-navigation.css`: dashboard mode switching and connection chrome.
- `pairing-setup.css`: pairing, trust/rescue UI, setup QR, locale and success UI.
- `setup-diagnostics.css`: setup diagnostics.
- `device-management.css`: trusted-device management.
- `app-controls.css`: utility/display controls and their device policies.
- `metric-card.css`: metric card internals.
- `quota-mini-card.css`: Sideboard quota mini card.
- `activity-feed.css`: Activity Feed card internals.
- `quota-page.css`: full Quota page.
- `eink-dashboard-content.css`: final E-Ink dashboard content/paper overrides.

If a component selector appears outside its owner, first decide whether it is a
real application-shell/compatibility contract. If it is only restating the
component's appearance, move or delete it instead of adding another override.

## Regression gates

After structural CSS changes, use `tmp/visual-matrix.mjs`. The full matrix is
172 scenarios across ZenPad, BOOX, Galaxy and iPhone profiles, including 12
Custom Cards settings/source-manager interaction variants on ZenPad and BOOX,
plus four Host Auth / mobile HTTPS gate scenarios.

Important Sideboard gates include:

- dashboard cards must not overlap each other;
- visible Sideboard chrome must not collapse to zero height;
- Sideboard chrome must not overlap the dashboard page;
- metric secondary text must not overlap its bar.

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
- Could `phone-client`, `tablet-client`, and `eink-client` coexist here?
- Which visual-matrix scenarios prove the change?
- Would the next maintainer understand the rule faster after this change?

If the last answer is no, do not keep the cleanup just to reduce a metric.
