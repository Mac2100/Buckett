# Buckett for Windows — feasibility and effort estimate

Assessment of what it would take to ship a Windows version of Buckett with feature
and workflow parity to the macOS app at v1.7.4.

**Short answer:** technically low-risk but broad. Roughly **120–180 developer-days
(~6–9 months for one full-time developer, ~3.5–5 months for two)** for true parity.
About 55% of that is rebuilding the UI, which cannot be ported at all. Three features
— the menu-bar drop target, Quick Look previews, and the in-place self-updater — have
no Windows equivalent and require redesign rather than translation. Ongoing
maintenance would run roughly **1.7–1.9×** a single-platform project.

---

## 1. What the codebase actually is

8,675 lines of Swift, no third-party dependencies, split across four layers with
very different portability characteristics.

| Layer | Files | LOC | Portability |
|---|---|---|---|
| **Protocol / business logic** | `SigV4`, `S3Client`, `S3XML`, `Models`, `UpdateChecker`, `UploadHistory`, `BucketAliases`, `AccountStore`, `AppVersion` | ~1,660 | **High** — pure algorithm + HTTP + XML. Semantics transfer 1:1. |
| **View models** | `AppState`, `BrowserModel` | ~890 | **Medium** — structurally portable; touches `NSPasteboard`, `NSApp`. |
| **UI** | 13 SwiftUI files (`BrowserView`, `SidebarView`, `SettingsView`, `OnboardingView`, `StatisticsView`, `DashboardView`, `ObjectViews`, `Sheets`, `MoveSheet`, `TransfersView`, `ContentView`, `Theme`, `Toast`) | ~3,930 | **Zero** — SwiftUI does not exist on Windows. Full rewrite. |
| **macOS platform glue** | `MenuBar`, `SelfUpdater`, `Keychain`, `Notifier`, `ThumbnailLoader`, `Panels`/`FileDrop`, `QuickLookPreview` | ~1,100 | **Zero to negative** — several have no Windows analogue at all. |

The good news is that the S3 layer is hand-rolled (SigV4 signing, a bespoke XML tree
parser, manual multipart orchestration) with no SDK dependency. On Windows you can
delete most of it and use `AWSSDK.S3`, which is a net simplification.

The bad news is that ~5,000 of 8,675 lines are UI and platform glue, and that is the
part that has to be written from scratch.

---

## 2. Recommended technology stack

| Option | Verdict |
|---|---|
| **Avalonia UI + C# / .NET 8** | **Recommended.** Mature, good control set, XAML+MVVM maps cleanly onto the existing `ObservableObject` view models, unpackaged deployment is easy, and Velopack (auto-update) integrates first-class. FluentTheme looks close enough to native. |
| **WinUI 3 / Windows App SDK + C#** | Best choice if native Fluent look-and-feel is the priority, and the strongest option for shell/tray/toast/MSIX integration. Costs: WinUI 3 is still rough in places (runtime app-level theme switching is awkward), packaging is more involved, and the free control ecosystem is thinner. |
| **WPF + C#** | Lowest risk, most mature, largest ecosystem — but dated visuals; needs WPF-UI or ModernWpf to not look like 2012. A reasonable conservative pick. |
| **Electron / Tauri + TypeScript** | Fastest UI rebuild, biggest departure from native feel, heavier runtime. Only compelling if the plan is to eventually collapse both platforms into one codebase. |
| **Swift on Windows** | **Not recommended.** The toolchain exists, but SwiftUI does not, Combine does not (OpenCombine is a partial substitute), CryptoKit does not (swift-crypto substitutes), and Security.framework does not. You would preserve ~1,660 lines of logic, rewrite everything else anyway, and take on an immature GUI story to do it. Worst of both worlds. |

The estimates below assume **Avalonia or WinUI 3 with C#, `AWSSDK.S3`, and Velopack.**

### Note on `AWSSDK.S3` with R2 / B2

Swapping the hand-rolled client for the SDK removes ~1,000 lines but is not free:

