# VibeDeck — OpenAI Build Week Submission Kit

This is the final English source copy for the Devpost submission. Official deadline: **July 21, 2026 at 5:00 PM PDT** (**July 22 at 8:00 AM UTC+8**).

## Submission Fields

| Field | Final copy |
|---|---|
| Project name | `VibeDeck` |
| Category | `Work & Productivity` |
| Tagline | `A trusted second surface for your Windows work.` |
| Repository | `https://github.com/mabyes1/phoneMonitor` |
| Release/testing URL | `https://github.com/mabyes1/phoneMonitor/releases/latest` |
| Primary `/feedback` Codex Session ID | `019f6890-877f-71e0-9ffa-7cf4d4457f2a` |

**Short description**

> VibeDeck turns a spare phone or e-paper reader into a secure Windows display, system sideboard, and AI-usage companion through one browser/PWA—locally or across networks, with approval staying on the PC.

## Long Description

### Bring idle screens back to work

Most desks already have another screen: an older phone, a small tablet, or an e-paper reader. Generic mirroring makes that device a cramped miniature desktop. VibeDeck gives it a role that matches its size: a real Windows display when needed, a glanceable system sideboard the rest of the time, and a persistent view of AI-tool usage.

One Windows Host serves iPhone, Android, and BOOX through Safari, Chrome, or an installable PWA. A browser requests access, shows a six-digit code, and must be explicitly approved on the PC. Once approved, it can reconnect on the same Wi-Fi or through the installation's browser-trusted HTTPS address. There is no native mobile app, VibeDeck account, VPN, or router port-forwarding setup.

### Three useful roles

- **Display:** stream a real Windows virtual display or choose an existing monitor. The approved browser receives WebRTC H.264 with a JPEG fallback and can use touch/mouse control plus its mobile keyboard.
- **Sideboard:** show live CPU, GPU, memory, storage, network, weather, processes, activity, and custom cards in a phone-first layout.
- **Quota:** keep Codex and AGY usage, reset windows, accounts, and remaining credits visible without opening another dashboard.

Only the extended-display workflow needs the optional virtual display. Existing-monitor control, Sideboard, Quota, pairing, and Device Lab work without it.

### Why it is different

VibeDeck is built around the realities that prototypes often skip: Windows interactive-session boundaries, explicit device trust, secure reconnect across networks, browser-media fallback, persistent state, installation and updates, e-paper readability, multilingual UI, diagnostics, and repeatable release checks. The cloud control plane can route encrypted traffic and resolve one-time connection codes, but it cannot approve or revoke a device; the Windows PC remains authoritative.

### Built with Codex and GPT-5.6

VibeDeck existed before Build Week as a Windows virtual-display and browser-streaming prototype. The official cutoff was `2026-07-13 09:00 PDT` (`2026-07-14 00:00 UTC+8`). The last pre-event baseline is commit [`872a985`](https://github.com/mabyes1/phoneMonitor/commit/872a985c27dbb8c486aef50b7e76a2b1c67d5f8d), and the 21-commit core event range is [`21c27e3..fc81cce`](https://github.com/mabyes1/phoneMonitor/compare/872a985...fc81cce).

During the event, Codex and GPT-5.6 helped turn the prototype into a coherent product: Windows Setup and updates, signed-in-session startup, persistent product data, one browser/PWA path across three device classes, managed HTTPS routing, hardened pairing, existing-monitor control, mobile keyboard input, e-paper and multilingual layouts, Device Lab, diagnostics, tests, packaging, and delivery assets.

Codex accelerated planning, implementation, debugging, review, test design, and release preparation. GPT-5.6 was used for the cross-system decisions where a local fix could break another layer: Session 0 versus interactive display capture, browser trust versus remote access, pairing authority versus cloud routing, media fallbacks, state ownership, and installer lifecycle. Human judgment owned the product scope, security trade-offs, real-device validation, and final acceptance.

## Testing Instructions — Paste into Devpost

> **Platform:** Windows 10 or 11 x64. The client is a current Safari or Chromium browser on any phone/tablet; a physical phone is optional for the first review.
>
> 1. Download `VibeDeck-Setup-<version>.exe` and its `.sha256` from the latest GitHub Release.
> 2. Run Setup, then open `http://127.0.0.1:5000` on the Windows PC.
> 3. Without a phone, run `scripts\open-device-lab.ps1` from the repository. Switch among BOOX Go Color 7, Galaxy S23, and iPhone XS profiles; these load the real client at the target viewport.
> 4. With a phone, open the QR URL, select Start pairing, match the six-digit code, and approve the request on the PC.
> 5. Try Sideboard and Quota first; neither requires the optional virtual display. Display mode can control an existing monitor immediately. Creating a separate extended display is optional and requires one local elevation prompt.
> 6. Automated source verification: `scripts\test-product-flow.ps1 -Source`. Installed-product verification: `scripts\test-product-flow.ps1 -Installed`.
>
> Setup may show the standard Windows unknown-publisher warning because this early open-source release does not yet have a production code-signing certificate. The project does not disable Secure Boot or enable Windows test-signing mode.

## Demo Video Plan — Maximum 2:40

The official requirement is a **public YouTube video under three minutes with audio** that explains both what was built and how Codex and GPT-5.6 were used.

| Time | Show | Narration goal |
|---:|---|---|
| 0:00–0:15 | Spare phone/BOOX beside the Windows PC | State the problem and one-line promise |
| 0:15–0:45 | QR → browser request → matching six-digit code → PC Allow | Prove the trust model and usable onboarding |
| 0:45–1:20 | Existing-monitor or virtual-display stream, touch, keyboard | Show a working non-trivial product, not slides |
| 1:20–1:45 | Sideboard and Quota on phone and e-paper layouts | Show why this is more useful than generic mirroring |
| 1:45–2:05 | Reconnect through the managed HTTPS URL or explain the route | Show cross-network value while PC approval remains authoritative |
| 2:05–2:35 | Commit range, tests/Device Lab, installer/release | Explain exactly how Codex + GPT-5.6 accelerated the Build Week extension and what remained human-owned |
| 2:35–2:40 | Product name + repository URL | End with one clear call to test it |

Do not include copyrighted music, personal account details, unsafe QR codes, reusable device credentials, or local IP addresses in the uploaded video.

## Final Human Checklist

### Required before clicking Submit

- [ ] Register/join OpenAI Build Week on Devpost and confirm eligibility/team representation.
- [ ] Merge and push the final submission branch to the public repository.
- [ ] Tag the exact tested commit (for example `v0.1.31`) and verify GitHub Release contains the Setup and `.sha256` assets.
- [ ] Download the release assets from GitHub—not the local build folder—and smoke-test them on Windows.
- [ ] Upload a public YouTube video under three minutes; verify it plays while signed out and contains audible Codex + GPT-5.6 explanation.
- [ ] Add the public YouTube URL to Devpost.
- [ ] Select **Work & Productivity**.
- [ ] Paste the repository, release/testing URL, short/long description, and testing instructions.
- [ ] Paste `/feedback` Session ID `019f6890-877f-71e0-9ffa-7cf4d4457f2a`.
- [ ] Confirm the submitted repo/video/screenshots contain no PII, secrets, unsafe QR codes, or unlicensed music/assets.
- [ ] Submit before **July 21, 2026 5:00 PM PDT / July 22 8:00 AM UTC+8**.

### Recommended final evidence

- [ ] Record the final .NET test count, Worker test count, and installer smoke result in the submission notes.
- [ ] Keep the baseline/compare links visible in README so judges can distinguish pre-event work.
- [ ] Confirm the release remains freely downloadable through the end of judging.
