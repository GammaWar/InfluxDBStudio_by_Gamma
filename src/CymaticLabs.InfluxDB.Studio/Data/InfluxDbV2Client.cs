using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Flux.Domain;
using InfluxDB.Client.Writes;
using NodaTime;

namespace CymaticLabs.InfluxDB.Data
{
    /// <summary>
    /// InfluxDB 2.x client implementation backed by the official InfluxDB.Client library.
    /// </summary>
    public class InfluxDbV2Client : InfluxDbClient, IDisposable
    {
        private readonly InfluxDBClient client;
        private readonly HttpClient ownedHttpClient;
        private string cachedOrgNameOrId;
        private bool disposed;

        public override InfluxDbApiVersion ApiVersion => InfluxDbApiVersion.V2;
        public override bool SupportsCreateRetentionPolicy => false;
        public override bool SupportsContinuousQueries => false;
        public override bool SupportsBackfill => false;
        public override bool SupportsRunningQueries => false;
        public override bool SupportsDiagnostics => false;
        public override bool SupportsStats => false;
        public override bool SupportsSeriesDiscovery => false;
        public override bool SupportsDropSeries => false;

        public InfluxDbV2Client(InfluxDbConnection connection, HttpClient httpClientOverride = null)
            : base(connection)
        {
            if (httpClientOverride == null)
            {
                ownedHttpClient = new HttpClient(SslIgnoreValidator.CreateHttpClientHandler(), disposeHandler: true);
                ownedHttpClient.Timeout = TimeSpan.FromSeconds(100);
            }

            var builder = new InfluxDBClientOptions.Builder()
                .Url(connection.HttpConnectionString)
                .TimeOut(TimeSpan.FromSeconds(100))
                .VerifySsl(!SslIgnoreValidator.AllowUntrusted)
                .SetHttpClient(httpClientOverride ?? ownedHttpClient);

            if (!string.IsNullOrWhiteSpace(connection.SecurityToken))
            {
                builder.AuthenticateToken(connection.SecurityToken.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(connection.Username))
            {
                builder.Authenticate(connection.Username, (connection.Password ?? string.Empty).ToCharArray());
            }

            if (!string.IsNullOrWhiteSpace(connection.Organization))
            {
                cachedOrgNameOrId = connection.Organization.Trim();
                builder.Org(cachedOrgNameOrId);
            }

            if (!string.IsNullOrWhiteSpace(connection.Database))
            {
                builder.Bucket(connection.Database.Trim());
            }

            client = new InfluxDBClient(builder.Build());
        }

        public override string GetDefaultQuery(string database, string measurement)
        {
            var b = EscapeFluxString(string.IsNullOrWhiteSpace(database) ? "bucket" : database);
            var m = EscapeFluxString(string.IsNullOrWhiteSpace(measurement) ? "measurement" : measurement);
            return $"from(bucket: \"{b}\")\r\n  |> range(start: -5m)\r\n  |> filter(fn: (r) => r[\"_measurement\"] == \"{m}\")";
        }

        private async Task<string> ResolveOrgAsync()
        {
            if (!string.IsNullOrWhiteSpace(cachedOrgNameOrId))
                return cachedOrgNameOrId;

            if (!string.IsNullOrWhiteSpace(Connection.Organization))
            {
                cachedOrgNameOrId = Connection.Organization.Trim();
                return cachedOrgNameOrId;
            }

            var orgs = await client.GetOrganizationsApi().FindOrganizationsAsync(limit: 100).ConfigureAwait(false);
            if (orgs != null && orgs.Count == 1)
            {
                cachedOrgNameOrId = !string.IsNullOrWhiteSpace(orgs[0].Name) ? orgs[0].Name : orgs[0].Id;
                return cachedOrgNameOrId;
            }

            if (orgs != null && orgs.Count > 1)
            {
                throw new InvalidOperationException(
                    "Multiple InfluxDB 2.x organizations found. Please specify an Organization in Connection Settings.");
            }

            throw new InvalidOperationException(
                "InfluxDB 2.x requires an Organization. Please specify an Organization in Connection Settings.");
        }

        private async Task<Organization> ResolveOrganizationObjectAsync()
        {
            var orgApi = client.GetOrganizationsApi();
            if (!string.IsNullOrWhiteSpace(Connection.Organization))
            {
                var target = Connection.Organization.Trim();
                var byName = await orgApi.FindOrganizationsAsync(org: target).ConfigureAwait(false);
                if (byName != null && byName.Count > 0) return byName[0];

                var byId = await orgApi.FindOrganizationByIdAsync(target).ConfigureAwait(false);
                if (byId != null) return byId;
            }

            var orgs = await orgApi.FindOrganizationsAsync(limit: 100).ConfigureAwait(false);
            if (orgs != null && orgs.Count == 1) return orgs[0];

            throw new InvalidOperationException(
                "Could not resolve InfluxDB 2.x Organization. Please configure Organization in Connection Settings.");
        }

        #region Databases (Buckets)

        public override async Task<IEnumerable<string>> GetDatabaseNamesAsync()
        {
            var bucketsApi = client.GetBucketsApi();
            List<Bucket> buckets;

            if (!string.IsNullOrWhiteSpace(Connection.Organization))
            {
                buckets = await bucketsApi.FindBucketsAsync(org: Connection.Organization.Trim(), limit: 100).ConfigureAwait(false);
            }
            else
            {
                buckets = await bucketsApi.FindBucketsAsync(limit: 100).ConfigureAwait(false);
            }

            return (buckets ?? new List<Bucket>())
                .Where(b => !string.IsNullOrWhiteSpace(b.Name))
                .Select(b => b.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public override async Task<InfluxDbApiResponse> CreateDatabaseAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            var org = await ResolveOrganizationObjectAsync().ConfigureAwait(false);
            var created = await client.GetBucketsApi().CreateBucketAsync(database.Trim(), org).ConfigureAwait(false);
            return new InfluxDbApiResponse(created?.Id ?? database, HttpStatusCode.Created, created != null);
        }

        public override async Task<InfluxDbApiResponse> DropDatabaseAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            var bucketsApi = client.GetBucketsApi();
            var bucket = await bucketsApi.FindBucketByNameAsync(database.Trim()).ConfigureAwait(false);
            if (bucket == null)
                return new InfluxDbApiResponse($"Bucket '{database}' not found.", HttpStatusCode.NotFound, false);

            await bucketsApi.DeleteBucketAsync(bucket).ConfigureAwait(false);
            return new InfluxDbApiResponse(string.Empty, HttpStatusCode.NoContent, true);
        }

        #endregion Databases (Buckets)

        #region Retention Policies

        public override async Task<IEnumerable<InfluxDbRetentionPolicy>> GetRetentionPoliciesAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            var bucket = await client.GetBucketsApi().FindBucketByNameAsync(database.Trim()).ConfigureAwait(false);
            var list = new List<InfluxDbRetentionPolicy>();
            if (bucket == null) return list;

            var rule = bucket.RetentionRules?.FirstOrDefault();
            var duration = (rule == null || !rule.EverySeconds.HasValue || rule.EverySeconds.Value <= 0)
                ? "INF"
                : FormatDurationSeconds(rule.EverySeconds.Value);

            var shardDuration = (rule != null && rule.ShardGroupDurationSeconds.HasValue && rule.ShardGroupDurationSeconds.Value > 0)
                ? FormatDurationSeconds(rule.ShardGroupDurationSeconds.Value)
                : string.Empty;

            list.Add(new InfluxDbRetentionPolicy
            {
                Name = "bucket-retention",
                Database = database,
                Duration = duration,
                ShardGroupDuration = shardDuration,
                ReplicationCopies = 1,
                Default = true
            });

            return list;
        }

