# Privacy

Anthropic-Proxy-RS-Win is a Windows application maintained by Silviu Vlasceanu and forked from m0n0x41d/anthropic-proxy-rs.

The application forwards requests from your Anthropic-compatible client to the upstream API endpoint you configure. Prompts, messages, images, tool information and responses pass through that endpoint. The endpoint provider's terms and privacy policy apply. The application does not provide an upstream account or an API key.

The Windows application stores settings and named profiles on your computer. Saved API keys use Windows DPAPI encryption for your Windows account. The proxy receives the decrypted key in its process environment when it starts.

Debug logging (`-d`) records diagnostic information. Verbose logging (`-v`) additionally records request and response bodies, including streamed content. Body capture is disabled by default. Logs are stored locally and may contain private content when verbose mode is enabled. Clear logs from the app to remove the current file and backup.

Export excludes both the API key and its encrypted stored representation by default. If you explicitly choose to include the key, the exported JSON contains a readable API key and the app displays “don't share with others.” Protect that file as a secret.

The Windows app does not add analytics, advertising or telemetry services. It contacts your configured upstream endpoint for proxy operations. Opening the GitHub links launches your browser and is subject to GitHub's privacy policy.

Uninstall removes the installed application and its autostart entry. Settings, profiles and logs are kept in `%LOCALAPPDATA%\AnthropicProxy` so an upgrade or reinstall preserves them. Delete that folder if you want to remove this local data completely. A custom log or PID path remains your responsibility.

Report privacy or security concerns through the repository's issue tracker without including keys, private logs or sensitive messages: https://github.com/silviuk/Anthropic-Proxy-RS-Win/issues
