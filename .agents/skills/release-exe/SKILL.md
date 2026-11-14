---
name: release-exe
description: >-
  Builds and publishes a self-contained win-x64 single-file InfluxDBStudio.exe into publish/.
  Use when the user asks to create a new .exe, release build, or verify the publish output.
---

# Publish Single-File Executable (`InfluxDBStudio.exe`)

## Steps

1. Run the helper script from the repository root:
   - `./.agents/skills/release-exe/scripts/publish.ps1`
   - *(Note: On offline/sandboxed machines, `--no-restore` can be appended only if `win-x64` runtime assets were already restored.)*
2. Verify the output in `publish/`:
   - `publish\InfluxDBStudio.exe` exists with current timestamp and zero build errors.
3. Remind the user that `publish/` is gitignored and intended for local use or GitHub Releases.
