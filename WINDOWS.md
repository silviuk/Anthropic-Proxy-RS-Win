# Anthropic-Proxy-RS-Win for Windows x64

This package includes a native Windows Forms settings app and the 64-bit Rust proxy. The settings app uses .NET Framework 4.8, included with current Windows 10/11 installations. The proxy links the Microsoft C runtime statically. Rust, Python, WSL and a separate Visual C++ runtime installation are not needed to run the package. Build and executable tests run on GitHub's Windows runner against a local test upstream. These tests do not use a paid provider or validate every upstream model.

## Use the Windows app

Extract the ZIP to a permanent folder and open `Anthropic-Proxy-RS-Win.exe`. Keep all packaged files together, including `Anthropic-Proxy-RS-Win.exe.config`. The app follows the Windows app light/dark setting, including changes while it is open, and respects Windows high contrast colors.

Enter the upstream URL, API key and any model overrides. Choose **Save and start**. Use **Stop proxy** to stop it, or **Save and apply** to save settings and restart a running proxy. Background mode leaves the proxy running when the window closes. With background mode disabled, closing its controlling window stops that proxy. The CLI remains available separately; the app manages its own per-user proxy instance.

The listen address defaults to `127.0.0.1` for local access. Use `0.0.0.0` only when you intend to accept connections from other machines. The proxy does not add client authorization checks.

Settings are saved under `%LOCALAPPDATA%\AnthropicProxy`. The API key is encrypted with Windows DPAPI for the current Windows account; it is decrypted only when needed and passed to the child proxy through its environment, never a command-line argument. The desktop uses its own settings rather than discovering `.env` files. The advanced fields accept model mappings as `source=target;other=target` and ignored prompt terms separated by semicolons.

### Save and import named settings

Use the **Saved settings** dropdown above the form:

- Type a new name and choose **Save** to create a profile. Saving an existing name updates that profile. Names are case insensitive.
- Select a saved name and choose **Load** to fill the form. The app asks before replacing unsaved form edits.
- Select a saved name and choose **Rename** to give it a new name. This preserves its settings and encrypted API key.
- Choose **Import...** to import a `.env` configuration or an existing desktop `settings.json`. The imported settings appear as a new named profile. Existing names are preserved; repeated imports receive a numeric suffix.

Loading, importing, renaming or saving a profile does not restart the proxy. **Save and apply** or **Save and start** saves the current profile and applies it. Autostart continues to use the last applied settings, even when another profile has been loaded for editing. The autostart checkbox is a shared Windows setting, independent of profiles.

Profiles are saved in `%LOCALAPPDATA%\AnthropicProxy\profiles.json`. On first use, the app preserves the existing desktop configuration as **Default**. The last loaded or saved profile is selected when the window opens. API keys remain encrypted for the current Windows account. An encrypted JSON key from another account or computer may not decrypt; use a `.env` file or re-enter that key instead.

Environment imports accept `NAME=value`, optional `export`, comments, single-quoted literals and double-quoted values with `\n`, `\r`, `\t`, `\\` and `\"` escapes. Each setting must occupy one line; shell commands and variable substitutions are never executed. Supported fields include the upstream URL and key, passthrough authentication, bind address, port, completion/reasoning models, model mapping, ignored terms and `DEBUG` for diagnostic logging and `VERBOSE` for body capture. Unknown fields are ignored. Review imported settings before applying them.

### Export

Choose **Export...** to save the current form as JSON. **Include API key** is unchecked each time the dialog opens. The default export omits both the readable key and its encrypted stored representation. Selecting the checkbox displays **don't share with others**. That explicit export includes a readable API key; protect the exported file as a secret. Exports can be imported as named profiles.

### Proxy options

All current upstream public command-line options are available through the Windows UI or the included command-line executable. The original `anthropic-proxy.exe` name and argument names are retained for compatibility.

