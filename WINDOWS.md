# Windows x64

This fork builds a native 64-bit Windows executable with the Microsoft C runtime linked statically. Rust, Python, WSL and a separate Visual C++ runtime installation are not needed to run it. Build and smoke tests run on GitHub's Windows runner against a local test upstream. These tests do not use a paid provider or validate every upstream model.

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
