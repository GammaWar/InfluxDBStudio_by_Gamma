using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Apache.Arrow;
using InfluxDB3.Client;
using InfluxDB3.Client.Config;
using InfluxDB3.Client.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using V3PointData = InfluxDB3.Client.Write.PointData;

namespace CymaticLabs.InfluxDB.Data
{
    /// <summary>
    /// InfluxDB 3.x client implementation backed by InfluxDB3.Client (Arrow Flight SQL + InfluxQL).
    /// </summary>
    public class InfluxDbV3Client : InfluxDbClient, IDisposable
    {
        private static readonly Regex InfluxQlQueryRegex = new Regex(
            @"^\s*SHOW\s+(MEASUREMENTS|TAG\s+KEYS|TAG\s+VALUES|FIELD\s+KEYS|SERIES|RETENTION\s+POLICIES|CONTINUOUS\s+QUERIES|DIAGNOSTICS|STATS)\b|\bGROUP\s+BY\s+time\s*\(|\bnow\s*\(\s*\)\s*-\s*\d+[a-z]+\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly InfluxDBClient client;
        private readonly HttpClient auxHttpClient;
        private readonly bool ownsAuxHttpClient;
        private bool disposed;

        public override InfluxDbApiVersion ApiVersion => InfluxDbApiVersion.V3;
        public override bool SupportsRetentionPolicies => false;
        public override bool SupportsCreateRetentionPolicy => false;
        public override bool SupportsContinuousQueries => false;
        public override bool SupportsBackfill => false;
        public override bool SupportsUsers => false;
        public override bool SupportsRunningQueries => false;
        public override bool SupportsDiagnostics => false;
        public override bool SupportsStats => false;
        public override bool SupportsSeriesDiscovery => false;
        public override bool SupportsDropSeries => false;
        public override bool SupportsDropMeasurement => false;

        public InfluxDbV3Client(InfluxDbConnection connection, HttpClient httpClientOverride = null)
            : base(connection)
        {
            var config = new ClientConfig
            {
                Host = connection.HttpConnectionString,
                Token = connection.SecurityToken?.Trim(),
                Organization = connection.Organization?.Trim(),
                Database = connection.Database?.Trim(),
                DisableServerCertificateValidation = SslIgnoreValidator.AllowUntrusted,
                QueryTimeout = TimeSpan.FromSeconds(100),
                WriteTimeout = TimeSpan.FromSeconds(30)
            };

            if (httpClientOverride != null)
            {
                config.HttpClient = httpClientOverride;
                auxHttpClient = httpClientOverride;
                ownsAuxHttpClient = false;
            }
            else
            {
                auxHttpClient = new HttpClient(SslIgnoreValidator.CreateHttpClientHandler(), disposeHandler: true);
                auxHttpClient.Timeout = TimeSpan.FromSeconds(30);
                ownsAuxHttpClient = true;
            }

            client = new InfluxDBClient(config);
        }

        public override string GetDefaultQuery(string database, string measurement)
        {
            var m = EscapeSqlIdentifier(string.IsNullOrWhiteSpace(measurement) ? "measurement" : measurement);
            return $"SELECT *\r\nFROM \"{m}\"\r\nWHERE time >= now() - interval '5 minutes'\r\nORDER BY time DESC\r\nLIMIT 100";
        }

        public static QueryType DetectQueryType(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return QueryType.SQL;
            return InfluxQlQueryRegex.IsMatch(query) ? QueryType.InfluxQL : QueryType.SQL;
        }

        #region Databases

        public override async Task<IEnumerable<string>> GetDatabaseNamesAsync()
        {
            // 1. Try InfluxDB 3 Core / Enterprise configure endpoint
            try
            {
                using (var req = CreateHttpRequest(HttpMethod.Get, "api/v3/configure/database"))
                using (var resp = await auxHttpClient.SendAsync(req).ConfigureAwait(false))
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var parsed = ParseV3DatabaseListJson(body);
                        if (parsed.Count > 0) return parsed;
                    }
                }
            }
            catch
            {
                // Fall back to SQL SHOW DATABASES or v2 buckets
            }

            // 2. Try SQL SHOW DATABASES (works in InfluxDB 3 Core / Enterprise)
            try
            {
                var seriesList = await ExecuteQueryInternalAsync(Connection.Database, "SHOW DATABASES", QueryType.SQL).ConfigureAwait(false);
                var names = new List<string>();
                foreach (var s in seriesList)
                {
                    if (s.Values == null) continue;
                    foreach (var row in s.Values)
                    {
                        var val = row.FirstOrDefault()?.ToString();
                        if (!string.IsNullOrWhiteSpace(val)) names.Add(val);
                    }
                }
                if (names.Count > 0)
                    return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                // Fall back to /api/v2/buckets (Cloud Serverless / Dedicated compatibility)
            }

