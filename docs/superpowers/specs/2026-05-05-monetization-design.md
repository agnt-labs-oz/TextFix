# TextFix Monetization Design

**Date:** 2026-05-05
**Status:** Approved (brainstorm)
**Current version:** v0.6.0

## Goal & Trajectory

Build infrastructure to validate demand, then escalate revenue ambition based on signal:

1. **Phase 1 — Validate (now → ~3 months):** ship the surfaces that let users tip, suggest features, and discover what TextFix is. No paid tier, no telemetry. The current GitHub-distributed v0.x line stays free and MIT-licensed.
2. **Phase 2 — Coffee money (~3-6 months, signal-dependent):** add channels that increase reach and donations once Phase 1 shows real activity (Ko-fi tips trickling in, Discussions used, stars climbing).
3. **Phase 3 — Real product (~6-12 months, only if Phase 2 produced real signal):** open-core split with a paid `TextFix Pro` superset distributed alongside the free build.

## Target User

Phase 1/2 audience is **mixed** (developers, knowledge workers, small teams). Phase 3 paid features will be designed to appeal across that range, with **ESL / language-learning users** flagged as a secondary expansion vector worth keeping in mind (translation and grammar-explanation modes slot cleanly into the existing modes architecture).

## OSS Strategy

**Open core.** The current GitHub repo stays MIT-licensed and free forever. Pro features eventually live in a separate closed-source build, downloaded from a website and/or the Microsoft Store. Two artifacts when Phase 3 arrives:

- `TextFix.exe` (free, GitHub releases)
- `TextFix Pro.exe` (paid, separate distribution)

Until Phase 3, there is one binary and it is the free OSS one.

---

## Phase 1: Validation (detailed design)

### In-app surfaces

**New tray menu items**, ordered after existing items and before "Quit":

```
─────────────
Suggest a feature…    →  opens GitHub Discussions "Ideas" pre-fill URL in browser
Report an issue…      →  opens GitHub Issues new-issue URL in browser
Open log folder       →  opens %APPDATA%/TextFix/logs/ in Explorer
─────────────
About TextFix…        →  opens new About window
Support TextFix ☕     →  opens Ko-fi URL in browser
─────────────
Quit
```

**About window** (~400×500, dark theme matching `SettingsWindow.xaml`):

- App icon, "TextFix" + current version (read from assembly), short tagline
- GitHub repo link
- License: MIT (link)
- **Stats panel:**
  - Lifetime corrections (count)
  - Estimated time saved (chars-corrected ÷ 200 cpm, formatted as `Xh Ym`)
  - Most-used mode + percentage
  - Per-mode breakdown (small bar chart or table)
  - This-month API spend estimate (sum of token-cost estimates)
- Prominent "☕ Tip on Ko-fi" CTA button at the bottom, linking to the Ko-fi URL

### Stats storage

- New file: `%APPDATA%/TextFix/stats.json` — JSONL, one line appended per correction:
  ```json
  {"timestamp":"2026-05-05T12:34:56Z","mode":"Fix errors","provider":"anthropic","model":"claude-haiku-4-5","chars_in":120,"chars_out":118,"tokens_in":45,"tokens_out":44,"cost_estimate":0.00021,"status":"success"}
  ```
- Local only. About window reads + aggregates on open.
- Easy to truncate if it grows; no rotation logic in Phase 1.

### Cost estimation

Hardcoded per-model rate table (Haiku 4.5, Sonnet 4.5/4.6, Opus 4.6 — the models the Settings dropdown already supports). Computed at correction time and stored alongside the event in `stats.json`. Approximate, not billing-grade.

### Logging (without bloat)

- New `Services/AppLog.cs`
- Daily-rolling file: `%APPDATA%/TextFix/logs/textfix-YYYY-MM-DD.log`, retain last 7 days
- Levels: `Info`, `Warn`, `Error`. Default: `Warn`. New setting `LogLevel` to bump up for diagnosis.
- Logged events: app start/stop, hotkey registered, correction started/completed (mode, provider, duration, status — **no text content**), errors with stack traces.
- Surfaced via the "Open log folder" tray menu item.

### README + GitHub setup

**README rewrite priorities:**

