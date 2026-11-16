use crate::cli::Cli;

pub fn default_pid_file() -> std::path::PathBuf {
    std::env::temp_dir().join("anthropic-proxy.pid")
}

#[cfg(unix)]
pub fn start(cli: &Cli) -> anyhow::Result<bool> {
    use std::fs::OpenOptions;
    let log_path = std::env::temp_dir().join("anthropic-proxy.log");
    let stdout = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&log_path)?;
    let stderr = stdout.try_clone()?;
    daemonize::Daemonize::new()
        .pid_file(&cli.pid_file)
        .working_directory(std::env::current_dir()?)
        .stdout(stdout)
        .stderr(stderr)
        .umask(0o027)
        .start()?;
    Ok(false)
}

#[cfg(unix)]
mod unix {

    pub fn stop_daemon(pid_file: &std::path::Path) -> anyhow::Result<()> {
        if !pid_file.exists() {
            eprintln!("✗ PID file not found: {}", pid_file.display());
            eprintln!("  Daemon is not running or PID file was removed");
            std::process::exit(1);
        }

        let pid_str = std::fs::read_to_string(pid_file)?;
        let pid: i32 = pid_str
            .trim()
            .parse()
            .map_err(|_| anyhow::anyhow!("Invalid PID in file: {}", pid_str))?;

        #[cfg(unix)]
        {
            use std::process::Command;
            let output = Command::new("kill").arg(pid.to_string()).output()?;

            if output.status.success() {
                std::fs::remove_file(pid_file)?;
                eprintln!("✓ Daemon stopped (PID: {})", pid);
            } else {
                eprintln!("✗ Failed to stop daemon (PID: {})", pid);
                eprintln!("  Process may have already exited");
                std::fs::remove_file(pid_file)?;
                std::process::exit(1);
            }
        }

        #[cfg(not(unix))]
        {
            eprintln!("✗ Daemon stop is only supported on Unix systems");
            std::process::exit(1);
        }

        Ok(())
    }

    pub fn check_status(pid_file: &std::path::Path) -> anyhow::Result<()> {
        if !pid_file.exists() {
            eprintln!("✗ Daemon is not running");
            eprintln!("  PID file not found: {}", pid_file.display());
            std::process::exit(1);
        }

        let pid_str = std::fs::read_to_string(pid_file)?;
        let pid: i32 = pid_str
            .trim()
            .parse()
            .map_err(|_| anyhow::anyhow!("Invalid PID in file: {}", pid_str))?;

        #[cfg(unix)]
        {
            use std::process::Command;
            let output = Command::new("ps").arg("-p").arg(pid.to_string()).output()?;

            if output.status.success() {
                eprintln!("✓ Daemon is running (PID: {})", pid);
                eprintln!("  PID file: {}", pid_file.display());
            } else {
                eprintln!("✗ Daemon is not running");
                eprintln!(
                    "  Stale PID file found: {} (PID: {})",
                    pid_file.display(),
                    pid
                );
                std::process::exit(1);
            }
        }

        #[cfg(not(unix))]
        {
            eprintln!("✗ Daemon status check is only supported on Unix systems");
            std::process::exit(1);
        }

        Ok(())
    }
}
#[cfg(unix)]
pub use unix::{check_status, stop_daemon};

#[cfg(windows)]
mod windows;
#[cfg(windows)]
pub use windows::{check_status, start, stop_daemon};
