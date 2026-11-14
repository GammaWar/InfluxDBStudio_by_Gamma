# InfluxDB 1.x / 2.x / 3.x Architecture Details

## Connection & Auto-Detection

- `InfluxDbConnection` (`Data/InfluxDbConnection.cs`):
  - Persisted fields: `ApiVersion` (`Auto = 0`, `V1 = 1`, `V2 = 2`, `V3 = 3`), `Organization`, `SecurityToken`, `Database`, `Username`, `Password`, `UseSsl`.
  - Runtime-only (`[JsonIgnore]`): `DetectedApiVersion`, `DetectedServerVersion`, `EffectiveApiVersion`.
- `InfluxDbVersionDetector.DetectAsync` (`Data/InfluxDbVersionDetector.cs`):
  1. Probes `GET /ping` -> checks `X-Influxdb-Version`, `X-Influxdb-Build` (`Core`, `Enterprise`, `Serverless`, `Dedicated`, `IOx`), and JSON body.
  2. Probes `GET /health` -> checks headers and JSON `version`/`build`.
  3. Probes `GET /api/v3/query_sql` -> `405` / `400` or v3 headers identify `V3`.
  4. Probes `GET /api/v2/buckets` -> `200 OK` (without v3 markers) identifies `V2`; `404` alongside a clean `/ping` (`200`/`204`) identifies `V1`.
  5. Returns `Detected = false` if ambiguous (e.g., uniform `401` proxy), causing `InfluxDbClientFactory.CreateAsync` to throw `InfluxDbVersionAmbiguousException` so the UI prompts explicitly before entering any DB.

## Client Implementations

- **1.x (`InfluxDataNetClient` via `InfluxData.Net 8.0.1`):**
  - Full legacy feature set (`Supports* => true`).
  - Default query: `SELECT * FROM "<measurement>" WHERE time > now() - 5m`.
- **2.x (`InfluxDbV2Client` via `InfluxDB.Client 5.1.0`):**
  - Databases map to **Buckets** (`BucketsApi`). Auto-resolves single org if `Organization` is left blank.
  - Schema discovery uses `influxdata/influxdb/schema` Flux queries; `FluxTable` maps to `InfluxDbSeries` with `NodaTime.Instant` converted to UTC `DateTime`.
  - `DropMeasurementAsync` uses `DeleteApi`; `GetRetentionPoliciesAsync` / `AlterRetentionPolicyAsync` map bucket retention rules.
  - Default query: Flux `from(bucket: "...") |> range(start: -5m) |> filter(...)`.
- **3.x (`InfluxDbV3Client` via `InfluxDB3.Client 1.10.0`):**
  - `QueryAsync` uses `QueryBatches` (Apache Arrow `RecordBatch` -> `InfluxDbSeries`, including `DictionaryArray` tag columns and `TimestampArray`).
  - `DetectQueryType` automatically routes `SHOW MEASUREMENTS/TAG KEYS/TAG VALUES/FIELD KEYS` or `GROUP BY time(...)` / `now() - ...` to `QueryType.InfluxQL`, and everything else to `QueryType.SQL`.
  - Database discovery tries `GET /api/v3/configure/database` -> SQL `SHOW DATABASES` -> `GET /api/v2/buckets`.
  - Default query: SQL `SELECT * FROM "<measurement>" WHERE time >= now() - interval '5 minutes' ORDER BY time DESC LIMIT 100`.