- Hero screenshot (overlay in action) at the top
- "Setup" becomes a numbered walkthrough with screenshots:
  1. Get an API key from console.anthropic.com (screenshot)
  2. Open Settings, paste the key (screenshot)
  3. Pick a model (screenshot)
  4. Try it: select text, press Ctrl+Shift+Z, see the overlay (screenshot)
- New section **"Suggest features / report bugs"** — points to GitHub Discussions + Issues
- New section **"Support TextFix"** — Ko-fi link, brief framing ("free forever; tips help cover dev time and API costs")
- New section **"Privacy"** — short statement: text is sent to your chosen AI provider, nothing is sent to the developer, no telemetry

**One-time GitHub setup (no code):**

- Enable Discussions on the repo with categories: Ideas, Bugs, Q&A, Show & Tell
- Issue template for bug reports (provider, mode, what happened, log excerpt)
- Discussion template for "Suggest a feature" with structured fields the in-app button pre-fills

### What's explicitly out of scope for Phase 1

- **Telemetry of any kind.** Local stats only. Telemetry stays on the roadmap; revisited in Phase 2/3 once trust posture is clearer.
- License keys, paid features, hosted backend, Microsoft Store, GitHub Sponsors button, landing page — all Phase 2+.

---

## Phase 2: Coffee Money (sketch, ~3-6 months)

Entry triggers: Ko-fi tips arriving, Discussions has real activity, stars climbing, 1-2 most-requested features shipped.

**Likely scope:**

- **OpenAI provider** — predicted #1 ask. Slot into the service abstraction; same modes, different backend.
- **Custom modes UI** — already on the roadmap; user-defined system prompts.
- **GitHub Sponsors button** — zero fees, sits next to Ko-fi on the repo.
- **Microsoft Store listing** — same exe, much better discoverability for non-developers. ~$20 one-time MS Partner Center fee, ~1 day of repackaging.
- **Reconsider opt-in telemetry** — by now the trust posture is clearer; decide based on Phase 1 audience.
- **Simple landing page** (e.g. `textfix.app`) — hero, screenshots, download CTA, Ko-fi link. Static site, half a day.

**Deferred decisions:** custom modes vs OpenAI provider priority — let Discussions activity decide.

---

## Phase 3: Pro Tier (sketch, ~6-12 months, only if Phase 2 signal warrants)

This phase is intentionally a sketch. Real spec to be written after Phase 2.

**Open-core split:**

- `TextFix` (free, MIT, GitHub) — current product, maintained.
- `TextFix Pro` (paid, closed-source, website + MS Store) — superset.

**Candidate Pro features** (rank later by user demand):

- **Translation + language-learning modes** — translate selection, explain grammar, suggest fluent rewrites. Marketable on its own to the ESL audience.
- **Multi-provider routing** — auto-pick fastest/cheapest provider per mode, with failover.
- **No-key-needed hosted mode** — Pro subscribers use the developer's API key via a backend; subscription covers API cost + margin. Opens TextFix to non-developers.
- **Cloud-synced custom modes + history** — define on one machine, available on all.
- **Team modes / "house style"** — small-team angle, per-seat pricing.
- **Stats dashboards + export** — Phase 1 stats but with charts, history, CSV export.
- **Priority support** — email + faster bug fixes.
- **Auto-update + signed installer** — non-dev users hate downloading exes from GitHub.

**Pricing instinct (placeholder, validate later):**

- One-time license: $25-40
- Subscription: $4-6/mo or $40/yr (if hosted-backend feature is included)
- Team: $3/seat/mo, 5-seat minimum

**Distribution:**

- Website (Stripe checkout → license key emailed → enter in Pro app)
- Microsoft Store (handles billing, takes a cut, much higher discoverability)
- Both, eventually

**Explicitly NOT designed in this spec:**

- License key validation / piracy prevention — defer until Phase 3 spec.
- Backend architecture for hosted mode — defer until commitment.
- Team feature mechanics — defer until a real team asks.

---

## Resolved Implementation Inputs

- **Ko-fi URL:** `https://ko-fi.com/3smallwins`
- **Discussions categories:** Ideas, Bugs, Q&A, Show & Tell
- **Per-model cost rates:** pull current published prices for the supported models (Haiku 4.5, Sonnet 4.5/4.6, Opus 4.6) at implementation time.
