---
name: influxdb-clients
description: >-
  Architecture reference for InfluxDB 1.x, 2.x, and 3.x client routing, auto-detection,
  capability flags, and query/result mapping in CymaticLabs.InfluxDB.Data.
  Use when modifying InfluxDB clients, version detection, connection settings, or UI capability gating.
---

# InfluxDB Multi-Version Clients (`1.x` / `2.x` / `3.x`)

Use this skill before editing files in `src/CymaticLabs.InfluxDB.Studio/Data/`, `Dialogs/ConnectionDialog*`, `AppSettings.cs`, or connection/UI logic in `AppForm.cs`.

## Key Rules

- Always route connection creation through `InfluxDbClientFactory.CreateAsync` on UI connect/test/ping paths so `InfluxDbApiVersion.Auto` probes the server before entering databases.
- Gate 1.x-only UI controls via capability flags on `InfluxDbClient` inside `AppForm.UpdateUIState()`.
- Mirror any new `InfluxDbConnection` properties in `AppSettings.SerializeToJson` and `ConnectionDialog`.

## Detailed Architecture Reference

- Read [architecture.md](./references/architecture.md) for endpoint probing order, Flux/Arrow mapping details, and NuGet package notes.
