# Windows x64

This package includes a native Windows Forms settings app and the 64-bit Rust proxy. The settings app uses .NET Framework 4.8, included with current Windows 10/11 installations. The proxy links the Microsoft C runtime statically. Rust, Python, WSL and a separate Visual C++ runtime installation are not needed to run the package. Build and executable tests run on GitHub's Windows runner against a local test upstream. These tests do not use a paid provider or validate every upstream model.

## Use the Windows app

Extract the ZIP to a permanent folder and open `anthropic-proxy-ui.exe`. Keep all packaged files together, including `anthropic-proxy-ui.exe.config`. The app follows the Windows app light/dark setting, including changes while it is open, and respects Windows high contrast colors.

Enter the upstream URL, API key and any model overrides. Choose **Save and start**. Use **Stop proxy** to stop it, or **Save and apply** to save settings and restart a running proxy. Closing the settings window leaves the proxy running. The CLI remains available separately; the app manages its own per-user proxy instance.

The listen address defaults to `127.0.0.1` for local access. Use `0.0.0.0` only when you intend to accept connections from other machines. The proxy does not add client authorization checks.

Settings are saved under `%LOCALAPPDATA%\AnthropicProxy`. The API key is encrypted with Windows DPAPI for the current Windows account; it is decrypted only when needed and passed to the child proxy through its environment, never a command-line argument. The desktop uses its own settings rather than discovering `.env` files. The advanced fields accept model mappings as `source=target;other=target` and ignored prompt terms separated by semicolons.

### Autostart

Check **Start proxy when I sign in to Windows**, then **Save and apply**. Uncheck and save to disable it. This controls the current user's `AnthropicProxy` entry in the Windows `Run` registry key; administrator rights are not required. It starts the proxy quietly after sign-in, without opening the settings window. It does not run before sign-in as a Windows service. Windows may delay startup, and Windows Startup settings can separately disable an entry.

Keep the extracted folder in place while autostart is enabled. After moving the app, open it at the new location and save with autostart enabled to update the registered path. Disable autostart before deleting the package.

### Request and response logs

Open the **Logs** tab for live activity. Check **Record request and response bodies** and save to capture prompts, replies, tool data and streamed content. Capture is off by default. These logs may contain private message content; HTTP authorization headers and API keys are not recorded by the proxy's body logger.

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

From the repository root in PowerShell, use `./windows/build.ps1`. It uses the Windows .NET Framework compiler and writes `dist/package/anthropic-proxy-ui.exe` and its configuration file. Place the release proxy executable beside them.

The Windows workflow tests settings persistence, DPAPI encryption, input validation, autostart registration in an isolated test registry key, proxy control, log capture and clearing, and native light/dark rendering. Screenshots and test results are retained as the `windows-ui-test-evidence` artifact. Autostart is tested through its registration and launcher behavior; CI does not reboot a desktop or sign a user in.
