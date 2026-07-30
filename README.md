# Buckett

[![Star on GitHub](https://img.shields.io/github/stars/Mac2100/Buckett?style=flat&logo=github&label=Star)](https://github.com/Mac2100/Buckett)

An open-source, native macOS bucket explorer for **Cloudflare R2** and **Backblaze B2** (and any other S3-compatible storage), built with SwiftUI.

Buckett is a free, open alternative to paid closed-source R2 clients: your credentials stay in your macOS Keychain, every API request goes directly from your Mac to your storage provider, and you can read every line of code that touches them.

![Buckett icon](Resources/icon_1024.png)

## Features

- **Visual bucket explorer** — browse buckets and folders with breadcrumb navigation, in a grid view (with image thumbnails, hover previews, and selection badges) or a list view with columns.
- **Guided onboarding** — a step-by-step wizard (provider → connection → credentials → test) gets a new account working in under a minute.
- **Drag & drop uploads** — drop files or entire folders anywhere in the browser; folder structure is preserved.
- **Batch operations** — multi-select to upload, download, delete, move, or batch-rename (find & replace) many objects at once via a floating action bar. Folder download/delete/rename recurse automatically.
- **Move files** — server-side move (copy + delete) into any folder or typed path, with Skip / Replace / Rename conflict handling.
- **Share links** — copy a time-limited presigned URL for any object (works without enabling public bucket access).
- **Resumable uploads** — files ≥ 16 MB use S3 multipart uploads; progress is checkpointed after every part, so an interrupted or failed upload resumes from the last completed part when retried (even after relaunching the app).
- **Previews** — Quick Look previews for common file types (images, video, audio, PDF, text, code, and more).
- **File metadata** — inspect content type, size, ETag, storage class, and custom `x-amz-meta-*` metadata.
- **Sorting & filtering** — sort by name, size, date, or kind; filter the current folder by name.
- **Statistics** — per-bucket usage cards (storage, objects, estimated monthly cost), a local upload-activity heatmap, largest objects, and a size-by-file-type chart — plus an account-wide overview dashboard.
- **Transfers view** — filterable queue (queued / active / completed / failed) with live speed, multipart part progress, retry/resume, and share-link or reveal-in-Finder actions.
- **Multiple accounts** — manage any number of R2 and B2 accounts and switch between them from the sidebar.
- **Local-only encrypted credential storage** — secret keys are stored exclusively in the macOS Keychain (encrypted at rest by the OS). No telemetry, no third-party servers, no license checks.
- **One-click updates** — optional check against GitHub Releases at launch plus "Check for Updates…" in the app menu; installing an update downloads, swaps the app in place, and relaunches automatically.
- **Themes** — six accent themes and a System/Light/Dark appearance override (Settings → Appearance).
- **Menu bar drop target** — drag files onto the bucket icon in the menu bar to upload them instantly (with a little drop-into-the-bucket animation); pick the target bucket from the icon's menu.

## Installation

### Download

Grab the latest `Buckett-x.y.z.dmg` from [Releases](https://github.com/Mac2100/Buckett/releases), open it, and drag **Buckett** into **Applications**.

> **Note on Gatekeeper:** releases are ad-hoc signed (no paid Apple Developer certificate), so the first launch requires right-clicking the app → **Open**, or:
> ```bash
> xattr -d com.apple.quarantine /Applications/Buckett.app
> ```

### Build from source

Requires Xcode 15+ / Swift 5.9+ on macOS 14 or later.

```bash
git clone https://github.com/Mac2100/Buckett.git
cd Buckett
./scripts/make_app.sh          # produces dist/Buckett.app and dist/Buckett-<version>.dmg
```

For development, `swift run` works directly, or open `Package.swift` in Xcode.

## Setting up an account

Open **Settings → Accounts** (⌘,) and add an account:

### Cloudflare R2

1. In the Cloudflare dashboard, go to **R2 → Manage R2 API Tokens** and create a token with *Object Read & Write* (or *Admin Read & Write* if you want to create/delete buckets from the app).
2. Copy the **Access Key ID** and **Secret Access Key** into Buckett.
3. Enter your **Cloudflare Account ID** (shown on the R2 overview page). The endpoint `https://<account-id>.r2.cloudflarestorage.com` is derived automatically.

### Backblaze B2

1. In the Backblaze console, go to **App Keys** and create a key.
2. Copy the **keyID** (Access Key ID) and **applicationKey** (Secret Access Key) into Buckett.
3. Enter your **region** — the part after `s3.` in your bucket's S3 endpoint, e.g. `us-west-004` for `s3.us-west-004.backblazeb2.com`.

Any other S3-compatible service (AWS S3, MinIO, Wasabi, …) works too via the **Custom Endpoint** field.

Use **Test Connection** to verify credentials before saving.

## Security & privacy

- Secret access keys are stored **only** in the macOS Keychain, never in plain files or preferences.
- All requests are signed locally (AWS Signature V4) and sent **directly** to your storage provider over HTTPS — there is no intermediary server.
- The only other network request the app ever makes is the (optional, off-switchable) update check against the public GitHub Releases API.

## CI / Releases

Every push and pull request builds the app and uploads a DMG artifact via GitHub Actions. Pushing a tag like `v1.2.0` additionally creates a GitHub Release with the DMG attached — which is what the in-app update checker looks at.

To cut a release: bump `AppVersion.marketing` in `Sources/Buckett/Support/AppVersion.swift`, then tag the commit `v<version>` and push the tag.

## Support

Buckett is free and open source. If it saves you a trip to the R2 or B2 dashboard, the easiest way to help is a
**[star on GitHub](https://github.com/Mac2100/Buckett)** — it costs nothing and helps other people
find the app. If you'd rather say thanks with a coffee:

<a href="https://www.buymeacoffee.com/Mac2100" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me a Coffee" style="height: 60px !important;width: 217px !important;" ></a>

## License

[MIT](LICENSE)
