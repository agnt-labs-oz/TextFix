# Task 13 — In-app Ollama setup helper

**Goal.** A user who picks Ollama and has nothing installed gets walked from zero to a
working local model without leaving TextFix: download → verify → install → detect →
pull a model → ready.

**Origin.** User request: "well actually be good if the app could do the download for
the user or at least tell them how." User chose **direct download from ollama.com**
over winget after the trust trade-off was stated explicitly. That decision binds this
design: the download must be HTTPS from the official host, and the installer's
**Authenticode signature must be verified before launch**.

## Ground truth (measured 2026-08-11, this machine, Ollama 0.32.6/0.32.7)

- Installer URL: `https://ollama.com/download/OllamaSetup.exe`. Observed redirect
  chain, all HTTPS: 307 → `github.com/ollama/ollama/releases/latest/download/…`
  → 302 → 302 → `release-assets.githubusercontent.com` → 200.
  **Content-Length 1,563,939,896 bytes (1.46 GB, v0.32.7)** — the dialog must state
  this before starting; it is not a casual download.
- Signer of shipped binaries: subject `CN=Ollama Inc., O=Ollama Inc., L=Toronto,
  S=Ontario, C=CA`, issued by DigiCert, embedded signature, status Valid. The
  verifier pins the CN **Ollama Inc.** (extracted via `GetNameInfo`, exact match —
  not a substring test on the DN) on top of chain validity.
- Server: `http://localhost:11434`, `GET /api/version` answers when up. The installer
  starts the app on completion and registers launch-at-login; no admin rights needed
  (per-user install to `%LOCALAPPDATA%\Programs\Ollama`).
- Model pull: `POST /api/pull {"model":"…"}` (0.32 also accepts legacy `"name"`).
  Captured NDJSON stream: `{"status":"pulling manifest"}` → per-layer
  `{"status":"pulling <id>","digest":"sha256:…","total":N,"completed":N}` (one layer
  carries ~99% of the bytes) → `{"status":"verifying sha256 digest"}` →
  `{"status":"writing manifest"}` → `{"status":"success"}`.
  **A failed pull is an `{"error":"…"}` line inside an HTTP 200 stream**, e.g.
  `{"error":"pull model manifest: file does not exist"}` — the parser must treat that
  line as failure; the status code says nothing.
- `GET /api/tags` → `{"models":[{"name":"llama3.2:3b","model":"…","size":N,…}]}`.
- Hardware reality (measured): llama3.2:3b is usable on CPU (~2s warm); gemma4:26b is
  not (100% CPU, timed out on a paragraph). The helper must be honest about this.

## Flow — one dialog, one state machine

Entry point: **"Set up Ollama…" button in Settings**, visible when the Ollama
provider is selected. Shown alongside the existing "Cannot reach … is Ollama
running?" failure hint. No tray entry point in v1.

States (each shows a progress line; Cancel is live throughout):

1. **Detect.** `GET /api/version`, 2 s budget.
   - Up → jump to (5).
   - Down but `ollama app.exe` exists on disk → offer **Start Ollama**, launch it,
     poll until up.
   - Not installed → offer **Download** (state the size and the URL before starting).
2. **Download.** HttpClient to the fixed HTTPS URL, streamed to a unique file under
   `%TEMP%`, progress from Content-Length, cancellable.
3. **Verify.** WinVerifyTrust (`LibraryImport`, matching the repo's P/Invoke idiom)
   must report a valid embedded signature, **then** the certificate subject must
   carry `CN=Ollama Inc.`. Any failure: delete the file, show the reason, stop.
   Launch is unreachable except through this state.
4. **Install.** Launch the installer **interactively** — the user sees and clicks
   through the real installer UI. Transparency over silence: this app just asked for
   trust; hiding the install would spend it. Poll `/api/version` while the wizard
   runs (up to 5 min) — the installer starts Ollama itself when it finishes.
5. **Model.** Server up: `GET /api/tags`. If a model is already present, select it
   and finish. If none: offer a pull with two curated choices and the measured
   hardware guidance — `llama3.2:3b` (2 GB, "works on any machine, occasional rough
   edit") preselected, `qwen2.5:7b` (4.7 GB, "better quality, wants a GPU").
   NDJSON progress, cancellable.
6. **Done.** Write the pulled model into the Ollama provider config if it has none,
   flag `SettingsChanged` so the existing rebuild path picks it up, prompt the user
   to hit Test connection.

## New code

| File | Contents |
|---|---|
| `Interop/NativeMethods.cs` | WinVerifyTrust + WINTRUST_DATA/WINTRUST_FILE_INFO (append to existing file) |
| `Services/AuthenticodeVerifier.cs` | `Verify(path, requiredSubjectCn)` → chain validity via WinVerifyTrust, subject pin via `X509Certificate.CreateFromSignedFile` |
| `Services/OllamaSetup.cs` | Detect / download / launch / poll / list / pull. HttpMessageHandler injectable, same pattern as `OpenAiCompatibleProvider` |
| `Views/OllamaSetupDialog.xaml(.cs)` | The state machine UI, dark theme |
| `Views/SettingsWindow.xaml(.cs)` | The entry button |

## Security invariants

- Fixed URL, HTTPS. No user-supplied download location.
- **Verify before launch, always.** Failed verification deletes the file.
- No elevation; the Ollama installer is per-user.
- The downloaded file goes to `%TEMP%` with a unique name and is deleted after the
  installer exits (or on any failure).

## Testing

- `OllamaSetup` against a stubbed handler: version detection, download streaming +
  progress, NDJSON pull-progress parsing, tags listing. Same stub idiom as the
  provider tests.
- `AuthenticodeVerifier`: unsigned file → rejected; missing file → rejected; wrong
  CN on a validly-signed file → rejected (uses the installed `ollama.exe` when
  present, skips silently when not — CI has no signed fixture).
- Dialog wiring: WPF, verified by hand per the standing testing note.

## Decisions taken without asking (flagged, not blocking)

- Interactive installer launch rather than silent — transparency argument above.
- Settings-only entry point — the tray path already names Settings as the fix.
- Two curated models rather than a free-text pull box — the Settings model field
  already covers arbitrary models once the server is up.