- R2 needs `ForcePathStyle = true` and signing region `auto` (matches
  `Account.signingRegion` today).
- Recent AWSSDK versions send integrity checksums by default; R2, B2 and MinIO have
  historically rejected them. Expect to set
  `RequestChecksumCalculation = WhenRequired`.
- The provider quirks already encoded in `S3Client.swift` must be re-verified, not
  assumed: `CopyObject` and `CompleteMultipartUpload` returning HTTP 200 with an
  error body (`S3Client.swift:398`, `:671`), B2's hidden object versions blocking
  bucket deletion (`listAllObjectVersions`, `:463`), and orphaned multipart uploads
  blocking deletion (`listMultipartUploads`, `:530`). These were clearly learned the
  hard way and represent real institutional knowledge in this file.
- `TransferUtility` does **not** persist part state across process restarts. The
  resumable-upload behaviour in `TransferManager.swift:291` — a JSON checkpoint per
  part, surviving app relaunch, with `NoSuchUpload` recovery — has to be
  reimplemented by hand on top of the low-level multipart API.

---

## 3. Platform gap analysis

### 3.1 Straightforward (days, not weeks)

| macOS | Windows | Notes |
|---|---|---|
| `NSOpenPanel` (`BrowserView.swift:525`) | `FileOpenPicker` / `CommonOpenFileDialog` | Trivial. |
| `NSPasteboard` (3 sites) | `Clipboard` / `DataPackage` | Trivial. |
| `NSWorkspace.activateFileViewerSelecting` | `explorer.exe /select,"path"` | Trivial. |
| `NSWorkspace.icon(forFile:)` | `SHGetFileInfo` / `IShellItemImageFactory` | Easy. |
| Keychain (`Keychain.swift`, 45 LOC) | DPAPI `ProtectedData` or Credential Manager | Easy — see caveat in §3.3. |
| `UNUserNotification` (`Notifier.swift`) | `AppNotificationBuilder` (Windows App SDK) | Easy; needs an AUMID / package identity, or a registered COM server when unpackaged. |
| `SMAppService` open-at-login (`SettingsView.swift:451`) | `Run` registry key, Startup shortcut, or `StartupTask` in the manifest | Easy. |
| Swift Charts (2 charts) | LiveCharts2 / ScottPlot / OxyPlot | Easy–medium. |
| `NSAppearance` light/dark override (`Theme.swift:116`) | `RequestedTheme` | Easy in Avalonia; awkward at runtime app-wide in WinUI 3. |
| Window drag & drop (`BrowserView.swift:41`) | `DragOver`/`Drop` with `StorageItems` | Easy–medium. Watch the UAC integrity-level mismatch: an elevated app cannot receive drops from non-elevated Explorer. |

### 3.2 Tedious (weeks)

**SF Symbols → Windows icons.** 48 distinct symbols across 95 use sites. There is no
1:1 mapping. Segoe Fluent Icons covers maybe two-thirds; the rest need substitutes or
custom SVG. Also note Segoe Fluent Icons ships with Windows 11 — on Windows 10 you get
Segoe MDL2 Assets, which differs. If Windows 10 is in scope, bundle your own icon font.

**`UniformTypeIdentifiers` → nothing.** `Models.swift:135-158` leans on UTType's
conformance tree (`conforms(to: .image)`, `.movie`, `.audio`, `.sourceCode`,
`.archive`) for file-kind detection, icon selection, MIME types for uploads, and
thumbnail eligibility. Windows has no conformance hierarchy. You need a MIME map
(MimeKit or similar) plus a hand-written category table. Not hard, just fiddly and
easy to get subtly wrong.

### 3.3 Genuinely different, needs a decision

**Credential storage — a real security-posture change.** The README's central promise
is "secret keys are stored exclusively in the macOS Keychain, encrypted at rest by the
OS." The Windows equivalents are not equivalent in the way that sentence implies:

- **DPAPI (`ProtectedData`, `CurrentUser` scope)** encrypts at rest, but the key is
  derived from the user's logon credentials — *any process running as that user can
  decrypt it.* macOS Keychain items, by contrast, can be ACL-bound to the signed
  application.