| Upstream option or setting | Windows UI |
| --- | --- |
| `--config`, `-c` | Config file / Load file; Import also reads dotenv settings into a named profile. |
| `--debug`, `-d`, `DEBUG` | Debug checkbox. Diagnostic messages; it does not enable request/response bodies. |
| `--verbose`, `-v`, `VERBOSE` | Verbose checkbox. Full request/response bodies, including streaming, at trace level. Verbose takes precedence when both boxes are checked. |
| `--port`, `-p`, `PORT` | Port field. |
| `--bind`, `ANTHROPIC_PROXY_BIND` | Listen address, including IP addresses, bracketed IPv6 and hostnames. The Windows UI defaults to local access. |
| `--system-prompt-ignore`, ignore terms variable | Ignore prompt terms with an Entries editor. |
| `--daemon` | Keep running after closing the window. Disabling it starts a proxy controlled by this window's lifetime. |
| `--pid-file` | Custom PID file path; blank uses the per-user app data folder. |
| `stop`, `status` | Stop proxy button and live status indicator. |
| `--help`, `-h` | Show command-line help button. |
| `--version`, `-V` | Version and author in the bottom-right About area. |
| Upstream URLs | Entries editor preserves failover order; semicolon-separated input also works. |
| Upstream API key and passthrough | API key field and client API key checkbox. |
| Completion/reasoning model overrides | Model fields. |
| Model mapping | Entries editor for `source=target` mappings. |
| `--no-env-file`, `--log-file` (Windows fork additions) | Skip dotenv checkbox and custom log file path. |

The UI passes `--debug` and `--verbose` to the same proxy implementation used by the CLI. Explicit form values take precedence over dotenv values. **Load file...** first imports a dotenv file into the form and records its path; clearing **Skip .env discovery** allows the proxy's normal file discovery or explicit `--config` file. A blank optional model field may be filled by that file. Saved API keys continue to use the visible form value.

Running is green, stopped is red, and warnings are orange. The indicator also names the state so it does not depend only on color. Upstream warnings and app errors point to Logs for details. The bottom-right About area shows the version, Windows author, project GitHub link and original fork link.

### Installation

The release installer performs a per-user installation without administrator rights, adds Start menu entries and registers an uninstaller. Setup and uninstall select the system light/dark appearance when they start. The installer requires Windows 10 build 19041 or later, x64 compatibility and .NET Framework 4.8. The portable ZIP remains available.

Uninstall stops the managed proxy and disables its autostart entry. It preserves local settings, profiles and logs. GitHub installer artifacts are not claimed to be Store-certified. See [Store submission requirements](packaging/STORE.md) for the account, identity and signing requirements.

### Autostart

Check **Start proxy when I sign in to Windows**, then **Save and apply**. Uncheck and save to disable it. This controls the current user's `AnthropicProxy` entry in the Windows `Run` registry key; administrator rights are not required. It starts the proxy quietly after sign-in, without opening the settings window. It does not run before sign-in as a Windows service. Windows may delay startup, and Windows Startup settings can separately disable an entry.

Keep the extracted folder in place while autostart is enabled. After moving the app, open it at the new location and save with autostart enabled to update the registered path. Disable autostart before deleting the package.

### Request and response logs

Open the **Logs** tab for live activity. Check **Verbose (-v, --verbose): request/response bodies** and save to capture prompts, replies, tool data and streamed content. Capture is off by default. These logs may contain private message content; HTTP authorization headers and API keys are not recorded by the proxy's body logger.

Logs are stored in `%LOCALAPPDATA%\AnthropicProxy\requests.log`. The proxy rotates at approximately 10 MiB and retains one backup. The viewer shows the most recent 256 KB. **Clear logs** clears both the active file and backup. Turning body capture off restarts a running proxy without capture; earlier captured bodies remain until cleared or rotated. **Open log folder** opens the settings and logs folder.

If autostart fails, inspect `startup-error.txt` in the same folder and open the app to correct the settings.

## Start the proxy

