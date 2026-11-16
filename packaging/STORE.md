# Microsoft Store submission

The selected Store route is MSIX. The app reservation is `9PKT00FWGM35` under publisher Silviu Vlasceanu. `msix/identity.json` records its public package identity. `msix/build.ps1` creates the unsigned Store upload; Microsoft signs packages distributed through the Store after certification.

`msix/test.ps1` signs a separate copy with an ephemeral certificate on the disposable Windows test machine. It verifies installation, package identity, native UI and startup API access, and removal. The certificate and its private key are never included in the Store upload. The packaged app uses a Windows startup task; the standalone app uses the current user's Run entry. Both start at sign-in.

The separate EXE installer supports per-user silent installation and removal, with the Windows light/dark appearance selected at the start. GitHub test installers are unsigned. Never replace a published installer after submitting its checksum to WinGet or the Store.

Prepared listing:

- Product name: Anthropic-Proxy-RS-Win
- Publisher: Silviu Vlasceanu
- Category: Developer tools / Networking
- Short description: A native Windows app for configuring an Anthropic-to-OpenAI API proxy.
- Description: Configure upstream endpoints, API keys, model routing and prompt terms. Save named settings, import or export configurations, and start or stop a local Anthropic-compatible proxy. The settings app follows the Windows light/dark appearance. Optional debug and verbose logs help inspect requests and responses. API keys are encrypted locally and omitted from exports unless explicitly included. The original proxy is by Ivan Zakutnii (m0n0x41d), author of anthropic-proxy-rs. The Windows UI, artwork and packaging are by Silviu Vlasceanu. This app is not affiliated with Anthropic or OpenAI.
- Privacy URL: https://github.com/silviuk/Anthropic-Proxy-RS-Win/blob/main/PRIVACY.md
- Support URL: https://github.com/silviuk/Anthropic-Proxy-RS-Win/issues
- Upstream requirements: an OpenAI-compatible API endpoint; a provider account/API key may be required and provider charges may apply.
- Reviewer notes: configure a test OpenAI-compatible endpoint; the app does not bundle a model or sell API access. Local HTTP upstreams are supported. Do not enter a production API key in certification screenshots.

Screenshots are produced by the native Windows desktop tests. Use `store-settings-light.png`, `store-settings-dark.png` and `store-logs-dark.png`, captured at 1366 by 768 pixels. Do not change what the application actually displayed. The package includes the SVG-derived icon used by the executable and tray.

Reference: https://learn.microsoft.com/en-us/windows/apps/publish/get-started
