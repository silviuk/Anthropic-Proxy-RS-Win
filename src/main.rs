mod cli;
mod config;
mod daemon;
mod error;
mod metrics;
mod models;
mod proxy;
mod translate;

use axum::{
    routing::{get, post},
    Extension, Router,
};
use clap::Parser;
use cli::{Cli, Command};
use config::Config;
use reqwest::Client;
use std::sync::Arc;
use tower_http::{
    cors::{Any, CorsLayer},
    trace::TraceLayer,
};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();

    if let Some(command) = cli.command {
        match command {
            Command::Stop { pid_file } => {
                daemon::stop_daemon(&pid_file)?;
                return Ok(());
            }
            Command::Status { pid_file } => {
                daemon::check_status(&pid_file)?;
                return Ok(());
            }
        }
    }

    if cli.daemon {
        if daemon::start(&cli)? {
            return Ok(());
        }
    } else {
        eprintln!("Starting proxy in foreground mode");
    }

    let runtime = tokio::runtime::Runtime::new()?;
    runtime.block_on(async_main(cli))
}

async fn async_main(cli: Cli) -> anyhow::Result<()> {
    let mut config = Config::from_env_with_path(cli.config)?;

    if cli.debug {
        config.debug = true;
    }
    if cli.verbose {
        config.verbose = true;
    }
    if let Some(port) = cli.port {
        config.port = port;
    }
    if let Some(bind) = cli.bind {
        let trimmed = bind.trim();
        if !trimmed.is_empty() {
            config.bind = trimmed.to_string();
        }
    }
    if !cli.system_prompt_ignore.is_empty() {
        config.system_prompt_ignore_terms.extend(
            cli.system_prompt_ignore
                .into_iter()
                .map(|term| term.trim().to_string())
                .filter(|term| !term.is_empty()),
        );
        Config::dedupe_ignore_terms(&mut config.system_prompt_ignore_terms);
    }

    let log_level = if config.verbose {
        tracing::Level::TRACE
    } else if config.debug {
        tracing::Level::DEBUG
    } else {
        tracing::Level::INFO
    };

    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| format!("anthropic_proxy={}", log_level).into()),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();

    tracing::info!("Starting Anthropic Proxy v{}", env!("CARGO_PKG_VERSION"));
    tracing::info!("Bind: {}", config.bind);
    tracing::info!("Port: {}", config.port);
    tracing::info!("Upstream URLs: {}", config.upstream_urls.join("; "));
    tracing::info!(
        "Resolved chat completions URLs: {}",
        config.chat_completions_urls().join("; ")
    );
    if let Some(ref model) = config.reasoning_model {
        tracing::info!("Reasoning Model Override: {}", model);
    }
    if let Some(ref model) = config.completion_model {
        tracing::info!("Completion Model Override: {}", model);
    }
    if config.passthrough_api_key {
        tracing::info!("API Key: passthrough mode (extracted from x-api-key header)");
    } else if config.api_key.is_some() {
        tracing::info!("API Key: configured");
    } else {
        tracing::info!("API Key: not set (using unauthenticated endpoint)");
    }
    if !config.system_prompt_ignore_terms.is_empty() {
        tracing::info!(
            "System prompt ignore terms: {}",
            config.system_prompt_ignore_terms.join("; ")
        );
    }
    if !config.model_map.is_empty() {
        let entries = config
            .model_map
            .iter()
            .map(|(source, target)| format!("{source} -> {target}"))
            .collect::<Vec<_>>()
            .join("; ");
        tracing::info!("Model map: {}", entries);
    }

    let metrics_handle = metrics::install();

    let client = Client::builder()
        .timeout(std::time::Duration::from_secs(300))
        .connect_timeout(std::time::Duration::from_secs(10))
        .pool_max_idle_per_host(10)
        .build()?;

    let config = Arc::new(config);

    let cors = CorsLayer::new()
        .allow_origin(Any)
        .allow_methods(Any)
        .allow_headers(Any);

    let app = Router::new()
        .route("/v1/messages", post(proxy::proxy_handler))
        .route("/v1/models", get(proxy::list_models_handler))
        .route("/health", axum::routing::get(health_handler))
        .route(
            "/metrics",
            get(move || {
                let handle = metrics_handle.clone();
                async move { handle.render() }
            }),
        )
        .layer(Extension(config.clone()))
        .layer(Extension(client))
        .layer(TraceLayer::new_for_http())
        .layer(cors);

    let addr = format!("{}:{}", config.bind, config.port);
    let listener = tokio::net::TcpListener::bind(&addr).await?;

    if config.bind == "0.0.0.0" {
        tracing::warn!(
            "Binding to 0.0.0.0 exposes the proxy on every network interface. \
             The proxy may hold an Anthropic API key, which makes this risky on shared networks. \
             Set --bind 127.0.0.1 (or ANTHROPIC_PROXY_BIND=127.0.0.1) to restrict to localhost."
        );
    }

    #[cfg(windows)]
    if cli.daemon_child {
        std::fs::write(&cli.pid_file, std::process::id().to_string())?;
    }

    tracing::info!("Listening on {}", addr);
    tracing::info!("Proxy ready to accept requests");

    axum::serve(listener, app).await?;

    Ok(())
}

async fn health_handler() -> &'static str {
    "OK"
}