- **Windows Credential Manager** (`CredWrite`/`CredRead`) is the closer analogue and
  what I would use — it is the OS-blessed store, integrates with the Credential
  Manager UI, and the 2,560-byte secret limit is irrelevant for API keys. It still
  does not give per-application ACLs the way Keychain does.

Either way the marketing copy must be adjusted for Windows. Do not copy the macOS
security claim verbatim.

### 3.4 Hard — no Windows equivalent

These three are where the schedule risk lives.

#### (a) Menu-bar drop target — the flagship feature

`MenuBar.swift` is 650 lines: an `NSStatusItem` that accepts file drops directly on
the icon, shows a hover panel of per-bucket drop zones across accounts, and plays a
file-falls-into-bucket animation. It is arguably Buckett's signature interaction.

**The Windows notification-area (tray) icon cannot accept file drops.** There is no
shell mechanism to route a drag-and-drop onto a tray icon; the shell simply does not
deliver drop events there. This is not a "hard to implement" — it is not available.

Redesign options, ranked:

1. **Floating always-on-top drop window** — a small toggleable panel, summoned from
   the tray, that accepts drops and expands into the per-bucket picker on hover. This
   preserves the workflow and the animation most faithfully. *~5–8 days.*
2. **Explorer integration** — a "Send to → Buckett" shortcut (cheap, ~1 day) and/or a
   right-click "Upload to Buckett ▸ <bucket>" context menu. The context menu is
   arguably *better* than the macOS workflow, but on Windows 11 the modern context
   menu requires an MSIX-packaged sparse-package COM extension, which is a
   meaningfully bigger lift. *~1 day for SendTo, ~5–8 days for the shell extension.*
3. **Taskbar Jump List** with bucket entries — cheap, but Jump Lists do not accept
   drops either, so it complements rather than replaces.

Recommendation: option 1 plus a SendTo shortcut for v1; consider the shell extension
later. Budget **8–14 days** including the animation and cross-account bucket picker.

#### (b) Quick Look previews

`Sheets.swift:6` wraps `QLPreviewView` in ~15 lines and gets previews for images,
video, audio, PDF, text, code, and more — for free, from the OS. Windows has no
equivalent API. You must build a preview subsystem per type:

- Images → `Image` control (plus HEIC/AVIF/TIFF decoding gaps to fill)
- Video/audio → `MediaPlayerElement` / LibVLCSharp
- PDF → WebView2 or PdfiumViewer
- Text/code → AvaloniaEdit or similar, with syntax highlighting
- Everything else → graceful "no preview available" fallback

15 lines of Swift becomes ~1,500 lines of C# and a pile of dependencies. Budget
**8–14 days**, and accept that coverage will be narrower than the macOS app's.

#### (c) In-place self-update

`SelfUpdater.swift` downloads the release DMG, mounts it with `hdiutil`, `ditto`s the
new bundle over the running app, rolls back on failure, and relaunches. **None of this
translates.** Windows memory-maps and locks a running executable — you cannot
overwrite it in place. (You *can* rename it and drop a new one alongside, which is
exactly the trick Chrome, Squirrel and Velopack exploit.)

The right answer is not to port this at all:

- **Velopack** (successor to Squirrel.Windows) — delta updates, handles the
  rename-and-swap dance, works unpackaged, integrates with GitHub Releases, which is
  already what `UpdateChecker.swift` polls. **Recommended.**
- **MSIX + App Installer** — clean install/uninstall and an OS-managed update channel,
  but mandates code signing and complicates the tray/startup story.
- **WinSparkle** — closest philosophically to the current design, C-based.

Budget **4–6 days** with Velopack. The upside: the resulting mechanism is more robust
than the current DMG-swapping approach.

---

## 4. Packaging, signing, distribution

