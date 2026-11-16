use crate::cli::Cli;
use anyhow::{bail, Context, Result};
use std::{fs, os::windows::process::CommandExt, path::Path, process::Stdio, time::Duration};
use windows_sys::Win32::{
    Foundation::{CloseHandle, HANDLE, WAIT_OBJECT_0, WAIT_TIMEOUT},
    System::Threading::{
        OpenProcess, TerminateProcess, WaitForSingleObject, CREATE_NEW_PROCESS_GROUP,
        CREATE_NO_WINDOW, PROCESS_SYNCHRONIZE, PROCESS_TERMINATE,
    },
};

struct Process(HANDLE);
impl Drop for Process {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.0) };
    }
}

fn process(pid: u32, terminate: bool) -> Result<Option<Process>> {
    let access = PROCESS_SYNCHRONIZE | if terminate { PROCESS_TERMINATE } else { 0 };
    let handle = unsafe { OpenProcess(access, 0, pid) };
    if handle.is_null() {
        let error = std::io::Error::last_os_error();
        // ERROR_INVALID_PARAMETER means the process no longer exists.
        if error.raw_os_error() == Some(87) {
            return Ok(None);
        }
        return Err(error.into());
    }
    Ok(Some(Process(handle)))
}

fn running(process: &Process) -> Result<bool> {
    match unsafe { WaitForSingleObject(process.0, 0) } {
        WAIT_TIMEOUT => Ok(true),
        WAIT_OBJECT_0 => Ok(false),
        _ => Err(std::io::Error::last_os_error().into()),
    }
}

fn read_pid(path: &Path) -> Result<u32> {
    let pid = fs::read_to_string(path)
        .with_context(|| format!("Cannot read PID file: {}", path.display()))?
        .trim()
        .parse::<u32>()
        .context("Invalid PID file")?;
    anyhow::ensure!(pid != 0, "Invalid zero PID");
    Ok(pid)
}

pub fn start(cli: &Cli) -> Result<bool> {
    if cli.pid_file.exists() {
        let pid = read_pid(&cli.pid_file)?;
        if let Some(handle) = process(pid, false)? {
            anyhow::ensure!(!running(&handle)?, "Daemon is already running (PID: {pid})");
        }
        fs::remove_file(&cli.pid_file)?;
    }
    let log_path = std::env::temp_dir().join("anthropic-proxy.log");
    let log = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(&log_path)?;
    let args = std::env::args_os().skip(1).filter(|arg| arg != "--daemon");
    let mut child = std::process::Command::new(std::env::current_exe()?)
        .args(args)
        .arg("--daemon-child")
        .stdin(Stdio::null())
        .stdout(log.try_clone()?)
        .stderr(log)
        .creation_flags(CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP)
        .spawn()?;
    // The child writes its PID only after configuration and listener setup succeed.
    for _ in 0..200 {
        if let Some(status) = child.try_wait()? {
            bail!("Daemon exited with {status}; see {}", log_path.display());
        }
        if read_pid(&cli.pid_file).ok() == Some(child.id()) {
            eprintln!(
                "Daemon started (PID: {}); log: {}",
                child.id(),
                log_path.display()
            );
            return Ok(true);
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    child.kill()?;
    child.wait()?;
    let _ = fs::remove_file(&cli.pid_file);
    bail!("Daemon did not become ready; see {}", log_path.display())
}

pub fn check_status(path: &Path) -> Result<()> {
    let pid = read_pid(path)?;
    if let Some(handle) = process(pid, false)? {
        if running(&handle)? {
            eprintln!("Daemon is running (PID: {pid})");
            return Ok(());
        }
    }
    bail!("Daemon is not running; stale PID file: {}", path.display())
}

pub fn stop_daemon(path: &Path) -> Result<()> {
    let pid = read_pid(path)?;
    if let Some(handle) = process(pid, true)? {
        if running(&handle)? {
            if unsafe { TerminateProcess(handle.0, 0) } == 0 {
                return Err(std::io::Error::last_os_error().into());
            }
            anyhow::ensure!(
                unsafe { WaitForSingleObject(handle.0, 5000) } == WAIT_OBJECT_0,
                "Timed out waiting for daemon to stop"
            );
        }
    }
    fs::remove_file(path)?;
    eprintln!("Daemon stopped (PID: {pid})");
    Ok(())
}
