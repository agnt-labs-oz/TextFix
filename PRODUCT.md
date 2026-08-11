# Product

> Derived from the repo (README, CLAUDE.md) and the owner's brief for the marketing
> page ("gently sell the app, show its capabilities with screenshots, keep it
> simple"), 2026-08-12, without a live interview — the owner was away and asked for
> the work to proceed. Correct anything here that reads wrong; downstream design
> work treats this file as authoritative.

## Register

brand

*(Scope note: this file governs the marketing page under `docs/`. The product
itself is a WPF desktop app whose UI conventions live in CLAUDE.md, not here.)*

## Users

Windows users who type all day in other people's apps — Teams, Outlook, editors,
browsers — and want their writing cleaned up without leaving them. They arrive at
the page from a GitHub link or word of mouth, mid-workday, and decide in under a
minute whether a small tray utility is worth installing. A meaningful subset cares
specifically about privacy and picks it because it can run fully local.

## Product Purpose

TextFix fixes your writing where you wrote it: select text anywhere, press one
hotkey, review a diff, apply. The page exists to get a visitor to download the
installer from GitHub Releases — and to let the privacy-minded see, quickly, that
the local/Ollama path means their text never leaves the machine.

## Brand Personality

Quiet, practical, trustworthy. The app is a small dark utilitarian tool that
respects its user; the page should feel like the app, not like a launch. It sells
by demonstrating, never by claiming. Three words: competent, unhurried, honest.

## Anti-references

- The modal SaaS landing page: gradient hero, big vanity metrics, logo walls,
  fabricated testimonials, "supercharge your productivity" copy.
- Anything that oversells — the owner's word was "gently".
- Feature-grid card walls with an icon above every heading.
- Claims the app can't keep (no "AI-powered perfection"; the README openly
  documents that a 3B local model makes rough edits).

## Design Principles

1. **Show the correction, don't describe it.** The product's output — a diff of
   broken text becoming fixed text — is the most persuasive asset available. Lead
   with it.
2. **The page inherits the app's world.** Dark surface, the app's violet, real
   screenshots. A visitor should feel they've already seen the product by the time
   they download it.
3. **Honesty is the differentiator.** Local-first privacy, bring-your-own-key,
   open source, measured hardware guidance. Say what it costs and where the rough
   edges are; that is what earns the install from this audience.
4. **One idea per fold, short page, fast.** No build step, no framework, minimal
   JS. The page is a utility about a utility.

## Accessibility & Inclusion

WCAG AA: body text ≥4.5:1 against its background, large text ≥3:1. Every
animation has a `prefers-reduced-motion` fallback that lands on the finished
state. Keyboard-reachable links and visible focus. Alt text written as voice, not
filler.