| Concern | macOS today | Windows |
|---|---|---|
| Format | `.app` bundle in a `.dmg` (`scripts/make_app.sh`) | MSIX, or MSI/Inno Setup for unpackaged |
| Signing | Ad-hoc (`codesign --sign -`), no paid cert | See below |
| Gate | Gatekeeper: right-click → Open, one time | **SmartScreen** |
| CI | `macos-14` runner, one job | `windows-latest` runner, second job |

**SmartScreen is a worse problem than Gatekeeper**, and this deserves flagging as a
project cost rather than a technical one. An unsigned Windows binary produces "Windows
protected your PC — Unknown publisher", and the bypass is less discoverable than
macOS's right-click-Open. The mitigations:

- **OV certificate (~$200–400/yr)** — signs the binary, but SmartScreen reputation
  accrues per-certificate over download volume. New certs still warn initially.
- **EV certificate (~$300–600/yr, hardware token or cloud HSM required)** — grants
  immediate SmartScreen reputation. This is effectively the price of a frictionless
  first-run experience on Windows.

That is a **recurring cash cost the project does not currently have** (Buckett ships
ad-hoc signed with no Apple Developer account). For a free, donation-funded project
this is a genuine decision point, not a line item.

CI itself is easy: add a `windows-latest` job to `.github/workflows/build.yml`
producing an installer artifact, and extend the tag-triggered release step to attach
both. `UpdateChecker.swift:89` currently filters release assets by `.dmg` suffix, so
the Windows client needs its own asset-matching logic.

---

## 5. Effort estimate

One experienced Windows developer already fluent in C# and the chosen UI framework.

| Workstream | Days |
|---|---|
| Project setup, DI, MVVM scaffolding, CI | 3–5 |
| S3 layer on AWSSDK.S3 + R2/B2/S3/custom provider quirks | 5–8 |
| Resumable multipart with on-disk part checkpoints | 4–6 |
| Transfer queue, speed smoothing, retry/cancel | 4–5 |
| Credential storage + account store | 2–3 |
| **Browser UI** (grid, list, breadcrumbs, selection, sort/filter, action bar) | 12–18 |
| **Sidebar** (accounts, buckets, all-accounts, aliases, context menus) | 7–10 |
| **Settings** (5 tabs) + onboarding wizard | 8–12 |
| Statistics + dashboard + charts + activity heatmap | 6–9 |
| Transfers view | 3–4 |
| Move sheet + conflict handling, batch rename, other sheets | 6–8 |
| Toasts + theming (6 accents, light/dark) | 4–6 |
| **Preview subsystem** (Quick Look replacement) | 8–14 |
| Icon set: 48 SF Symbols → Fluent/custom | 2–4 |
| Thumbnails + caching | 2–3 |
| Window drag & drop + folder-preserving expansion | 3–4 |
| **Tray + drop-target redesign** | 8–14 |
| Toast notifications + open-at-login | 2–3 |
| Packaging (MSIX or MSI), icons, associations | 3–5 |
| **Auto-update** (Velopack) + release CI | 4–6 |
| Code signing setup | 1–2 |
| QA: Win 10 22H2 / Win 11, x64 + ARM64, HiDPI, multi-monitor, long paths, non-ASCII keys | 8–12 |
| Buffer / bug tail / parity polish (~20%) | 15–20 |
| **Total** | **120–181** |

### Calendar

| Configuration | Duration |
|---|---|
| One full-time developer | **6–9 months** |
| Two developers (UI ∥ services/platform) | **3.5–5 months** |
| Current maintainer learning the Windows stack first | add **4–8 weeks** |

One structural caveat: Buckett reached v1.7.4 over ~18 incremental releases. A parity
port has to land the *entire current* feature surface at once, which is why the number
is larger than the original development probably felt.

### Difficulty rating: **medium-high**

No hard algorithms, no exotic constraints, no performance cliffs. The difficulty is
breadth, and it concentrates in exactly three places (§3.4). Everything else is grind.

---

## 6. What parity actually means

**Achievable at parity or better:** all S3 operations, multi-account handling,
transfers and resumable uploads, statistics, presigned share links, batch operations,
move/rename, onboarding, themes, settings. Explorer integration could genuinely beat
the Finder experience.

