**Language:** English | [Deutsch](README.de.md)

# InfluxDB Studio (`InfluxDBStudio_by_Gamma`)

**InfluxDB Studio is a Windows desktop UI management tool for [InfluxDB](https://www.influxdata.com/) 1.x, 2.x, and 3.x.**

Originally forked from [CymaticLabs/InfluxDBStudio](https://github.com/CymaticLabs/InfluxDBStudio) and modernized to **.NET 10 (`net10.0-windows7.0`)**, this version supports all three InfluxDB server generations with automatic server version detection and explicit fallback selection:

| InfluxDB Version | Client Library | Query Language | Supported Features |
| :--- | :--- | :--- | :--- |
| **1.x** | `InfluxData.Net` (`8.0.1`) | InfluxQL | Databases, Measurements, Tags/Fields, Series, Retention Policies, Continuous Queries, Backfill, Running Queries, Diagnostics, Stats, Users & Grants, Point Writes |
| **2.x** | `InfluxDB.Client` (`5.1.0`) | Flux | Buckets (as databases), Bucket Retention (view/alter), Measurements, Tags/Fields, Flux Queries, Delete API, OSS Users, Point Writes |
| **3.x** *(Core / Enterprise / Serverless)* | `InfluxDB3.Client` (`1.10.0`) | SQL & InfluxQL *(auto-detected)* | Databases (`/api/v3/configure/database`, `SHOW DATABASES`, `/api/v2/buckets`), `information_schema` Measurements, Tags/Fields, Arrow Flight (`RecordBatch`) Query Results, Point Writes |

> **Auto-Detection:** When **API Version** is set to `Auto (Detect)`, InfluxDB Studio probes `/ping`, `/health`, `/api/v3/query_sql`, and `/api/v2/buckets` before opening a database. If the server version cannot be determined unambiguously, a selection prompt lets you pick 1.x, 2.x, or 3.x before connecting.

---

## Table of Contents

- [Installation & Building from Source](#installation--building-from-source)
- [Managing Connections](#managing-connections)
  - [Connection Settings](#connection-settings)
- [Connecting to a Server](#connecting-to-a-server)
- [Working with Connections](#working-with-connections)
  - [Showing Server Diagnostics (1.x)](#showing-server-diagnostics-1x)
- [Working with Databases / Buckets](#working-with-databases--buckets)
  - [Creating a Database / Bucket](#creating-a-database--bucket)
  - [Dropping a Database / Bucket](#dropping-a-database--bucket)
  - [Running a Query](#running-a-query)
  - [Exporting Query Results](#exporting-query-results)
  - [Creating Continuous Queries (1.x)](#creating-continuous-queries-1x)
  - [Running a Backfill Query (1.x)](#running-a-backfill-query-1x)
- [Working with Measurements and Series](#working-with-measurements-and-series)
  - [Running a Measurement Query](#running-a-measurement-query)
  - [Showing Tag Keys, Tag Values & Field Keys](#showing-tag-keys-tag-values--field-keys)
  - [Showing Series (1.x)](#showing-series-1x)
  - [Dropping Measurements & Series](#dropping-measurements--series)
- [Working with Users and Privileges (1.x / 2.x OSS)](#working-with-users-and-privileges-1x--2x-oss)
- [Application Settings](#application-settings)
- [License](#license)

---

## Installation & Building from Source

Prebuilt binaries (when published) can be found under [Releases](https://github.com/GammaWar/InfluxDBStudio_by_Gamma/releases).

### Build Requirements (Windows)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows Desktop / WinForms workload)

### Build Commands
Open `CymaticLabs.InfluxDB.sln` in Visual Studio / Rider, or build from PowerShell:

```powershell
# Restore and build
dotnet build src\CymaticLabs.InfluxDB.Studio\CymaticLabs.InfluxDB.Studio.csproj

# Create self-contained single-file publish\InfluxDBStudio.exe (win-x64)
./.agents/skills/release-exe/scripts/publish.ps1
```

The published `publish\InfluxDBStudio.exe` is self-contained and runs without a separate .NET runtime installation.

---

## Managing Connections

Upon running **InfluxDBStudio.exe** you will be prompted with the **Manage Connections** dialog to create, edit, or delete InfluxDB server connections.

![Empty Manage Connections Dialog](docs/img/ManageConnectionsDialog_Blank.png?raw=true "Empty Manage Connections Dialog")

Press **Create** to configure a new connection.

### Connection Settings

* **Name** – Display label for the connection.
* **Address & Port** – Server hostname or IP (protocol prefixes such as `http://` or `https://` are normalized automatically) and port (`1–65535`, default `8086`).
* **API Version** – Choose `Auto (Detect)` or explicitly pin `InfluxDB 1.x (InfluxQL)`, `InfluxDB 2.x (Flux / Buckets)`, or `InfluxDB 3.x (SQL / InfluxQL)`.
* **Organization** – Organization name or ID (used for InfluxDB 2.x and optionally 3.x; automatically resolved in 2.x when your token belongs to a single organization).
* **Database** – Optional single database/bucket name. Useful when your token or user lacks permission to list all databases.
* **User Name & Password** – Credentials for InfluxDB 1.x (or 2.x Basic Auth).
* **API Token** – Bearer/Token authentication for InfluxDB 2.x and 3.x.
* **Use SSL** – Enable HTTPS. When **Test** or **Ping** fails over HTTPS, the dialog checks HTTP fallback availability and offers to switch automatically.

![Create/Edit Connection Dialog](docs/img/ConnectionsDialog_1.png?raw=true "Create/Edit Connection Dialog")

* **Test** verifies version detection and database/measurement listing.
* **Ping** measures response latency and prints the detected server & API version.
* **Save** stores the connection (credentials are DPAPI-encrypted in local user settings).

---

## Connecting to a Server

Select a saved connection in **Manage Connections** and press **Connect**.

![Connect to a Server](docs/img/ManageConnectionsDialog_WithLocalhost.png?raw=true "Connect to a Server")

Active connections appear in the tree view on the left. Unsupported toolbar and context-menu actions for the active server version (such as 1.x Continuous Queries on a 2.x/3.x server) are automatically disabled.

![Main Window](docs/img/AppForm_InitialView.png?raw=true "Main Window")

---

## Working with Connections

**Right-click** a **Connection** node (or use the toolbar) to access connection-level actions:

![Connection Commands](docs/img/Connections_ContextMenu.png?raw=true "Connection Commands")

* **Refresh** – Refreshes the database / bucket list.
* **Create Database** – Creates a new database (1.x/3.x) or bucket (2.x).
* **Show Running Queries** *(1.x)* – Lists active queries and allows killing a query by PID.
* **Show Retention Policies** *(1.x / 2.x)* – Views/edits retention policies (1.x) or bucket retention duration (2.x).
* **Show Users** *(1.x / 2.x OSS)* – Lists and manages server users.
* **Show Statistics** *(1.x)* – Displays `SHOW STATS` output.
* **Diagnostics** *(1.x)* – Displays `SHOW DIAGNOSTICS` server details.
* **Disconnect** – Closes the connection and all associated tabs cleanly.

### Showing Server Diagnostics (1.x)

![Show Diagnostics](docs/img/Connections_Diagnostics_2.png?raw=true "Show Diagnostics")

---

## Working with Databases / Buckets

### Creating a Database / Bucket

Select a Connection node and choose **Create Database** from the context menu or toolbar:

![Create Database](docs/img/Connections_CreateDatabase_1.png?raw=true "Create Database")
![Database Created](docs/img/Connections_CreateDatabase_2.png?raw=true "Database Created")

### Dropping a Database / Bucket

Select a Database node and choose **Drop Database** (**permanent operation**):

![Confirm Drop Database](docs/img/Databases_Drop_2.png?raw=true "Confirm Drop Database")

### Running a Query

Select a Database or Measurement node and click **New Query** (or double-click a measurement). The editor pre-populates an idiomatic template for the active version:
- **1.x:** `SELECT * FROM "<measurement>" WHERE time > now() - 5m`
- **2.x:** `from(bucket: "<db>") |> range(start: -5m) |> filter(fn: (r) => r["_measurement"] == "<measurement>")`
- **3.x:** `SELECT * FROM "<measurement>" WHERE time >= now() - interval '5 minutes' ORDER BY time DESC LIMIT 100` *(both SQL and `SHOW ...` / InfluxQL queries are auto-routed)*

Press **CTRL+R** or click **Run Query**:

![Run Query](docs/img/Databases_RunQuery_2.png?raw=true "Run Query")
![Group Results](docs/img/Databases_RunQuery_3.png?raw=true "Group Results")

### Exporting Query Results

Right-click the results grid to export **All** or **Selected** rows as **CSV** (RFC 4180 escaped) or **JSON**:

![Exporting Database Results](docs/img/Databases_ExportQueryResults.png?raw=true "Exporting Database Results")

### Creating Continuous Queries (1.x)

Right-click a 1.x database and select **Show Continuous Queries**, then click **Create CQ**:

![Create Continuous Query](docs/img/Databases_CQ_2.png?raw=true "Create Continuous Query")
![Create Continuous Queries Dialog](docs/img/Databases_CQ_3.png?raw=true "Create Continuous Queries Dialog")
![Continuous Query Created](docs/img/Databases_CQ_4.png?raw=true "Continuous Query Created")

### Running a Backfill Query (1.x)

Right-click a 1.x database and select **Run Backfill**:

![Run Backfill Query Dialog](docs/img/Databases_Backfill_1.png?raw=true "Run Backfill Query Dialog")
![Backfill Query Results](docs/img/Databases_Backfill_2.png?raw=true "Backfill Query Results")

---

## Working with Measurements and Series

### Running a Measurement Query

![Run Query](docs/img/Measurements_RunQuery_1.png?raw=true "Run Query")
![Exporting Measurement Results](docs/img/Measurements_ExportQuery_1.png?raw=true "Exporting Measurement Results")

### Showing Tag Keys, Tag Values & Field Keys

Available across **1.x, 2.x, and 3.x**:

![Showing Tag Keys](docs/img/Measurements_ShowTagKeys.png?raw=true "Showing Tag Keys")
![Showing Tag Values](docs/img/Measurements_ShowTagValues.png?raw=true "Showing Tag Values")
![Showing Field Keys](docs/img/Measurements_ShowFieldKeys.png?raw=true "Showing Field Keys")

### Showing Series (1.x)

![Showing Series](docs/img/Measurements_ShowSeries.png?raw=true "Showing Series")

### Dropping Measurements & Series

* **Drop Measurement** is supported on **1.x** (`DROP MEASUREMENT`) and **2.x** (via Delete API).
* **Drop Series** is supported on **1.x**.

![Confirm Drop Measurement](docs/img/Measurements_DropMeasurement.png?raw=true "Confirm Drop Measurement")
![Confirm Drop Series](docs/img/Measurements_DropSeries.png?raw=true "Confirm Drop Series")

---

## Working with Users and Privileges (1.x / 2.x OSS)

Manage users and 1.x database privileges via **Show Users**:

![Showing Users](docs/img/Connections_ShowUsers.png?raw=true "Showing Users")
![Create User Dialog](docs/img/Connections_CreateUser_1.png?raw=true "Create User Dialog")
![Edit User](docs/img/Connections_EditUser.png?raw=true "Edit User")
![Change User Password](docs/img/Connections_ChangeUserPassword.png?raw=true "Change User Password")
![Granting a Privilege](docs/img/Connections_GrantUserPrivilege_1.png?raw=true "Granting a Privilege")

---

## Application Settings

* **Settings → Time Format:** `12 hour` or `24 hour`.
* **Settings → Date Format:** `Month First` (`mm/dd/yyyy`) or `Day First` (`dd/mm/yyyy`).
* **Allow Untrusted SSL:** Allows connecting to InfluxDB instances using self-signed certificates.
* **File → Import / Export → Settings:** Export settings with or without passwords/API tokens, and safely restore them later.

---

## License

Code and documentation are available under the **MIT License** — see [LICENSE](LICENSE).
Based on the original [CymaticLabs/InfluxDBStudio](https://github.com/CymaticLabs/InfluxDBStudio).
