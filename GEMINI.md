# InfluxDB Studio (`InfluxDBStudio_by_Gamma`)

- **Sprache:** Antworten auf Deutsch; Code/Kommentare auf Englisch.
- **Stack:** C# / WinForms (`net10.0-windows7.0`), Projekt: `src/CymaticLabs.InfluxDB.Studio/CymaticLabs.InfluxDB.Studio.csproj`.
- **Multi-Version:** Unterstützt InfluxDB **1.x** (`InfluxDataNetClient`), **2.x** (`InfluxDbV2Client`) und **3.x** (`InfluxDbV3Client`) über `InfluxDbVersionDetector` + `InfluxDbClientFactory` (Details im Skill `influxdb-clients`).
- **Build & Test:**
  - Build: `dotnet build src\CymaticLabs.InfluxDB.Studio\CymaticLabs.InfluxDB.Studio.csproj --no-restore` (0 Warnungen / 0 Fehler).
  - `.exe`-Release: Siehe Skill `release-exe` (Output nach `publish\InfluxDBStudio.exe`).
- **Repo-Hygiene & Git:**
  - Für GitHub-Uploads/Operations bevorzugt `github-mcp-server` (z. B. `push_files`) verwenden; danach lokal mit `origin/master` abgleichen.
  - `publish/`, `scratch/`, `.vs/`, `.vscode/` niemals committen (`.gitignore`).
  - Keine Secrets/Tokens in Projektdateien oder `.git/config` ablegen.
  - Bei Änderungen an `InfluxDbConnection` immer `AppSettings.SerializeToJson` sowie `ConnectionDialog` mitanpassen.
