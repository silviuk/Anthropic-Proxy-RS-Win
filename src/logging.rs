use std::{
    fs::{File, OpenOptions},
    io::{self, Write},
    path::PathBuf,
};

const LIMIT: u64 = 10 * 1024 * 1024;

/// Keep the current log and one backup. Append allows the desktop's Clear action.
pub struct RotatingLog {
    path: PathBuf,
    file: Option<File>,
}

impl RotatingLog {
    pub fn open(path: PathBuf) -> io::Result<Self> {
        let file = OpenOptions::new().create(true).append(true).open(&path)?;
        Ok(Self {
            path,
            file: Some(file),
        })
    }
}

impl Write for RotatingLog {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        // Query the live length so truncating from the log viewer is respected.
        if self
            .file
            .as_ref()
            .is_some_and(|f| f.metadata().map(|m| m.len() >= LIMIT).unwrap_or(false))
        {
            self.file.take();
            let backup = self.path.with_extension("log.1");
            if backup.exists() {
                std::fs::remove_file(&backup)?;
            }
            std::fs::rename(&self.path, backup)?;
        }
        if self.file.is_none() {
            self.file = Some(
                OpenOptions::new()
                    .create(true)
                    .append(true)
                    .open(&self.path)?,
            );
        }
        self.file.as_mut().unwrap().write(bytes)
    }
    fn flush(&mut self) -> io::Result<()> {
        if let Some(file) = self.file.as_mut() {
            file.flush()?;
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rotates_and_respects_external_clear() {
        let path =
            std::env::temp_dir().join(format!("anthropic-log-test-{}.log", std::process::id()));
        let mut writer = RotatingLog::open(path.clone()).unwrap();
        writer.write_all(&vec![b'x'; LIMIT as usize]).unwrap();
        writer.write_all(b"new log").unwrap();
        assert_eq!(std::fs::read(&path).unwrap(), b"new log");
        OpenOptions::new()
            .write(true)
            .open(&path)
            .unwrap()
            .set_len(0)
            .unwrap();
        writer.write_all(b"after clear").unwrap();
        assert_eq!(std::fs::read(&path).unwrap(), b"after clear");
        drop(writer);
        std::fs::remove_file(&path).unwrap();
        std::fs::remove_file(path.with_extension("log.1")).unwrap();
    }
}
