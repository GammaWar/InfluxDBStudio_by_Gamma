**Sprache:** [English](README.md) | Deutsch

# InfluxDB Studio (`InfluxDBStudio_by_Gamma`)

**InfluxDB Studio ist eine Windows-Desktop-Oberfläche zur Verwaltung von [InfluxDB](https://www.influxdata.com/) 1.x, 2.x und 3.x.**

Das Projekt basiert auf [CymaticLabs/InfluxDBStudio](https://github.com/CymaticLabs/InfluxDBStudio), wurde auf **.NET 10 (`net10.0-windows7.0`)** modernisiert und unterstützt alle drei InfluxDB-Generationen inklusive automatischer Versionserkennung sowie manueller Auswahl vor dem Betreten einer Datenbank:

| InfluxDB-Version | Client-Bibliothek | Abfragesprache | Unterstützte Funktionen |
| :--- | :--- | :--- | :--- |
| **1.x** | `InfluxData.Net` (`8.0.1`) | InfluxQL | Datenbanken, Measurements, Tags/Fields, Series, Retention Policies, Continuous Queries, Backfill, laufende Queries, Diagnostics, Stats, Benutzer & Rechte, Schreiben von Punkten |
| **2.x** | `InfluxDB.Client` (`5.1.0`) | Flux | Buckets (als Datenbanken), Bucket-Retention (anzeigen/ändern), Measurements, Tags/Fields, Flux-Abfragen, Delete-API, OSS-Benutzer, Schreiben von Punkten |
| **3.x** *(Core / Enterprise / Serverless)* | `InfluxDB3.Client` (`1.10.0`) | SQL & InfluxQL *(automatisch erkannt)* | Datenbanken (`/api/v3/configure/database`, `SHOW DATABASES`, `/api/v2/buckets`), Measurements über `information_schema`, Tags/Fields, Arrow-Flight-Ergebnisse (`RecordBatch`), Schreiben von Punkten |

> **Autoerkennung:** Steht **API Version** auf `Auto (Detect)`, prüft InfluxDB Studio vor dem Öffnen einer Datenbank die Endpunkte `/ping`, `/health`, `/api/v3/query_sql` und `/api/v2/buckets`. Lässt sich die Version nicht eindeutig bestimmen, erscheint vor dem Verbindungsaufbau ein Auswahldialog für 1.x, 2.x oder 3.x.

---

## Inhaltsverzeichnis

- [Installation & Aus dem Quellcode bauen](#installation--aus-dem-quellcode-bauen)
- [Verbindungen verwalten](#verbindungen-verwalten)
  - [Verbindungseinstellungen](#verbindungseinstellungen)
- [Mit einem Server verbinden](#mit-einem-server-verbinden)
- [Arbeiten mit Verbindungen](#arbeiten-mit-verbindungen)
- [Arbeiten mit Datenbanken / Buckets](#arbeiten-mit-datenbanken--buckets)
- [Arbeiten mit Measurements und Series](#arbeiten-mit-measurements-und-series)
- [Benutzer und Berechtigungen (1.x / 2.x OSS)](#benutzer-und-berechtigungen-1x--2x-oss)
- [Programmeinstellungen](#programmeinstellungen)
- [Lizenz](#lizenz)

---

## Installation & Aus dem Quellcode bauen

Fertige Builds (sofern veröffentlicht) findest du unter [Releases](https://github.com/GammaWar/InfluxDBStudio_by_Gamma/releases).

### Voraussetzungen (Windows)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows Desktop / WinForms)

### Build-Befehle
Öffne `CymaticLabs.InfluxDB.sln` in Visual Studio / Rider oder baue direkt per PowerShell:

```powershell
# Wiederherstellen und bauen
dotnet build src\CymaticLabs.InfluxDB.Studio\CymaticLabs.InfluxDB.Studio.csproj

# Eigenständige Single-File-EXE nach publish\InfluxDBStudio.exe erstellen (win-x64)
./.agents/skills/release-exe/scripts/publish.ps1
```

Die erzeugte `publish\InfluxDBStudio.exe` enthält die benötigte .NET-Runtime bereits vollständig (self-contained).

---

## Verbindungen verwalten

Beim Start von **InfluxDBStudio.exe** öffnet sich der Dialog **Manage Connections** zum Anlegen, Bearbeiten und Löschen von Verbindungen.

![Manage Connections Dialog](docs/img/ManageConnectionsDialog_Blank.png?raw=true "Manage Connections Dialog")

Über **Create** legst du eine neue Verbindung an.

### Verbindungseinstellungen

* **Name** – Anzeigename der Verbindung.
* **Address & Port** – Hostname oder IP (Protokolle wie `http://` oder `https://` werden automatisch bereinigt) sowie Port (`1–65535`, Standard `8086`).
* **API Version** – `Auto (Detect)` oder explizite Festlegung auf `InfluxDB 1.x (InfluxQL)`, `InfluxDB 2.x (Flux / Buckets)` oder `InfluxDB 3.x (SQL / InfluxQL)`.
* **Organization** – Organisationsname oder -ID (erforderlich bei InfluxDB 2.x, optional bei 3.x; in 2.x bei nur einer Organisation auch automatisch aufgelöst).
* **Database** – Optionaler einzelner Datenbank-/Bucket-Name (hilfreich ohne Admin-Rechte zum Auflisten aller Datenbanken).
* **User Name & Password** – Zugangsdaten für InfluxDB 1.x (bzw. Basic Auth).
* **API Token** – Token-Authentifizierung für InfluxDB 2.x und 3.x.
* **Use SSL** – Aktiviert HTTPS. Scheitern **Test** oder **Ping** über HTTPS, prüft der Dialog automatisch, ob HTTP erreichbar ist, und bietet den Wechsel an.

![Connection Settings Dialog](docs/img/ConnectionsDialog_1.png?raw=true "Connection Settings Dialog")

* **Test** prüft Erkennung und Datenbank-/Measurement-Abruf.
* **Ping** misst die Antwortzeit und zeigt Server- und API-Version an.
* **Save** speichert die Verbindung (Passwörter und Tokens werden lokal per Windows DPAPI verschlüsselt).

---

## Mit einem Server verbinden

Wähle eine Verbindung unter **Manage Connections** aus und klicke auf **Connect**.

![Mit Server verbinden](docs/img/ManageConnectionsDialog_WithLocalhost.png?raw=true "Mit Server verbinden")

Aktive Verbindungen erscheinen links in der Baumansicht. Funktionen, die von der aktiven Server-Version nicht unterstützt werden (z. B. Continuous Queries unter 2.x/3.x), werden in Toolbar und Kontextmenü automatisch deaktiviert.

![Hauptfenster](docs/img/AppForm_InitialView.png?raw=true "Hauptfenster")

---

## Arbeiten mit Verbindungen

Über **Rechtsklick** auf einen Verbindungs-Knoten (oder über die Toolbar) stehen je nach Version zur Verfügung:

![Verbindungsbefehle](docs/img/Connections_ContextMenu.png?raw=true "Verbindungsbefehle")

* **Refresh** – Aktualisiert die Liste der Datenbanken / Buckets.
* **Create Database** – Erstellt eine neue Datenbank (1.x/3.x) bzw. einen Bucket (2.x).
* **Show Running Queries** *(1.x)* – Zeigt laufende Abfragen und erlaubt deren Beenden per PID.
* **Show Retention Policies** *(1.x / 2.x)* – Verwaltet Retention Policies (1.x) bzw. Bucket-Aufbewahrungsfristen (2.x).
* **Show Users** *(1.x / 2.x OSS)* – Zeigt und verwaltet Benutzer.
* **Show Statistics** *(1.x)* – Zeigt `SHOW STATS`.
* **Diagnostics** *(1.x)* – Zeigt `SHOW DIAGNOSTICS`.
* **Disconnect** – Trennt die Verbindung und schließt zugehörige Tabs sauber.

![Server Diagnostics](docs/img/Connections_Diagnostics_2.png?raw=true "Server Diagnostics")

---

## Arbeiten mit Datenbanken / Buckets

### Datenbank / Bucket anlegen & löschen

![Datenbank anlegen](docs/img/Connections_CreateDatabase_1.png?raw=true "Datenbank anlegen")
![Datenbank löschen](docs/img/Databases_Drop_2.png?raw=true "Datenbank löschen")

### Abfragen ausführen (**New Query**)

Je nach erkannter Version erstellt **New Query** automatisch eine passende Startabfrage:
- **1.x:** `SELECT * FROM "<measurement>" WHERE time > now() - 5m`
- **2.x:** `from(bucket: "<db>") |> range(start: -5m) |> filter(fn: (r) => r["_measurement"] == "<measurement>")`
- **3.x:** `SELECT * FROM "<measurement>" WHERE time >= now() - interval '5 minutes' ORDER BY time DESC LIMIT 100` *(SQL und `SHOW ...`-/InfluxQL-Befehle werden automatisch erkannt)*

Mit **CTRL+R** oder **Run Query** wird die Abfrage ausgeführt:

![Abfrage ausführen](docs/img/Databases_RunQuery_2.png?raw=true "Abfrage ausführen")

### Ergebnisse exportieren

Per **Rechtsklick** in der Ergebnistabelle lassen sich alle oder markierte Zeilen als **CSV** (sauber RFC-4180-escaped) oder **JSON** exportieren:

![Ergebnisse exportieren](docs/img/Databases_ExportQueryResults.png?raw=true "Ergebnisse exportieren")

### Continuous Queries & Backfill *(nur 1.x)*

![Continuous Queries](docs/img/Databases_CQ_2.png?raw=true "Continuous Queries")
![Backfill Dialog](docs/img/Databases_Backfill_1.png?raw=true "Backfill Dialog")

---

## Arbeiten mit Measurements und Series

* **New Query** – Startet eine Abfrage direkt auf das Measurement.
* **Show Tag Keys / Tag Values / Field Keys** – Funktioniert unter **1.x, 2.x und 3.x**.
* **Show Series / Drop Series** – Verfügbar unter **1.x**.
* **Drop Measurement** – Verfügbar unter **1.x** (`DROP MEASUREMENT`) und **2.x** (Delete-API).

![Tag Keys anzeigen](docs/img/Measurements_ShowTagKeys.png?raw=true "Tag Keys anzeigen")
![Tag Values anzeigen](docs/img/Measurements_ShowTagValues.png?raw=true "Tag Values anzeigen")
![Field Keys anzeigen](docs/img/Measurements_ShowFieldKeys.png?raw=true "Field Keys anzeigen")

---

## Benutzer und Berechtigungen *(1.x / 2.x OSS)*

![Benutzer anzeigen](docs/img/Connections_ShowUsers.png?raw=true "Benutzer anzeigen")
![Rechte vergeben](docs/img/Connections_GrantUserPrivilege_1.png?raw=true "Rechte vergeben")

---

## Programmeinstellungen

* **Settings → Time Format:** `12 hour` oder `24 hour`
* **Settings → Date Format:** `Month First` (`mm/dd/yyyy`) oder `Day First` (`dd/mm/yyyy`)
* **Allow Untrusted SSL:** Erlaubt selbstsignierte SSL-Zertifikate
* **File → Import / Export → Settings:** Einstellungen wahlweise **mit oder ohne Passwörter/Tokens** exportieren und wieder importieren

---

## Lizenz

Quellcode und Dokumentation stehen unter der **MIT-Lizenz** – siehe [LICENSE](LICENSE).
Basiert auf dem Originalprojekt [CymaticLabs/InfluxDBStudio](https://github.com/CymaticLabs/InfluxDBStudio).