Extract `anthropic-proxy-windows-x64.zip`. Copy `proxy.env.example` to `proxy.env`, then set `UPSTREAM_BASE_URL`, `UPSTREAM_API_KEY` and your provider's model names. Keep `ANTHROPIC_PROXY_BIND=127.0.0.1` for local access.

Open PowerShell in that folder:

```powershell
.\anthropic-proxy.exe --config .\proxy.env
```

The proxy listens at `http://127.0.0.1:3000`. Press Ctrl+C to stop it. In another PowerShell window, check it with:

```powershell
Invoke-RestMethod http://127.0.0.1:3000/health
```

For an Anthropic-compatible client, set its endpoint to the proxy:

```powershell
$env:ANTHROPIC_BASE_URL = 'http://127.0.0.1:3000'
```

Choose a model your upstream supports, either in the client or using `COMPLETION_MODEL`, `REASONING_MODEL` or `ANTHROPIC_PROXY_MODEL_MAP` in `proxy.env`. See the main README for the existing API features and limitations.

## Run in the background

```powershell
.\anthropic-proxy.exe --config .\proxy.env --daemon
.\anthropic-proxy.exe status
.\anthropic-proxy.exe stop
```

Background logs are written to `$env:TEMP\anthropic-proxy.log` and the default PID file is `$env:TEMP\anthropic-proxy.pid`. Start reports success after the child process loads its configuration and opens the listening port. Stop terminates the process; in-flight requests may be interrupted. This is a background user process, not a registered Windows service.

Use `--pid-file` with the same file on start, status and stop when running separate instances. A crash or forced stop can leave a stale PID file. Check the process before using a stale PID file because Windows can reuse process IDs.

Configuration search order is `--config`, `.env` in the working directory or a parent directory, `%USERPROFILE%\.anthropic-proxy.env`, and `%PROGRAMDATA%\anthropic-proxy\.env`. Environment variables take precedence over file values.

## Build from source

Install Rust for the `x86_64-pc-windows-msvc` target and Visual Studio Build Tools with the C++ workload. Then use:

```powershell
$env:RUSTFLAGS = '-C target-feature=+crt-static'
cargo test --locked
cargo build --release --locked --target x86_64-pc-windows-msvc
.\target\x86_64-pc-windows-msvc\release\anthropic-proxy.exe --help
```

The `Windows` GitHub Actions workflow also checks formatting, runs Clippy, exercises foreground and background operation, and packages the executable with configuration instructions and a SHA-256 checksum.


## Build the settings app

From the repository root in PowerShell, use `./windows/build.ps1`. It uses the Windows .NET Framework compiler and writes `dist/package/Anthropic-Proxy-RS-Win.exe` and its configuration file. Place the release proxy executable beside them.

The Windows workflow tests settings persistence, DPAPI encryption, input validation, autostart registration in an isolated test registry key, proxy control, log capture and clearing, and native light/dark rendering. Screenshots and test results are retained as the `windows-ui-test-evidence` artifact. Autostart is tested through its registration and launcher behavior; CI does not reboot a desktop or sign a user in.


## System tray and attribution

The SVG in `assets/icon.svg` is the source of the Windows, system tray and Store icons. Double-click the tray icon to reopen settings. Its menu can show settings or logs, start or stop the proxy, or exit and stop it. In background mode, closing the window hides it to the tray; foreground mode stops the proxy when its controlling window closes. Autostart opens the controller in the tray.

The original proxy is by **Ivan Zakutnii (m0n0x41d)**, author of [anthropic-proxy-rs](https://github.com/m0n0x41d/anthropic-proxy-rs). The Windows UI, SVG artwork and packaging are maintained by **Silviu Vlasceanu**. Both copyrights are retained in the MIT license.

The Store MSIX uses Windows packaged startup tasks instead of a desktop Run registry entry. Startup is disabled by default, and Windows user or organization restrictions are respected. The unsigned Store upload is kept separate from the temporary test-signed package used only inside the disposable build runner.
