use clap::{Parser, Subcommand};
use std::path::PathBuf;

#[derive(Parser, Debug)]
#[command(
    name = "anthropic-proxy",
    version,
    about = "Proxy Anthropic API requests to OpenAI-compatible endpoints",
    long_about = "A high-performance proxy that translates Anthropic Claude API requests \
                  to OpenAI-compatible endpoints like OpenRouter, allowing you to use \
                  Claude-compatible clients with any OpenAI-compatible API."
)]
pub struct Cli {
    #[command(subcommand)]
    pub command: Option<Command>,

    /// Path to custom .env configuration file
    #[arg(short, long, value_name = "FILE")]
    pub config: Option<PathBuf>,

    /// Use only environment variables and skip .env file discovery
    #[arg(long)]
    pub no_env_file: bool,

    /// Write plain text logs to this file (rotates at 10 MiB, one backup)
    #[arg(long, value_name = "FILE")]
    pub log_file: Option<PathBuf>,

    /// Enable debug logging (same as DEBUG=true)
    #[arg(short, long)]
    pub debug: bool,

    /// Enable verbose logging (logs full request/response bodies)
    #[arg(short, long)]
    pub verbose: bool,

    /// Port to listen on (overrides PORT env var)
    #[arg(short, long, value_name = "PORT")]
    pub port: Option<u16>,

    /// Address to bind the listener to (overrides ANTHROPIC_PROXY_BIND env var, default 0.0.0.0)
    #[arg(long, value_name = "ADDR")]
    pub bind: Option<String>,

    /// System prompt terms to remove before forwarding upstream (semicolon-separated or repeated)
    #[arg(long, value_name = "TEXT", value_delimiter = ';')]
    pub system_prompt_ignore: Vec<String>,

    /// Run as background daemon
    #[arg(long)]
    pub daemon: bool,

    /// Internal flag for the detached Windows process
    #[cfg(windows)]
    #[arg(long, hide = true)]
    pub daemon_child: bool,

    /// PID file path (used with daemon commands)
    #[arg(long, value_name = "FILE", default_value_os_t = crate::daemon::default_pid_file())]
    pub pid_file: PathBuf,
}

#[derive(Subcommand, Debug)]
pub enum Command {
    /// Stop running daemon
    Stop {
        /// PID file path
        #[arg(long, value_name = "FILE", default_value_os_t = crate::daemon::default_pid_file())]
        pid_file: PathBuf,
    },
    /// Check daemon status
    Status {
        /// PID file path
        #[arg(long, value_name = "FILE", default_value_os_t = crate::daemon::default_pid_file())]
        pid_file: PathBuf,
    },
}
