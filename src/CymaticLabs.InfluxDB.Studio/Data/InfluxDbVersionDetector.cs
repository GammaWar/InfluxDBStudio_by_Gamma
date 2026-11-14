using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CymaticLabs.InfluxDB.Data
{
    /// <summary>
    /// Result of probing an InfluxDB server to determine its API generation (v1, v2, or v3).
    /// </summary>
    public class InfluxDbDetectionResult
    {
        public bool Detected { get; set; }
        public InfluxDbApiVersion ApiVersion { get; set; }
        public string ServerVersion { get; set; }
        public string Build { get; set; }
        public string Details { get; set; }
        public TimeSpan ResponseTime { get; set; }
    }

    /// <summary>
    /// Probes InfluxDB HTTP endpoints (/ping, /health, /api/v3, /api/v2) to distinguish InfluxDB 1.x, 2.x, and 3.x.
    /// </summary>
    public static class InfluxDbVersionDetector
    {
        private static readonly Regex MajorVersionRegex = new Regex(@"(?:^|v)(\d+)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<InfluxDbDetectionResult> DetectAsync(
            InfluxDbConnection connection,
            HttpMessageHandler handlerOverride = null,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var disposeHandler = handlerOverride == null;
            var handler = handlerOverride ?? SslIgnoreValidator.CreateHttpClientHandler();

            try
            {
                using (var http = new HttpClient(handler, disposeHandler))
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    http.BaseAddress = new Uri(connection.HttpConnectionString.TrimEnd('/') + "/");

                    // Step 1: Probe /ping (most InfluxDB 1.x, 2.x, 3.x servers expose /ping with version headers)
                    var pingResponse = await SafeSendAsync(http, "ping", connection, cancellationToken);
                    HttpStatusCode? pingStatus = null;
                    string buildHeader = null;
                    string versionHeader = null;

                    if (pingResponse != null)
                    {
                        using (pingResponse)
                        {
                            pingStatus = pingResponse.StatusCode;
                            versionHeader = GetHeaderValue(pingResponse, "X-Influxdb-Version");
                            buildHeader = GetHeaderValue(pingResponse, "X-Influxdb-Build");

                            var fromHeaders = ClassifyFromVersionAndBuild(versionHeader, buildHeader);
                            if (fromHeaders != InfluxDbApiVersion.Auto)
                            {
                                sw.Stop();
                                return new InfluxDbDetectionResult
                                {
                                    Detected = true,
                                    ApiVersion = fromHeaders,
                                    ServerVersion = versionHeader ?? buildHeader ?? fromHeaders.ToString(),
                                    Build = buildHeader,
                                    ResponseTime = sw.Elapsed,
                                    Details = $"Detected via /ping headers (Version: {versionHeader ?? "n/a"}, Build: {buildHeader ?? "n/a"})."
                                };
                            }

                            var pingBody = await SafeReadBodyAsync(pingResponse);
                            var bodyVersion = TryExtractVersionFromJson(pingBody, out var pingBuild);
                            var fromBody = ClassifyFromVersionAndBuild(bodyVersion, pingBuild ?? buildHeader);
                            if (fromBody != InfluxDbApiVersion.Auto)
                            {
                                sw.Stop();
                                return new InfluxDbDetectionResult
                                {
                                    Detected = true,
                                    ApiVersion = fromBody,
                                    ServerVersion = bodyVersion,
                                    Build = pingBuild ?? buildHeader,
                                    ResponseTime = sw.Elapsed,
                                    Details = $"Detected via /ping response body ({bodyVersion})."
                                };
                            }
                        }
                    }

                    // Step 2: Probe /health (InfluxDB 2.x and 3.x return JSON with version; 1.x typically returns 404)
                    var healthResponse = await SafeSendAsync(http, "health", connection, cancellationToken);
                    if (healthResponse != null)
                    {
                        using (healthResponse)
                        {
                            var hVerHeader = GetHeaderValue(healthResponse, "X-Influxdb-Version") ?? versionHeader;
                            var hBuildHeader = GetHeaderValue(healthResponse, "X-Influxdb-Build") ?? buildHeader;
                            var fromHealthHeaders = ClassifyFromVersionAndBuild(hVerHeader, hBuildHeader);
                            if (fromHealthHeaders != InfluxDbApiVersion.Auto)
                            {
                                sw.Stop();
                                return new InfluxDbDetectionResult
                                {
                                    Detected = true,
                                    ApiVersion = fromHealthHeaders,
                                    ServerVersion = hVerHeader ?? hBuildHeader,
                                    Build = hBuildHeader,
                                    ResponseTime = sw.Elapsed,
                                    Details = $"Detected via /health headers ({hVerHeader ?? hBuildHeader})."
                                };
                            }

                            var healthBody = await SafeReadBodyAsync(healthResponse);
                            var healthJsonVer = TryExtractVersionFromJson(healthBody, out var healthJsonBuild);
                            var fromHealthBody = ClassifyFromVersionAndBuild(healthJsonVer, healthJsonBuild ?? hBuildHeader);
                            if (fromHealthBody != InfluxDbApiVersion.Auto)
                            {
                                sw.Stop();
                                return new InfluxDbDetectionResult
                                {
                                    Detected = true,
                                    ApiVersion = fromHealthBody,
                                    ServerVersion = healthJsonVer,
                                    Build = healthJsonBuild ?? hBuildHeader,
                                    ResponseTime = sw.Elapsed,
                                    Details = $"Detected via /health JSON ({healthJsonVer})."
                                };
                            }
                        }
                    }

                    // Step 3: Probe v3-specific SQL/configure endpoint before v2 endpoints
                    var v3Probe = await SafeSendAsync(http, "api/v3/query_sql", connection, cancellationToken);
                    if (v3Probe != null)
                    {
                        using (v3Probe)
                        {
                            // 400 (missing query), 405 (POST expected), 401/403 (auth required) vs 404 (not v3)
                            var v3Ver = GetHeaderValue(v3Probe, "X-Influxdb-Version") ?? versionHeader;
                            var v3Build = GetHeaderValue(v3Probe, "X-Influxdb-Build") ?? buildHeader;
                            var classified = ClassifyFromVersionAndBuild(v3Ver, v3Build);
                            if (classified == InfluxDbApiVersion.V3 ||
                                (v3Probe.StatusCode != HttpStatusCode.NotFound &&
                                 (v3Probe.StatusCode == HttpStatusCode.MethodNotAllowed ||
                                  v3Probe.StatusCode == HttpStatusCode.BadRequest)))
                            {
                                sw.Stop();
                                return new InfluxDbDetectionResult
                                {
                                    Detected = true,
                                    ApiVersion = InfluxDbApiVersion.V3,
                                    ServerVersion = v3Ver ?? "3.x",
                                    Build = v3Build,
                                    ResponseTime = sw.Elapsed,
                                    Details = $"Detected via /api/v3/query_sql probe (HTTP {(int)v3Probe.StatusCode})."
                                };
                            }
                        }
                    }

                    // Step 4: Probe /api/v2/buckets vs v1 /query
                    var v2Probe = await SafeSendAsync(http, "api/v2/buckets", connection, cancellationToken);
                    HttpStatusCode? v2Status = null;
                    if (v2Probe != null)
                    {
                        using (v2Probe)
                        {
                            v2Status = v2Probe.StatusCode;
                            var v2Ver = GetHeaderValue(v2Probe, "X-Influxdb-Version");
                            var v2Build = GetHeaderValue(v2Probe, "X-Influxdb-Build");
                            var classified = ClassifyFromVersionAndBuild(v2Ver, v2Build);
                            if (classified != InfluxDbApiVersion.Auto)
                            {
                                sw.Stop();
                                return new InfluxDbDetectionResult
                                {
                                    Detected = true,
                                    ApiVersion = classified,
                                    ServerVersion = v2Ver ?? v2Build,
                                    Build = v2Build,
                                    ResponseTime = sw.Elapsed,
                                    Details = $"Detected via /api/v2/buckets headers ({v2Ver ?? v2Build})."
                                };
                            }
                        }
                    }

                    if (v2Status == HttpStatusCode.OK)
                    {
                        // Server responded 200 OK on /api/v2/buckets without v3 markers or version headers
                        sw.Stop();
                        return new InfluxDbDetectionResult
                        {
                            Detected = true,
                            ApiVersion = InfluxDbApiVersion.V2,
                            ServerVersion = versionHeader ?? "2.x",
                            Build = buildHeader,
                            ResponseTime = sw.Elapsed,
                            Details = "Detected InfluxDB 2.x via /api/v2/buckets availability."
                        };
                    }

                    if (v2Status == HttpStatusCode.NotFound && pingStatus.HasValue &&
                        (pingStatus.Value == HttpStatusCode.NoContent || pingStatus.Value == HttpStatusCode.OK))
                    {
                        // Responded to /ping cleanly while /api/v2 returned 404 => InfluxDB 1.x
                        sw.Stop();
                        return new InfluxDbDetectionResult
                        {
                            Detected = true,
                            ApiVersion = InfluxDbApiVersion.V1,
                            ServerVersion = versionHeader ?? "1.x",
                            Build = buildHeader,
                            ResponseTime = sw.Elapsed,
                            Details = "Detected InfluxDB 1.x (/ping responded cleanly and /api/v2 returned 404)."
                        };
                    }

                    sw.Stop();
                    return new InfluxDbDetectionResult
                    {
                        Detected = false,
                        ApiVersion = InfluxDbApiVersion.Auto,
                        ServerVersion = versionHeader,
                        Build = buildHeader,
                        ResponseTime = sw.Elapsed,
                        Details = "Could not definitively distinguish InfluxDB 1.x / 2.x / 3.x from server responses. Please select the API version manually."
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new InfluxDbDetectionResult
                {
                    Detected = false,
                    ApiVersion = InfluxDbApiVersion.Auto,
                    ResponseTime = sw.Elapsed,
                    Details = $"Auto-detection failed: {ex.Message}"
                };
            }
        }

        public static InfluxDbApiVersion ClassifyFromVersionAndBuild(string version, string build)
        {
            if (!string.IsNullOrWhiteSpace(build))
            {
                if (build.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    build.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    build.IndexOf("Serverless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    build.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    build.IndexOf("InfluxDB 3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    build.IndexOf("IOx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return InfluxDbApiVersion.V3;
                }
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                var match = MajorVersionRegex.Match(version.Trim());
                if (match.Success && int.TryParse(match.Groups[1].Value, out var major))
                {
                    if (major == 1) return InfluxDbApiVersion.V1;
                    if (major == 2) return InfluxDbApiVersion.V2;
                    if (major >= 3) return InfluxDbApiVersion.V3;
                }
            }

            return InfluxDbApiVersion.Auto;
        }

        private static async Task<HttpResponseMessage> SafeSendAsync(
            HttpClient http,
            string relativePath,
            InfluxDbConnection connection,
            CancellationToken cancellationToken)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, relativePath))
                {
                    ApplyAuth(req, connection);
                    return await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyAuth(HttpRequestMessage request, InfluxDbConnection connection)
        {
            if (!string.IsNullOrWhiteSpace(connection.SecurityToken))
            {
                // InfluxDB 2.x expects "Token <token>"; InfluxDB 3.x accepts both "Bearer <token>" and "Token <token>".
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", connection.SecurityToken.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(connection.Username))
            {
                var raw = $"{connection.Username}:{connection.Password ?? string.Empty}";
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
            }
        }

        private static string GetHeaderValue(HttpResponseMessage response, string headerName)
        {
            if (response.Headers.TryGetValues(headerName, out var values))
                return values.FirstOrDefault();
            if (response.Content != null && response.Content.Headers.TryGetValues(headerName, out var contentValues))
                return contentValues.FirstOrDefault();
            return null;
        }

        private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
        {
            if (response?.Content == null) return null;
            try
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private static string TryExtractVersionFromJson(string body, out string build)
        {
            build = null;
            if (string.IsNullOrWhiteSpace(body)) return null;
            var trimmed = body.Trim();
            if (!trimmed.StartsWith("{")) return null;

            try
            {
                var obj = JObject.Parse(trimmed);
                build = obj.Value<string>("build");
                return obj.Value<string>("version");
            }
            catch
            {
                return null;
            }
        }
    }
}