            // 3. Try /api/v2/buckets
            using (var req = CreateHttpRequest(HttpMethod.Get, "api/v2/buckets?limit=100"))
            using (var resp = await auxHttpClient.SendAsync(req).ConfigureAwait(false))
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Failed to list InfluxDB 3.x databases (HTTP {(int)resp.StatusCode}): {body}");
                }

                var obj = JObject.Parse(body);
                var buckets = obj["buckets"] as JArray;
                var list = new List<string>();
                if (buckets != null)
                {
                    foreach (var b in buckets)
                    {
                        var name = b.Value<string>("name");
                        if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                    }
                }
                return list.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public override async Task<InfluxDbApiResponse> CreateDatabaseAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));

            using (var req = CreateHttpRequest(HttpMethod.Post, "api/v3/configure/database"))
            {
                var payload = JsonConvert.SerializeObject(new { db = database.Trim() });
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using (var resp = await auxHttpClient.SendAsync(req).ConfigureAwait(false))
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new InfluxDbApiResponse(body, resp.StatusCode, resp.IsSuccessStatusCode);
                }
            }
        }

        public override async Task<InfluxDbApiResponse> DropDatabaseAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));

            using (var req = CreateHttpRequest(HttpMethod.Delete, $"api/v3/configure/database?db={Uri.EscapeDataString(database.Trim())}"))
            using (var resp = await auxHttpClient.SendAsync(req).ConfigureAwait(false))
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new InfluxDbApiResponse(body, resp.StatusCode, resp.IsSuccessStatusCode);
            }
        }

        #endregion Databases

        #region Measurements

        public override async Task<IEnumerable<string>> GetMeasurementNamesAsync(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));

            const string sql = "SELECT table_name FROM information_schema.tables WHERE table_schema NOT IN ('system', 'information_schema') ORDER BY table_name";
            var results = await ExecuteQueryInternalAsync(database, sql, QueryType.SQL).ConfigureAwait(false);
            var names = new List<string>();
            foreach (var s in results)
            {
                if (s.Values == null) continue;
                foreach (var row in s.Values)
                {
                    var name = row.FirstOrDefault()?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override async Task<IEnumerable<string>> GetTagKeysAsync(string database, string measurement)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));

            var influxQl = $"SHOW TAG KEYS FROM \"{EscapeSqlIdentifier(measurement)}\"";
            var series = await ExecuteQueryInternalAsync(database, influxQl, QueryType.InfluxQL).ConfigureAwait(false);
            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var s in series)
            {
                if (s.Values == null) continue;
                var idx = s.GetColumnIndex("tagKey");
                if (idx < 0) idx = s.Columns.Count - 1;
                if (idx < 0) continue;

                foreach (var row in s.Values)
                {
                    var key = row[idx]?.ToString();
                    if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
                }
            }

            return keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override async Task<IEnumerable<InfluxDbTagValue>> GetTagValuesAsync(string database, string measurement, string tag)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));
            if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentNullException(nameof(tag));

            var t = EscapeSqlIdentifier(tag);
            var m = EscapeSqlIdentifier(measurement);
            var sql = $"SELECT DISTINCT \"{t}\" FROM \"{m}\" WHERE \"{t}\" IS NOT NULL ORDER BY 1 LIMIT 1000";
            var series = await ExecuteQueryInternalAsync(database, sql, QueryType.SQL).ConfigureAwait(false);
            var values = new List<InfluxDbTagValue>();

            foreach (var s in series)
            {
                if (s.Values == null) continue;
                foreach (var row in s.Values)
                {
                    var val = row.FirstOrDefault()?.ToString();
                    if (val != null) values.Add(new InfluxDbTagValue(tag, val));
                }
            }

            return values;
        }

        public override async Task<IEnumerable<InfluxDbFieldKey>> GetFieldKeysAsync(string database, string measurement)
        {
            if (string.IsNullOrWhiteSpace(database)) throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(measurement)) throw new ArgumentNullException(nameof(measurement));

            var influxQl = $"SHOW FIELD KEYS FROM \"{EscapeSqlIdentifier(measurement)}\"";
            var series = await ExecuteQueryInternalAsync(database, influxQl, QueryType.InfluxQL).ConfigureAwait(false);
            var fields = new List<InfluxDbFieldKey>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var s in series)
            {
                if (s.Values == null) continue;
                var keyIdx = s.GetColumnIndex("fieldKey");
                var typeIdx = s.GetColumnIndex("fieldType");
                if (keyIdx < 0) keyIdx = 0;

                foreach (var row in s.Values)
                {
                    var name = keyIdx < row.Count ? row[keyIdx]?.ToString() : null;
                    var type = (typeIdx >= 0 && typeIdx < row.Count ? row[typeIdx]?.ToString() : null) ?? "field";
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    {
                        fields.Add(new InfluxDbFieldKey(name, type));
                    }
                }
            }

            return fields.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override Task<InfluxDbApiResponse> DropMeasurementAsync(string database, string measurement)
        {
            return Task.FromResult(new InfluxDbApiResponse(
                "Dropping individual measurements/tables is not supported via this InfluxDB 3.x client.",
                HttpStatusCode.NotImplemented, false));
        }

        #endregion Measurements

        #region Query

        public override async Task<IEnumerable<InfluxDbSeries>> QueryAsync(string database, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            var queryType = DetectQueryType(query);
            return await ExecuteQueryInternalAsync(database, query, queryType).ConfigureAwait(false);
        }

        private async Task<List<InfluxDbSeries>> ExecuteQueryInternalAsync(string database, string query, QueryType queryType)
        {
            var targetDb = !string.IsNullOrWhiteSpace(database) ? database.Trim() : Connection.Database;
            var batches = new List<RecordBatch>();

            await foreach (var batch in client.QueryBatches(query, queryType: queryType, database: targetDb).ConfigureAwait(false))
            {
                batches.Add(batch);
            }

            try
            {
                return ConvertArrowBatchesToSeries(batches, targetDb ?? "result");
            }
            finally
            {
                foreach (var b in batches) b?.Dispose();
            }
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

            var v3Points = new List<V3PointData>();
            foreach (var p in points)
            {
                var pt = V3PointData.Measurement(p.Measurement);
                if (p.Tags != null)
                {
                    foreach (var kv in p.Tags)
                    {
                        if (kv.Value != null) pt.SetTag(kv.Key, kv.Value.ToString());
                    }
                }

                if (p.Fields != null)
                {
                    foreach (var kv in p.Fields)
                    {
                        pt.SetField(kv.Key, kv.Value);
                    }
                }

                var utc = p.TimeStamp.Kind == DateTimeKind.Utc ? p.TimeStamp : p.TimeStamp.ToUniversalTime();
                pt.SetTimestamp(utc);
                v3Points.Add(pt);
            }

            await client.WritePointsAsync(v3Points, database: database).ConfigureAwait(false);
            return new InfluxDbApiResponse(string.Empty, HttpStatusCode.NoContent, true);
        }

        #endregion Write

        #region Server

        public override async Task<InfluxDbPingResponse> PingAsync()
        {
            var sw = Stopwatch.StartNew();
            string version = null;
            var ok = false;

            try
            {
                version = await client.GetServerVersion().ConfigureAwait(false);
                ok = true;
            }
            catch
            {
                using (var req = CreateHttpRequest(HttpMethod.Get, "ping"))
                using (var resp = await auxHttpClient.SendAsync(req).ConfigureAwait(false))
                {
                    ok = resp.IsSuccessStatusCode;
                    if (resp.Headers.TryGetValues("X-Influxdb-Version", out var values))
                        version = values.FirstOrDefault();
                }
            }

            sw.Stop();
            return new InfluxDbPingResponse(ok, sw.Elapsed, string.IsNullOrWhiteSpace(version) ? "3.x" : version);
        }

        public override Task<InfluxDbDiagnostics> GetDiagnosticsAsync() =>
            throw new NotSupportedException("SHOW DIAGNOSTICS is only supported on InfluxDB 1.x.");

        public override Task<InfluxDbStats> GetStatsAsync() =>
            throw new NotSupportedException("SHOW STATS is only supported on InfluxDB 1.x.");

        #endregion Server

        #region Unsupported v1-only Operations

        public override Task<IEnumerable<InfluxDbRetentionPolicy>> GetRetentionPoliciesAsync(string database) =>
            Task.FromResult<IEnumerable<InfluxDbRetentionPolicy>>(System.Array.Empty<InfluxDbRetentionPolicy>());

        public override Task<InfluxDbApiResponse> CreateRetentionPolicyAsync(string database, string policyName, string duration, int replication, bool isDefault = false) =>
            Task.FromResult(UnsupportedResponse("Retention Policies are specific to InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> AlterRetentionPolicyAsync(string database, string policyName, string duration, int replication, bool isDefault = false) =>
            Task.FromResult(UnsupportedResponse("Retention Policies are specific to InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> DropRetentionPolicyAsync(string database, string policyName) =>
            Task.FromResult(UnsupportedResponse("Retention Policies are specific to InfluxDB 1.x."));

        public override Task<IEnumerable<string>> GetSeriesNamesAsync(string database, string measurement = null) =>
            throw new NotSupportedException("SHOW SERIES is only supported on InfluxDB 1.x.");

        public override Task<InfluxDbApiResponse> DropSeriesAsync(string database, string measurement = null) =>
            Task.FromResult(UnsupportedResponse("DROP SERIES is only supported on InfluxDB 1.x."));

        public override Task<IEnumerable<InfluxDbRunningQuery>> GetRunningQueriesAsync() =>
            Task.FromResult<IEnumerable<InfluxDbRunningQuery>>(System.Array.Empty<InfluxDbRunningQuery>());

        public override Task<InfluxDbApiResponse> KillQueryAsync(int pid) =>
            Task.FromResult(UnsupportedResponse("KILL QUERY is only supported on InfluxDB 1.x."));

        public override Task<IEnumerable<InfluxDbContinuousQuery>> GetContinousQueriesAsync(string database) =>
            Task.FromResult<IEnumerable<InfluxDbContinuousQuery>>(System.Array.Empty<InfluxDbContinuousQuery>());

        public override Task<InfluxDbApiResponse> CreateContinuousQueryAsync(InfluxDbCqParams cqParams) =>
            Task.FromResult(UnsupportedResponse("Continuous Queries are specific to InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> DropContinuousQueryAsync(string database, string cqName) =>
            Task.FromResult(UnsupportedResponse("Continuous Queries are specific to InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> BackfillAsync(string database, InfluxDbBackfillParams backfillParams) =>
            Task.FromResult(UnsupportedResponse("Backfill is only supported on InfluxDB 1.x."));

        public override Task<IEnumerable<InfluxDbUser>> GetUsersAsync() =>
            Task.FromResult<IEnumerable<InfluxDbUser>>(System.Array.Empty<InfluxDbUser>());

        public override Task<InfluxDbApiResponse> CreateUserAsync(string username, string password, bool isAdmin) =>
            Task.FromResult(UnsupportedResponse("User management is only supported on InfluxDB 1.x/2.x."));

        public override Task<InfluxDbApiResponse> DropUserAsync(string username) =>
            Task.FromResult(UnsupportedResponse("User management is only supported on InfluxDB 1.x/2.x."));

        public override Task<InfluxDbApiResponse> SetPasswordAsync(string username, string password) =>
            Task.FromResult(UnsupportedResponse("User management is only supported on InfluxDB 1.x."));

        public override Task<IEnumerable<InfluxDbGrant>> GetPrivilegesAsync(string username) =>
            Task.FromResult<IEnumerable<InfluxDbGrant>>(System.Array.Empty<InfluxDbGrant>());

        public override Task<InfluxDbApiResponse> GrantAdministratorAsync(string username) =>
            Task.FromResult(UnsupportedResponse("User grants are only supported on InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> RevokeAdministratorAsync(string username) =>
            Task.FromResult(UnsupportedResponse("User grants are only supported on InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> GrantPrivilegeAsync(string username, InfluxDbPrivileges privilege, string database) =>
            Task.FromResult(UnsupportedResponse("User grants are only supported on InfluxDB 1.x."));

        public override Task<InfluxDbApiResponse> RevokePrivilegeAsync(string username, InfluxDbPrivileges privilege, string database) =>
            Task.FromResult(UnsupportedResponse("User grants are only supported on InfluxDB 1.x."));

        #endregion

        #region Helpers

        private static InfluxDbApiResponse UnsupportedResponse(string message) =>
            new InfluxDbApiResponse(message, HttpStatusCode.NotImplemented, false);

        private HttpRequestMessage CreateHttpRequest(HttpMethod method, string relativePath)
        {
            var baseUrl = Connection.HttpConnectionString.TrimEnd('/');
            var path = relativePath.TrimStart('/');
            var req = new HttpRequestMessage(method, $"{baseUrl}/{path}");
            if (!string.IsNullOrWhiteSpace(Connection.SecurityToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Connection.SecurityToken.Trim());
            }
            return req;
        }

        public static List<string> ParseV3DatabaseListJson(string body)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(body)) return result;

            var trimmed = body.Trim();
            if (trimmed.StartsWith("["))
            {
                var arr = JArray.Parse(trimmed);
                foreach (var item in arr)
                {
                    if (item.Type == JTokenType.String)
                    {
                        var s = item.Value<string>();
                        if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                    }
                    else if (item is JObject obj)
                    {
                        var name = obj.Value<string>("iox::database")
                            ?? obj.Value<string>("database_name")
                            ?? obj.Value<string>("db")
                            ?? obj.Value<string>("name");
                        if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                    }
                }
            }
            else if (trimmed.StartsWith("{"))
            {
                var obj = JObject.Parse(trimmed);
                var dbs = obj["databases"] as JArray;
                if (dbs != null)
                {
                    foreach (var d in dbs)
                    {
                        var name = d.Type == JTokenType.String ? d.Value<string>() : (d["name"]?.Value<string>() ?? d["db"]?.Value<string>());
                        if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                    }
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<InfluxDbSeries> ConvertArrowBatchesToSeries(IEnumerable<RecordBatch> batches, string seriesName)
        {
            var result = new List<InfluxDbSeries>();
            if (batches == null) return result;

            List<string> columns = null;
            var rows = new List<IList<object>>();

            foreach (var batch in batches)
            {
                if (batch == null) continue;
                var batchCols = batch.Schema.FieldsList.Select(f => f.Name).ToList();
                if (columns == null)
                {
                    columns = batchCols;
                }
                else if (!columns.SequenceEqual(batchCols))
                {
                    result.Add(new InfluxDbSeries(seriesName ?? "result", columns, new Dictionary<string, string>(), rows));
                    columns = batchCols;
                    rows = new List<IList<object>>();
                }

                for (int r = 0; r < batch.Length; r++)
                {
                    var row = new List<object>(batch.ColumnCount);
                    for (int c = 0; c < batch.ColumnCount; c++)
                    {
                        row.Add(ExtractArrowValue(batch.Column(c), r));
                    }
                    rows.Add(row);
                }
            }

            if (columns != null)
            {
                result.Add(new InfluxDbSeries(seriesName ?? "result", columns, new Dictionary<string, string>(), rows));
            }

            return result;
        }

        private static object ExtractArrowValue(IArrowArray array, int index)
        {
            if (array == null || array.IsNull(index)) return null;

            switch (array)
            {
                case StringArray sa: return sa.GetString(index);
                case StringViewArray sva: return sva.GetString(index);
                case LargeStringArray lsa: return lsa.GetString(index);
                case BooleanArray ba: return ba.GetValue(index);
                case Int8Array i8: return i8.GetValue(index);
                case Int16Array i16: return i16.GetValue(index);
                case Int32Array i32: return i32.GetValue(index);
                case Int64Array i64: return i64.GetValue(index);
                case UInt8Array u8: return u8.GetValue(index);
                case UInt16Array u16: return u16.GetValue(index);
                case UInt32Array u32: return u32.GetValue(index);
                case UInt64Array u64: return u64.GetValue(index);
                case FloatArray fa: return fa.GetValue(index);
                case DoubleArray da: return da.GetValue(index);
                case Decimal128Array d128: return d128.GetValue(index);
                case Decimal256Array d256: return d256.GetString(index);
                case TimestampArray ts: return ts.GetTimestamp(index)?.UtcDateTime;
                case Date32Array d32: return d32.GetDateTime(index);
                case Date64Array d64: return d64.GetDateTime(index);
                case DurationArray dur: return dur.GetTimeSpan(index);
                case DictionaryArray dict:
                    var keyObj = ExtractArrowValue(dict.Indices, index);
                    if (keyObj != null && int.TryParse(keyObj.ToString(), out var dictIdx))
                        return ExtractArrowValue(dict.Dictionary, dictIdx);
                    return null;
                case BinaryArray bin:
                    return BitConverter.ToString(bin.GetBytes(index).ToArray());
                default:
                    return array.ToString();
            }
        }

        public static string EscapeSqlIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return string.Empty;
            return identifier.Replace("\"", "\"\"");
        }

        #endregion Helpers

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client?.Dispose();
            if (ownsAuxHttpClient) auxHttpClient?.Dispose();
        }
    }
}