        public override Task<InfluxDbApiResponse> CreateRetentionPolicyAsync(string database, string policyName, string duration, int replication, bool isDefault = false)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "InfluxDB 2.x uses a single retention rule per bucket instead of creating multiple named retention policies.",
                HttpStatusCode.NotImplemented, false));
        }

        public override async Task<InfluxDbApiResponse> AlterRetentionPolicyAsync(string database, string policyName, string duration, int replication, bool isDefault = false)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            var seconds = ParseDurationToSeconds(duration);
            var bucketsApi = client.GetBucketsApi();
            var bucket = await bucketsApi.FindBucketByNameAsync(database.Trim()).ConfigureAwait(false);
            if (bucket == null)
                return new InfluxDbApiResponse($"Bucket '{database}' not found.", HttpStatusCode.NotFound, false);

            bucket.RetentionRules = seconds <= 0
                ? new List<BucketRetentionRules>()
                : new List<BucketRetentionRules> { new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, seconds) };

            await bucketsApi.UpdateBucketAsync(bucket).ConfigureAwait(false);
            return new InfluxDbApiResponse(string.Empty, HttpStatusCode.OK, true);
        }

        public override Task<InfluxDbApiResponse> DropRetentionPolicyAsync(string database, string policyName)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "Dropping retention policies is not supported in InfluxDB 2.x. Edit the bucket retention duration to INF instead.",
                HttpStatusCode.NotImplemented, false));
        }

        #endregion Retention Policies

        #region Measurements

        public override async Task<IEnumerable<string>> GetMeasurementNamesAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var flux = $"import \"influxdata/influxdb/schema\"\r\nschema.measurements(bucket: \"{EscapeFluxString(database)}\")";
            var tables = await client.GetQueryApi().QueryAsync(flux, org).ConfigureAwait(false);
            return ExtractSingleStringColumn(tables, "_value")
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public override async Task<InfluxDbApiResponse> DropMeasurementAsync(string database, string measurement)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));

            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var start = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var stop = DateTime.UtcNow.AddYears(1);
            var predicate = $"_measurement=\"{EscapeFluxString(measurement)}\"";
            await client.GetDeleteApi().Delete(start, stop, predicate, database, org).ConfigureAwait(false);
            return new InfluxDbApiResponse(string.Empty, HttpStatusCode.NoContent, true);
        }

        public override async Task<IEnumerable<string>> GetTagKeysAsync(string database, string measurement)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));

            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var flux = $"import \"influxdata/influxdb/schema\"\r\nschema.measurementTagKeys(bucket: \"{EscapeFluxString(database)}\", measurement: \"{EscapeFluxString(measurement)}\")";
            var tables = await client.GetQueryApi().QueryAsync(flux, org).ConfigureAwait(false);

            return ExtractSingleStringColumn(tables, "_value")
                .Where(k => k != "_start" && k != "_stop" && k != "_field" && k != "_measurement")
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public override async Task<IEnumerable<InfluxDbTagValue>> GetTagValuesAsync(string database, string measurement, string tag)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));
            if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentNullException(nameof(tag));

            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var flux = $"import \"influxdata/influxdb/schema\"\r\nschema.measurementTagValues(bucket: \"{EscapeFluxString(database)}\", measurement: \"{EscapeFluxString(measurement)}\", tag: \"{EscapeFluxString(tag)}\")";
            var tables = await client.GetQueryApi().QueryAsync(flux, org).ConfigureAwait(false);

            return ExtractSingleStringColumn(tables, "_value")
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Select(v => new InfluxDbTagValue(tag, v))
                .ToList();
        }

        public override async Task<IEnumerable<InfluxDbFieldKey>> GetFieldKeysAsync(string database, string measurement)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));

            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var flux = $"import \"influxdata/influxdb/schema\"\r\nschema.measurementFieldKeys(bucket: \"{EscapeFluxString(database)}\", measurement: \"{EscapeFluxString(measurement)}\")";
            var tables = await client.GetQueryApi().QueryAsync(flux, org).ConfigureAwait(false);

            return ExtractSingleStringColumn(tables, "_value")
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => new InfluxDbFieldKey(k, "field"))
                .ToList();
        }

        #endregion Measurements

        #region Series (Unsupported in v2)

        public override Task<IEnumerable<string>> GetSeriesNamesAsync(string database, string measurement = null)
        {
            throw new NotSupportedException("InfluxDB 2.x does not expose a v1 SHOW SERIES command.");
        }

        public override Task<InfluxDbApiResponse> DropSeriesAsync(string database, string measurement = null)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "DROP SERIES is an InfluxDB 1.x command. Use Drop Measurement or the v2 Delete API instead.",
                HttpStatusCode.NotImplemented, false));
        }

        #endregion Series

        #region Query

        public override Task<IEnumerable<InfluxDbRunningQuery>> GetRunningQueriesAsync()
        {
            return Task.FromResult<IEnumerable<InfluxDbRunningQuery>>(Array.Empty<InfluxDbRunningQuery>());
        }

        public override Task<InfluxDbApiResponse> KillQueryAsync(int pid)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "KILL QUERY is only supported on InfluxDB 1.x.",
                HttpStatusCode.NotImplemented, false));
        }

        public override async Task<IEnumerable<InfluxDbSeries>> QueryAsync(string database, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var tables = await client.GetQueryApi().QueryAsync(query, org).ConfigureAwait(false);
            return ConvertFluxTablesToSeries(tables, database);
        }

        public override Task<IEnumerable<InfluxDbContinuousQuery>> GetContinousQueriesAsync(string database)
        {
            return Task.FromResult<IEnumerable<InfluxDbContinuousQuery>>(Array.Empty<InfluxDbContinuousQuery>());
        }

        public override Task<InfluxDbApiResponse> CreateContinuousQueryAsync(InfluxDbCqParams cqParams)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "Continuous Queries are specific to InfluxDB 1.x. InfluxDB 2.x uses Tasks instead.",
                HttpStatusCode.NotImplemented, false));
        }

        public override Task<InfluxDbApiResponse> DropContinuousQueryAsync(string database, string cqName)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "Continuous Queries are specific to InfluxDB 1.x.",
                HttpStatusCode.NotImplemented, false));
        }

        public override Task<InfluxDbApiResponse> BackfillAsync(string database, InfluxDbBackfillParams backfillParams)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "Backfill queries are specific to InfluxDB 1.x.",
                HttpStatusCode.NotImplemented, false));
        }

        #endregion Query

        #region Write

        public override Task<InfluxDbApiResponse> WriteAsync(string database, string measurement,
            IDictionary<string, object> tags, IDictionary<string, object> fields, DateTime timeStamp, string retentionPolicy = null)
        {
            return WriteAsync(database, new InfluxDbPoint(measurement, tags, fields, timeStamp), retentionPolicy);
        }

        public override Task<InfluxDbApiResponse> WriteAsync(string database, InfluxDbPoint point, string retentionPolicy = null)
        {
            return WriteAsync(database, new[] { point }, retentionPolicy);
        }

        public override async Task<InfluxDbApiResponse> WriteAsync(string database, IEnumerable<InfluxDbPoint> points, string retentionPolicy = null)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (points == null) throw new ArgumentNullException(nameof(points));

            var org = await ResolveOrgAsync().ConfigureAwait(false);
            var v2Points = new List<PointData>();

            foreach (var p in points)
            {
                var builder = PointData.Measurement(p.Measurement);

                if (p.Tags != null)
                {
                    foreach (var kv in p.Tags)
                    {
                        if (kv.Value != null) builder = builder.Tag(kv.Key, kv.Value.ToString());
                    }
                }

                if (p.Fields != null)
                {
                    foreach (var kv in p.Fields)
                    {
                        builder = builder.Field(kv.Key, kv.Value);
                    }
                }

                var utc = p.TimeStamp.Kind == DateTimeKind.Utc ? p.TimeStamp : p.TimeStamp.ToUniversalTime();
                builder = builder.Timestamp(utc, WritePrecision.Ns);
                v2Points.Add(builder);
            }

            await client.GetWriteApiAsync().WritePointsAsync(v2Points, database, org).ConfigureAwait(false);
            return new InfluxDbApiResponse(string.Empty, HttpStatusCode.NoContent, true);
        }

        #endregion Write

        #region Server

        public override async Task<InfluxDbPingResponse> PingAsync()
        {
            var sw = Stopwatch.StartNew();
            var ok = await client.PingAsync().ConfigureAwait(false);
            string version = null;
            if (ok)
            {
                try { version = await client.VersionAsync().ConfigureAwait(false); } catch { }
            }
            sw.Stop();
            return new InfluxDbPingResponse(ok, sw.Elapsed, string.IsNullOrWhiteSpace(version) ? "2.x" : version);
        }

        public override Task<InfluxDbDiagnostics> GetDiagnosticsAsync()
        {
            throw new NotSupportedException("SHOW DIAGNOSTICS is only supported on InfluxDB 1.x.");
        }

        public override Task<InfluxDbStats> GetStatsAsync()
        {
            throw new NotSupportedException("SHOW STATS is only supported on InfluxDB 1.x.");
        }

        #endregion Server

        #region Users

        public override async Task<IEnumerable<InfluxDbUser>> GetUsersAsync()
        {
            var users = await client.GetUsersApi().FindUsersAsync(limit: 100).ConfigureAwait(false);
            return (users ?? new List<User>())
                .Where(u => !string.IsNullOrWhiteSpace(u.Name))
                .Select(u => new InfluxDbUser(u.Name, false))
                .ToList();
        }

        public override async Task<InfluxDbApiResponse> CreateUserAsync(string username, string password, bool isAdmin)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));
            var created = await client.GetUsersApi().CreateUserAsync(username.Trim()).ConfigureAwait(false);
            if (created != null && !string.IsNullOrEmpty(password))
            {
                try
                {
                    await client.GetUsersApi().UpdateUserPasswordAsync(created.Id, string.Empty, password).ConfigureAwait(false);
                }
                catch
                {
                    // Some v2 editions create users without allowing empty old-password reset immediately
                }
            }

            return new InfluxDbApiResponse(created?.Id ?? username, HttpStatusCode.Created, created != null);
        }

        public override async Task<InfluxDbApiResponse> DropUserAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));
            var usersApi = client.GetUsersApi();
            var matches = await usersApi.FindUsersAsync(name: username.Trim()).ConfigureAwait(false);
            var target = matches?.FirstOrDefault(u => string.Equals(u.Name, username.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target == null)
                return new InfluxDbApiResponse($"User '{username}' not found.", HttpStatusCode.NotFound, false);

            await usersApi.DeleteUserAsync(target).ConfigureAwait(false);
            return new InfluxDbApiResponse(string.Empty, HttpStatusCode.NoContent, true);
        }

        public override async Task<InfluxDbApiResponse> SetPasswordAsync(string username, string password)
        {
            return await Task.FromResult(new InfluxDbApiResponse(
                "InfluxDB 2.x requires the current password to update user passwords via the API.",
                HttpStatusCode.NotImplemented, false));
        }

        public override Task<IEnumerable<InfluxDbGrant>> GetPrivilegesAsync(string username)
        {
            return Task.FromResult<IEnumerable<InfluxDbGrant>>(Array.Empty<InfluxDbGrant>());
        }

        public override Task<InfluxDbApiResponse> GrantAdministratorAsync(string username)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "InfluxDB 2.x manages access through scoped API tokens instead of v1 database grants.",
                HttpStatusCode.NotImplemented, false));
        }

        public override Task<InfluxDbApiResponse> RevokeAdministratorAsync(string username)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "InfluxDB 2.x manages access through scoped API tokens instead of v1 database grants.",
                HttpStatusCode.NotImplemented, false));
        }

        public override Task<InfluxDbApiResponse> GrantPrivilegeAsync(string username, InfluxDbPrivileges privilege, string database)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "InfluxDB 2.x manages access through scoped API tokens instead of v1 database grants.",
                HttpStatusCode.NotImplemented, false));
        }

        public override Task<InfluxDbApiResponse> RevokePrivilegeAsync(string username, InfluxDbPrivileges privilege, string database)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "InfluxDB 2.x manages access through scoped API tokens instead of v1 database grants.",
                HttpStatusCode.NotImplemented, false));
        }

        #endregion Users

        #region Helpers

        public static List<InfluxDbSeries> ConvertFluxTablesToSeries(IEnumerable<FluxTable> tables, string fallbackName)
        {
            var results = new List<InfluxDbSeries>();
            if (tables == null) return results;

            foreach (var table in tables)
            {
                if (table.Columns == null || table.Columns.Count == 0) continue;

                var columns = table.Columns
                    .Select(c => string.IsNullOrEmpty(c.Label) ? $"col_{c.Index}" : c.Label)
                    .ToList();

                var rows = new List<IList<object>>();
                string seriesName = null;

                if (table.Records != null)
                {
                    foreach (var record in table.Records)
                    {
                        if (seriesName == null)
                        {
                            try { seriesName = record.GetMeasurement(); } catch { }
                            if (string.IsNullOrEmpty(seriesName))
                            {
                                try { seriesName = record.GetField(); } catch { }
                            }
                        }

                        var rowValues = new List<object>(columns.Count);
                        foreach (var col in table.Columns)
                        {
                            object val = record.GetValueByIndex(col.Index);
                            if (val is Instant instant)
                            {
                                val = instant.ToDateTimeUtc();
                            }
                            rowValues.Add(val);
                        }

                        rows.Add(rowValues);
                    }
                }

                results.Add(new InfluxDbSeries(
                    string.IsNullOrEmpty(seriesName) ? (fallbackName ?? "flux_result") : seriesName,
                    columns,
                    new Dictionary<string, string>(),
                    rows));
            }

            return results;
        }

        private static IEnumerable<string> ExtractSingleStringColumn(IEnumerable<FluxTable> tables, string columnLabel)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (tables == null) return seen;

            foreach (var table in tables)
            {
                if (table.Records == null) continue;
                foreach (var record in table.Records)
                {
                    var val = record.GetValueByKey(columnLabel)?.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) seen.Add(val);
                }
            }

            return seen;
        }

        public static string EscapeFluxString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatDurationSeconds(long seconds)
        {
            if (seconds <= 0) return "INF";
            if (seconds % 604800 == 0) return $"{seconds / 604800}w";
            if (seconds % 86400 == 0) return $"{seconds / 86400}d";
            if (seconds % 3600 == 0) return $"{seconds / 3600}h";
            if (seconds % 60 == 0) return $"{seconds / 60}m";
            return $"{seconds}s";
        }

        private static long ParseDurationToSeconds(string duration)
        {
            if (string.IsNullOrWhiteSpace(duration)) return 0;
            var d = duration.Trim();
            if (d.Equals("INF", StringComparison.OrdinalIgnoreCase) || d == "0" || d.Equals("0s", StringComparison.OrdinalIgnoreCase))
                return 0;

            if (d.Length >= 2 && long.TryParse(d.Substring(0, d.Length - 1), out var num))
            {
                switch (char.ToLowerInvariant(d[d.Length - 1]))
                {
                    case 's': return num;
                    case 'm': return num * 60;
                    case 'h': return num * 3600;
                    case 'd': return num * 86400;
                    case 'w': return num * 604800;
                }
            }

            if (long.TryParse(d, out var rawSeconds)) return rawSeconds;
            throw new FormatException($"Unsupported retention duration format: '{duration}'. Use formats like 7d, 24h, 30m, 60s, or INF.");
        }

        #endregion Helpers

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client?.Dispose();
            ownedHttpClient?.Dispose();
        }
    }
}
