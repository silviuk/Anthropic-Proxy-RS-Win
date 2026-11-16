# Microsoft Store submission

The EXE installer supports per-user silent installation and removal. Its system light/dark appearance is selected at the start of installation or removal.

Microsoft requires an enrolled Partner Center publisher and an app reservation. An EXE/MSI submission also requires the installer and all packaged PE files to be signed with a certificate chaining to a Microsoft-trusted certificate authority. A self-signed test certificate does not meet that requirement.

The build is not automatically submitted to the Store. Do not label an unsigned installer as Store-ready or submit it as a signed package. The alternative MSIX route requires the exact package identity from Partner Center, Store-compatible startup integration and package validation before submission.

For an EXE submission, sign `Anthropic-Proxy-RS-Win.exe` and `anthropic-proxy.exe`, rebuild the installer with Inno Setup's signing configuration for its uninstaller, then sign the final setup executable. Re-run install/uninstall tests and publish a new immutable versioned installer URL. Never replace an installer URL after submitting its checksum to WinGet or the Store.

Prepared listing:

- Product name: Anthropic-Proxy-RS-Win
- Publisher: use the verified Partner Center publisher
- Category: Developer tools / Utilities
- Short description: A native Windows app for configuring an Anthropic-to-OpenAI API proxy.
- Description: Configure upstream endpoints, API keys, model routing and prompt terms. Save named settings, import or export configurations, and start or stop a local Anthropic-compatible proxy. The settings app follows the Windows light/dark appearance. Optional debug and verbose logs help inspect requests and responses. API keys are encrypted locally and omitted from exports unless explicitly included. The original proxy is by Ivan Zakutnii (m0n0x41d), author of anthropic-proxy-rs. The Windows UI, artwork and packaging are by Silviu Vlasceanu. This app is not affiliated with Anthropic or OpenAI.
- Privacy URL: https://github.com/silviuk/Anthropic-Proxy-RS-Win/blob/main/PRIVACY.md
- Support URL: https://github.com/silviuk/Anthropic-Proxy-RS-Win/issues
- Upstream requirements: an OpenAI-compatible API endpoint; a provider account/API key may be required and provider charges may apply.
- Reviewer notes: configure a test OpenAI-compatible endpoint; the app does not bundle a model or sell API access. Local HTTP upstreams are supported. Do not enter a production API key in certification screenshots.

Screenshots are produced by the native Windows desktop tests. Use the final version's `settings-light.png`, `settings-dark.png`, `logs-dark.png` and `export-warning.png`, without changing what the application actually displayed.

References: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements and https://learn.microsoft.com/en-us/windows/apps/publish/get-started

The selected Store route is MSIX. `packaging/msix/identity.json` records the actual reserved Partner Center identity for Store product `9PKT00FWGM35`. `packaging/msix/build.ps1` creates the unsigned upload, and `test.ps1` uses an ephemeral CI-only certificate to verify deployment and packaged runtime. That certificate and its private key are never part of the Store upload.
