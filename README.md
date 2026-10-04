# InfluxDB Studio (Modernized Edition)

**InfluxDB Studio is a UI management tool for [the InfluxDB time series database](https://www.influxdata.com/time-series-platform/influxdb/).**

Its inspiration comes from database management tools such as [SQL Server Management Studio](https://en.wikipedia.org/wiki/SQL_Server_Management_Studio) and [Robomongo](https://robomongo.org/). Under the hood it is powered by an updated .NET client engine supporting InfluxQL and [Kapacitor](https://www.influxdata.com/time-series-platform/kapacitor/).

Originally created by [CymaticLabs](https://github.com/CymaticLabs/InfluxDBStudio), this modernized edition upgrades the project from legacy .NET Framework 4.6.1 to **.NET 10 Windows Forms**, fixes critical runtime and query bugs, adds DPAPI credential encryption, supports InfluxDB 2.x API tokens, improves High-DPI rendering, and provides a truly portable single-file executable format.

---

## What's New & Modernization Highlights

* **Upgraded to .NET 10 (`net10.0-windows7.0`):**
  Migrated to modern SDK-style project format. Runs faster, lighter, and independently of legacy .NET Framework runtimes.
* **InfluxDB 2.x API Token Authentication:**
  Added native support for `API Token` authentication (`Authorization: Token <token>`), allowing connections to InfluxDB 2.x instances via the v1 compatibility API.
* **Encrypted Credential Storage (DPAPI):**
  Passwords and API tokens are now encrypted using the Windows Data Protection API (DPAPI, `CurrentUser` scope). Legacy unencrypted passwords are automatically detected, upgraded, and encrypted on load.
* **Truly Portable Application (`settings.json`):**
  Connections and preferences are stored in `settings.json` directly next to the executable, enabling easy portability on USB drives or across machines. Legacy settings previously buried in Windows `%LOCALAPPDATA%` are automatically detected and migrated on first launch.
* **Fixed InfluxQL Retention Policy Bug:**
  Resolved a long-standing issue where parameters were inverted when setting default retention policies (`ALTER RETENTION POLICY "<policyName>" ON "<database>" DEFAULT`), which previously caused syntax errors.
* **Expanded Duration & Time-Interval Parser:**
  Enhanced the interval parser to accurately recognize `ms` (milliseconds), `µs`/`µ` (microseconds), and `ns` (nanoseconds) alongside `s`, `m`, `h`, `d`, and `w`.
* **Fixed Scintilla Editor Runtime Crash:**
  Replaced deprecated `jacobslusser.ScintillaNET` with `fernandreu.ScintillaNET 4.2.0`, resolving the `System.MissingMethodException: Scintilla.GetModulePath()` crash that occurred under modern .NET runtimes.
* **Smart HTTPS / HTTP Fallback Check:**
  The connection test dialog now automatically verifies connectivity and offers a smooth fallback check to HTTP if HTTPS fails or is not configured on the target host.
* **High-DPI Support:**
  Enabled `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` for crisp UI and font rendering on 4K and scaled monitors.
* **Updated & Audited Dependencies:**
  Upgraded packages to modern and secure releases (`Newtonsoft.Json 13.0.4`, `log4net 3.4.0`), removing legacy BCL shims and resolving known security vulnerabilities.

---

## Table of Contents

 - [Requirements & Building](#requirements--building)
   - [Building with .NET SDK](#building-with-net-sdk)
   - [Publishing a Portable Single-File Executable](#publishing-a-portable-single-file-executable)
 - [Managing Connections](#managing-connections)
   - [Connection Settings](#connection-settings)
   - [Connecting to a Server](#connecting-to-a-server)
   - [Working with Connections](#working-with-connections)
   - [Showing Server Diagnostics](#showing-server-diagnostics)
 - [Working with Databases](#working-with-databases)
   - [Creating a Database](#creating-a-database)
   - [Dropping a Database](#dropping-a-database)
   - [Running a Database Query](#running-a-database-query)
   - [Exporting Database Query Results](#exporting-database-query-results)
   - [Creating Continuous Queries](#creating-continuous-queries)
   - [Running a Backfill Query](#running-a-backfill-query)
 - [Working with Measurements and Series](#working-with-measurements-and-series)
   - [Running a Measurement Query](#running-a-measurement-query)
   - [Exporting Measurement Query Results](#exporting-measurement-query-results)
   - [Showing Tag Keys](#showing-tag-keys)
   - [Showing Tag Values](#showing-tag-values)
   - [Showing Field Keys](#showing-field-keys)
   - [Showing Series](#showing-series)
   - [Dropping Measurements](#dropping-measurements)
   - [Dropping Series](#dropping-series)
 - [Working with Users and Privileges](#working-with-users-and-privileges)
   - [Showing Users](#showing-users)
   - [Managing Users](#managing-users)
   - [Managing Privileges](#managing-privileges)
 - [Application Settings & Portability](#application-settings--portability)
   - [Portable Settings Storage](#portable-settings-storage)
   - [Settings Overview](#settings-overview)
   - [Exporting Settings](#exporting-settings)
   - [Importing Settings](#importing-settings)
 - [License](#license)

---

## Requirements & Building

### Requirements
* **Operating System:** Windows 10 / 11 / Windows Server (64-bit recommended)
* **SDK / Tooling (for building):** [.NET 10 SDK](https://dotnet.microsoft.com/download) or Visual Studio 2022+

### Building with .NET SDK
You can compile the project directly from PowerShell or Command Prompt:

```powershell
dotnet build src/CymaticLabs.InfluxDB.Studio/CymaticLabs.InfluxDB.Studio.csproj -c Release
```

### Publishing a Portable Single-File Executable
To produce a standalone, self-contained `InfluxDBStudio.exe` that runs on any 64-bit Windows machine without requiring any pre-installed .NET runtime:

```powershell
dotnet publish src/CymaticLabs.InfluxDB.Studio/CymaticLabs.InfluxDB.Studio.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

The resulting `InfluxDBStudio.exe` will be located in the `publish\` directory.

---

## Managing Connections

Upon running **InfluxDBStudio.exe** you will be prompted with the **Manage Connections** dialog. This window allows you to create, edit, and delete InfluxDB server connections.

![Empty Manage Connections Dialog](docs/img/ManageConnectionsDialog_Blank.png?raw=true "Empty Manage Connections Dialog")

Press the **Create** button to add an InfluxDB connection using the **Connection Settings** dialog.

### Connection Settings

Use the Connection Settings dialog to configure the connection parameters:

* **Name** - The friendly label for this connection.
* **Address** - The InfluxDB server's host or IP address (exclude `http://` or `https://`).
* **Port** - The port number (typically `8086`).
* **Database** - The database to select. Leave blank to list all databases *(requires administrative privileges)*.
* **Username** - The InfluxDB username (for InfluxDB 1.x authentication).
* **Password** - The InfluxDB password. Stored encrypted with Windows DPAPI.
* **API Token** - Modern API Token (for InfluxDB 2.x/3.x v1 compatibility). Stored encrypted with Windows DPAPI.
* **Use SSL** - Whether to connect via HTTPS (`true`) or plain HTTP (`false`).

![Create/Edit Connection Dialog](docs/img/ConnectionsDialog_1.png?raw=true "Create/Edit Connection Dialog")

* *The **Test** button verifies connectivity using the provided credentials. If HTTPS fails, the dialog automatically checks for HTTP availability and offers to switch if supported.*
* *The **Ping** button pings the InfluxDB server and displays response time and server version.*
* *Press **Save** to save the connection details to your portable configuration.*

---

## Connecting to a Server

Once a connection is configured, select it in the **Manage Connections** dialog and press **Connect**.

![Connect to a Server](docs/img/ManageConnectionsDialog_WithLocalhost.png?raw=true "Connect to a Server")

The **main application window** displays connected servers in the tree view on the left. You can reopen the connection manager anytime by clicking **Connections** → **Manage** in the application menu.

![Main Window](docs/img/AppForm_InitialView.png?raw=true "Main Window")

---

## Working with Connections

Right-click a **Connection** node in the tree view or use the corresponding toolbar buttons:

![Connection Commands](docs/img/Connections_ContextMenu.png?raw=true "Connection Commands")

Available connection commands:
* **Refresh** - Reloads database information from the server.
* **Create Database** - Creates a new database on the server.
* **Show Users** - Opens user management and database permissions.
* **Diagnostics** - Displays server runtime diagnostics, uptime, and version.
* **Disconnect** - Closes the connection.

### Showing Server Diagnostics

Select **Show Diagnostics** from the context menu or toolbar:

![Show Diagnostics](docs/img/Connections_Diagnostics_2.png?raw=true "Show Diagnostics")

---

## Working with Databases

Available commands for databases:
* **Refresh** - Refreshes measurements and series from the server.
* **New Query** - Opens a query tab to write and execute InfluxQL queries.
* **Show Continuous Queries** - List, create, and manage continuous queries.
* **Run Backfill** - Run backfill operations to downsample historical data.
* **Drop Database** - Permanently deletes the database.

### Creating a Database

Select a connection node, click **Create Database**, enter the database name, and confirm:

![Create Database](docs/img/Connections_CreateDatabase_1.png?raw=true "Create Database")

The newly created database will appear immediately in the tree view:

![Database Created](docs/img/Connections_CreateDatabase_2.png?raw=true "Database Created")

### Dropping a Database

Select the database in the tree view, right-click, and choose **Drop Database**:

![Confirm Drop Database](docs/img/Databases_Drop_2.png?raw=true "Confirm Drop Database")

> [!CAUTION]
> Dropping a database permanently removes all measurements and series data within it.

### Running a Database Query

Select a database and choose **New Query** (or press **Ctrl+N**). Write your InfluxQL query in the syntax-highlighted editor and press **F5**, **Ctrl+R**, or the **Run** toolbar button.

![Run Query](docs/img/Databases_RunQuery_2.png?raw=true "Run Query")

Queries using `GROUP BY` will organize series into individual tabs in the results grid:

![Group Results](docs/img/Databases_RunQuery_3.png?raw=true "Group Results")

### Exporting Database Query Results

Results can be exported to **CSV** or **JSON**:
* Right-click the result table and select **Export All**, or
* Select specific rows (**Ctrl+Click** / **Shift+Click**) and select **Export Selected**.

![Exporting Database Results](docs/img/Databases_ExportQueryResults.png?raw=true "Exporting Database Results")

### Creating Continuous Queries

[Continuous Queries (CQ)](https://docs.influxdata.com/influxdb/v1.8/query_language/continuous_queries/) run automatically at predefined intervals to downsample or aggregate streaming data.

Select **Show Continuous Queries** → click **Create CQ**:

![Create Continuous Query](docs/img/Databases_CQ_2.png?raw=true "Create Continuous Query")

Use the CQ designer dialog to set interval, source measurement, downsampling aggregations, and destination measurement:

![Create Continuous Queries Dialog](docs/img/Databases_CQ_3.png?raw=true "Create Continuous Queries Dialog")

Once created, verify that data is streaming into the target measurement:

![Continuous Query Created](docs/img/Databases_CQ_4.png?raw=true "Continuous Query Created")

### Running a Backfill Query

Backfill queries apply downsampling aggregations to historical data stored prior to the activation of a Continuous Query.

Right-click a database and choose **Run Backfill**:

![Run Backfill Query Dialog](docs/img/Databases_Backfill_1.png?raw=true "Run Backfill Query Dialog")

Configure the query parameters, time ranges, and target measurement, then press **Run**:

![Backfill Query Results](docs/img/Databases_Backfill_2.png?raw=true "Backfill Query Results")

---

## Working with Measurements and Series

Available commands on measurements:
* **New Query** - Opens a query tab targeting the measurement.
* **Show Tag Keys** - Lists all tag keys.
* **Show Tag Values** - Explores distinct values for tags.
* **Show Field Keys** - Lists field keys along with their data types.
* **Show Series** - Lists all series associated with the measurement.
* **Drop Measurement** - Permanently removes the measurement and its data.
* **Drop Series** - Removes all series data while preserving the measurement definition.

### Running a Measurement Query

Double-click or right-click a measurement and select **New Query**:

![Run Query](docs/img/Measurements_RunQuery_1.png?raw=true "Run Query")

### Exporting Measurement Query Results

Right-click within the query result grid to export data to CSV or JSON:

![Exporting Measurement Results](docs/img/Measurements_ExportQuery_1.png?raw=true "Exporting Measurement Results")

### Showing Tag Keys, Tag Values, Field Keys & Series

Inspect tag keys, explore values, review field schemas, and browse series definitions:

| Tag Keys | Tag Values |
|---|---|
| ![Showing Tag Keys](docs/img/Measurements_ShowTagKeys.png?raw=true) | ![Showing Tag Values](docs/img/Measurements_ShowTagValues.png?raw=true) |

| Field Keys | Series |
|---|---|
| ![Showing Field Keys](docs/img/Measurements_ShowFieldKeys.png?raw=true) | ![Showing Series](docs/img/Measurements_ShowSeries.png?raw=true) |

### Dropping Measurements & Series

| Drop Measurement | Drop Series |
|---|---|
| ![Confirm Drop Measurement](docs/img/Measurements_DropMeasurement.png?raw=true) | ![Confirm Drop Series](docs/img/Measurements_DropSeries.png?raw=true) |

---

## Working with Users and Privileges

Manage InfluxDB server users and access permissions:

![Showing Users](docs/img/Connections_ShowUsers.png?raw=true "Showing Users")

### Managing Users

* **Create User:** Right-click in the Users tab and select **Create User**. Set username, password, and admin status.
* **Edit User:** Update admin privileges.
* **Change Password:** Safely update an existing user's password.
* **Drop User:** Permanently remove a user from the server.

| Create User | Edit User |
|---|---|
| ![Create User Dialog](docs/img/Connections_CreateUser_1.png?raw=true) | ![Edit User](docs/img/Connections_EditUser.png?raw=true) |

### Managing Privileges

Assign granular database privileges (**Read**, **Write**, or **All**) to non-admin users:

![Granting a Privilege](docs/img/Connections_GrantUserPrivilege_1.png?raw=true "Granting a Privilege")

Confirm the granted privilege in the Privileges Panel:

![Privilege Granted](docs/img/Connections_GrantUserPrivilege_2.png?raw=true "Privilege Granted")

---

## Application Settings & Portability

### Portable Settings Storage
By default, InfluxDB Studio operates in **portable mode**:
* All configurations and connections are saved to **`settings.json`** directly inside the application's executable directory.
* If the application folder is write-protected (e.g. `C:\Program Files\`), it seamlessly falls back to `%LOCALAPPDATA%\CymaticLabs\InfluxDBStudio\settings.json`.
* Existing connection settings from older .NET Framework installations or prior export files are automatically detected and consolidated on initial startup.

### Settings Overview
Access display preferences via **Settings**:
* **Time Format:** 12-hour (`hh:mm:ss tt`) or 24-hour (`HH:mm:ss`).
* **Date Format:** Month First (`M/dd/yyyy`) or Day First (`d/MM/yyyy`).
* **Allow Untrusted SSL:** Allows self-signed certificates when communicating over HTTPS.

### Exporting & Importing Settings
To transfer connections between workstations or create backups:
* **Export:** Select **File → Export → Settings** to save your configurations to a JSON file.
* **Import:** Select **File → Import → Settings** to load connections from a previous export.

---

## License

Code and documentation are released under the [MIT License](LICENSE).
