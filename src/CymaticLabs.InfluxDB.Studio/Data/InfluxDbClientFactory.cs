using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CymaticLabs.InfluxDB.Data
{
    /// <summary>
    /// Exception thrown when auto-detection cannot determine whether a server is InfluxDB 1.x, 2.x, or 3.x.
    /// </summary>
    public class InfluxDbVersionAmbiguousException : Exception
    {
        public InfluxDbDetectionResult DetectionResult { get; }

        public InfluxDbVersionAmbiguousException(InfluxDbDetectionResult detectionResult)
            : base(detectionResult?.Details ?? "Could not auto-detect InfluxDB API version (1.x / 2.x / 3.x).")
        {
            DetectionResult = detectionResult;
        }
    }

    /// <summary>
    /// Factory class for creating <see cref="InfluxDbClient">InfluxDB clients</see>.
    /// </summary>
    public static class InfluxDbClientFactory
    {
        #region Methods

        /// <summary>
        /// Creates a new <see cref="InfluxDbClient">InfluxDB client</see> for the configured or previously detected API version.
        /// </summary>
        /// <param name="connection">The connection information to use when creating the client.</param>
        /// <returns>An InfluxDB client that can be used to communicate with the InfluxDB server API.</returns>
        public static InfluxDbClient Create(InfluxDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            switch (connection.EffectiveApiVersion)
            {
                case InfluxDbApiVersion.V2:
                    return new InfluxDbV2Client(connection);
                case InfluxDbApiVersion.V3:
                    return new InfluxDbV3Client(connection);
                default:
                    return new InfluxDataNetClient(connection);
            }
        }

        /// <summary>
        /// Auto-detects the InfluxDB API version when <see cref="InfluxDbConnection.ApiVersion"/> is <see cref="InfluxDbApiVersion.Auto"/>,
        /// then creates the corresponding concrete client.
        /// </summary>
        public static async Task<InfluxDbClient> CreateAsync(
            InfluxDbConnection connection,
            HttpMessageHandler detectorHandler = null,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            if (connection.ApiVersion == InfluxDbApiVersion.Auto)
            {
                var result = await InfluxDbVersionDetector.DetectAsync(connection, detectorHandler, cancellationToken).ConfigureAwait(false);
                if (!result.Detected || result.ApiVersion == InfluxDbApiVersion.Auto)
                {
                    throw new InfluxDbVersionAmbiguousException(result);
                }

                connection.DetectedApiVersion = result.ApiVersion;
                connection.DetectedServerVersion = result.ServerVersion;
            }

            return Create(connection);
        }

        #endregion Methods
    }
}