**Cannot be literal, needs redesign:**

| macOS behaviour | Windows outcome |
|---|---|
| Drop files on the menu-bar icon | Floating drop window + SendTo/shell integration. Different gesture, same job. |
| Quick Look previews for "common file types" | Per-type viewers; narrower coverage. |
| `⌘,` Settings scene, Dock reopen, app survives last window closing | Tray-resident app, `Ctrl+,`, tray "Open Buckett". Windows users accept this idiom. |
| DMG self-swap update | Velopack. Functionally better, mechanically unrecognisable. |
| Gatekeeper right-click-Open workaround | SmartScreen — needs a paid certificate to be comparable. |
| "Keys stored in the macOS Keychain" | Credential Manager / DPAPI. Weaker per-app isolation; wording must change. |

---

## 7. Ongoing maintenance

**Roughly 1.7–1.9× a single-platform project.** Design, product decisions and the S3
protocol layer amortise across both; every UI feature gets written twice in two
languages with two idioms, and QA doubles.

Concretely, a Windows port adds:

- A second release pipeline, second signing identity (with **annual certificate
  renewal and hardware-token handling** — a chore macOS ad-hoc signing avoids
  entirely), and a second update mechanism.
- A larger QA matrix: Windows 10 22H2 (out of support since Oct 2025 but still widely
  deployed), Windows 11 22H2 through 24H2, x64 and ARM64, DPI scaling 100–200%, and
  Explorer shell behaviour that varies across all of it.
- **Version drift, which is the real risk for a small team.** The macOS app shipped 18
  releases to reach 1.7.4. Keeping a Windows build in lockstep means either freezing
  macOS feature work during the port, or accepting that Windows permanently lags —
  and a permanently-lagging second platform generates support burden without
  generating goodwill.
- **Protocol divergence.** If Windows uses AWSSDK while macOS keeps hand-rolled SigV4,
  provider bugs will not reproduce across platforms, and each fix lands twice. A
  shared core (Rust via FFI, realistically — Swift-on-Windows is too fragile) would
  solve this, but for an 8.7k-line app a shared core costs more than it saves.
  **Accept the duplication.**

---

## 8. Alternatives worth weighing first

**1. Rewrite once, cross-platform, retire the SwiftUI app.**
Avalonia or Tauri for both platforms. Costs ~30–50% more up front than the Windows
port alone (**~8–13 months**, since macOS gets rebuilt too), but ongoing maintenance
drops from ~1.8× to ~1.15×. Break-even is roughly 18–30 months out. For a
single-maintainer project this is the strongest *long-term* argument — but it means
discarding a polished, working native app, and Avalonia on macOS still needs
hand-written glue for menu-bar extras, Quick Look and Keychain, so some of the
platform work reappears on the other side.

**2. Ship a reduced-scope Windows v1.** ⭐ *My actual recommendation.*
Browser, transfers, accounts, settings, auto-update — drop the drop-target, Quick
Look previews, and statistics from v1. That is roughly **60–80 days (~3–4 months
solo)** and covers the workflows most users actually reach for. Add the
differentiators once there is evidence Windows users want them. This converts a
6–9 month bet into a 3–4 month experiment.

**3. Do nothing, and say so.**
Worth naming honestly: Buckett's differentiator on macOS is native polish in a thin
field. On Windows the field is crowded — WinSCP, Cyberduck, S3 Browser, rclone with a
GUI. A Windows Buckett would need to compete on merit rather than on being the only
pleasant option, and it would do so while consuming most of the maintainer's capacity
for a year.

---

## 9. Recommendation

If Windows support is the goal: **Avalonia + C# + AWSSDK.S3 + Velopack, scoped as a
reduced v1 (option 2)**, treating the drop-target redesign and preview subsystem as
explicitly deferred v2 work.

Budget **3–4 months** to a useful Windows 1.0 and **6–9 months** to genuine parity,
plus **~$300–600/year** for a code-signing certificate and a permanent ~1.8×
multiplier on all future feature work.
