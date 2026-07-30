# Buckett for Windows

A native Windows port of [Buckett](../README.md) — the open-source bucket explorer for
**Cloudflare R2** and **Backblaze B2** (and any other S3-compatible storage).

Same look, same workflows, same feature set as the macOS app, rebuilt on .NET 8 and
[Avalonia](https://avaloniaui.net/) so it renders and behaves like a Windows application.

![Buckett icon](../Resources/icon_1024.png)

## Installation

### Download

Every release carries two Windows downloads. Both contain the same app, which is
self-contained — no .NET install required.

**`Buckett-Setup-x.y.z.exe`** — the installer, and the one to pick if you're unsure. It
installs per user into `%LOCALAPPDATA%\Programs\Buckett`, adds a Start-menu entry (and
optionally a desktop shortcut), and registers an uninstaller. No administrator rights, no
UAC prompt.

**`Buckett-x.y.z-win-x64.zip`** — the portable build. Unzip anywhere you can write to and run
`Buckett.exe`. This is also the asset the in-app updater downloads.

> **Note on SmartScreen:** releases are unsigned (no paid code-signing certificate), so the
> first launch shows "Windows protected your PC". Click **More info → Run anyway**.

> **Why a per-user install:** the in-app updater replaces the app's files in place, which
> needs a writable folder. Installing under `Program Files` would demand administrator rights
> for every update, so the installer defaults to your user profile instead. If you move
> Buckett somewhere unwritable, the updater says so rather than failing silently.
>
> One consequence worth knowing: after the app updates itself, the version listed in
> **Apps & features** still shows whatever the installer put there. The app's own About tab
> is the accurate one. Running a newer installer resyncs it.

### Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10
1809 or later.

```powershell
git clone https://github.com/Mac2100/Buckett.git
cd Buckett
./windows/build/make_app.ps1                 # app folder + ZIP + installer
./windows/build/make_app.ps1 -NoInstaller    # skip the installer (no Inno Setup needed)
```

The installer step needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`choco install innosetup`); everything else needs only the .NET SDK.

For development, `dotnet run --project windows/src/Buckett` works directly, or open
`windows/Buckett.sln` in Visual Studio / Rider.

## Setting up an account

Open **Settings → Accounts** (from the sidebar footer) or run the guided wizard from
**Add Account…**:

### Cloudflare R2

1. In the Cloudflare dashboard, go to **R2 → Manage R2 API Tokens** and create a token with
   *Object Read & Write* (or *Admin Read & Write* if you want to create/delete buckets from
   the app).
2. Copy the **Access Key ID** and **Secret Access Key** into Buckett.
3. Enter your **Cloudflare Account ID** (shown on the R2 overview page). The endpoint
   `https://<account-id>.r2.cloudflarestorage.com` is derived automatically.

### Backblaze B2

1. In the Backblaze console, go to **App Keys** and create a key.
2. Copy the **keyID** (Access Key ID) and **applicationKey** (Secret Access Key).
3. Enter your **region** — the part after `s3.` in your bucket's S3 endpoint, e.g.
   `us-west-004` for `s3.us-west-004.backblazeb2.com`.

Any other S3-compatible service (AWS S3, MinIO, Wasabi, …) works too via the
**Custom Endpoint** field. Use **Test Connection** to verify credentials before saving.

## Security & privacy

- Secret access keys are stored **only** in the Windows Credential Manager (encrypted at rest
  by the OS under your user account), never in plain files or preferences.
- All requests are signed locally (AWS Signature V4) and sent **directly** to your storage
  provider over HTTPS — there is no intermediary server.
- The only other network request the app ever makes is the (optional, off-switchable) update
  check against the public GitHub Releases API.

Non-secret data lives in `%APPDATA%\Buckett`: `accounts.json`, `settings.json`,
`bucket-aliases.json`, `upload-history.json`, and upload checkpoints under `resumable\`.
The `accounts.json` format is identical to the macOS build's.

## How the platform pieces map

Everything in the macOS feature list is present. Five things are implemented differently
because Windows works differently:

| macOS | Windows |
| --- | --- |
| Keychain | Windows Credential Manager (`CredRead`/`CredWrite`, DPAPI-encrypted) |
| Menu bar icon accepts dropped files | Windows notification-area icons **cannot** receive drops, so Buckett shows a small always-on-top **desktop drop target** you can drag anywhere. It has the same hover panel of bucket drop zones and the same drop animation. The notification-area icon keeps the menu (open, pick drop buckets, active transfers, quit) and carries notifications. |
| Quick Look previews | Images and text/code preview in-window; anything else (video, audio, PDF, Office…) opens in your default Windows application from the same preview window |
| Notification Center | Notification-area balloons, which Windows 10/11 route into the Action Center |
| DMG + in-place `.app` swap | Portable ZIP; the updater downloads it, waits for Buckett to exit, swaps the install folder, and relaunches |

Two smaller differences: the macOS file picker can select files *and* folders at once, so the
**Upload** button offers "Upload Files…" and "Upload Folders…" separately (drag & drop still
takes both together); and Windows popup menus close on click, so the drop-bucket checkboxes
are toggled one visit at a time.

## CI / Releases

One workflow, `.github/workflows/build.yml`, covers both platforms:

1. **Check version** — reads the version constant from all three places it lives and fails
   the build if they disagree, so macOS and Windows can never ship under different numbers.
   On a tag, it also checks the tag matches.
2. **Build macOS** and **Build Windows** run in parallel, producing the DMG, the ZIP, and the
   installer. The Windows job runs the test suite first.
3. **Publish release** runs only for a `v*` tag (or a manual dispatch with *release* ticked)
   and attaches all three files to a single GitHub Release — which is what both in-app update
   checkers read.

To cut a release, set the same version in all three files:

- `Sources/Buckett/Support/AppVersion.swift` — `marketing`
- `windows/src/Buckett/Support/AppVersion.cs` — `Marketing`
- `windows/src/Buckett/Buckett.csproj` — `<Version>`

then tag the commit `v<version>` and push the tag. The macOS app picks the `.dmg` off that
release and the Windows app picks the `.zip`; neither can see the other's asset.

## Project layout

```
windows/
  Buckett.sln
  build/make_app.ps1            # publish + ZIP
  src/Buckett/
    Models/                     # Account, RemoteObject, BucketStats, byte formatting
    Services/                   # SigV4, S3 client, transfers, credentials, updates, tray
    ViewModels/                 # AppState, BrowserModel, ObjectItem
    Views/                      # Windows, controls, icon set, theme
    App/                        # Notification-area controller
```

The Swift and C# sources are deliberately kept structurally parallel — the same file names,
the same `// MARK:` section markers, and the same method names — so a change on one platform
is easy to mirror on the other.

## License

[MIT](../LICENSE)
