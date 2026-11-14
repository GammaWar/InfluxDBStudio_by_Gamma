using Newtonsoft.Json;

namespace CymaticLabs.InfluxDB.Data
{
    /// <summary>
    /// Supported InfluxDB API / server generations.
    /// </summary>
    public enum InfluxDbApiVersion
    {
        /// <summary>
        /// Automatically detect server version before connecting.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// InfluxDB 1.x (InfluxQL / /query /write).
        /// </summary>
        V1 = 1,

        /// <summary>
        /// InfluxDB 2.x (Flux / Buckets / /api/v2).
        /// </summary>
        V2 = 2,

        /// <summary>
        /// InfluxDB 3.x (Core, Enterprise, Serverless / SQL + InfluxQL / Arrow Flight + /api/v3).
        /// </summary>
        V3 = 3
    }

    /// <summary>
    /// Represents the connection information required to connect to a specific InfluxDB server.
    /// </summary>
    public class InfluxDbConnection
    {
        #region Fields

        #endregion Fields

        #region Properties

        /// <summary>
        /// Sets the unique ID of the connction instance. Ideally this value
        /// does not changed once assigned.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the human-readable name of the connection.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the host URL for the InfluxDB server to connect to.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Gets or sets the port number to use when connecting to the InfluxDB server.
        /// </summary>
        public ushort Port { get; set; }

        /// <summary>
        /// The name of the preferred database to use (optional).
        /// </summary>
        public string Database { get; set; }

        /// <summary>
        /// Gets or sets the username to use when connecting to the InfluxDB server.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Gets or sets the password to use when connecting to the InfluxDB server.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets whether or not to use SSL to communicate with the InfluxDB server.
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>
        /// Gets or sets the API token to use when connecting to an InfluxDB 2.x/3.x server or token-based instance.
        /// </summary>
        public string SecurityToken { get; set; }

        /// <summary>
        /// Gets or sets the InfluxDB organization name or ID (required for InfluxDB 2.x, optional for 3.x).
        /// </summary>
        public string Organization { get; set; }

        /// <summary>
        /// Gets or sets the configured API version mode (Auto, V1, V2, or V3).
        /// </summary>
        public InfluxDbApiVersion ApiVersion { get; set; } = InfluxDbApiVersion.Auto;

        /// <summary>
        /// Gets or sets the runtime auto-detected API version when <see cref="ApiVersion"/> is <see cref="InfluxDbApiVersion.Auto"/>.
        /// </summary>
        [JsonIgnore]
        public InfluxDbApiVersion? DetectedApiVersion { get; set; }

        /// <summary>
        /// Gets or sets the server version string observed during ping/detection.
        /// </summary>
        [JsonIgnore]
        public string DetectedServerVersion { get; set; }

        /// <summary>
        /// Gets the effective API version to instantiate for this connection.
        /// </summary>
        [JsonIgnore]
        public InfluxDbApiVersion EffectiveApiVersion
        {
            get
            {
                if (ApiVersion != InfluxDbApiVersion.Auto) return ApiVersion;
                return DetectedApiVersion ?? InfluxDbApiVersion.V1;
            }
        }

        /// <summary>
        /// Gets the combined/unified host connection string including protocol and port number for
        /// HTTP API access to the InfluxDB server.
        /// </summary>
        [JsonIgnore]
        public string HttpConnectionString
        {
            get
            {
                return string.Format("{0}://{1}:{2}", UseSsl ? "https" : "http", Host, Port);
            }
        }

        #endregion Properties

        #region Constructors

        /// <summary>
        /// Default constructor for serialization.
        /// </summary>
        public InfluxDbConnection()
        {
            Id = System.Guid.NewGuid().ToString();
            Port = 8086;
            UseSsl = false;
            ApiVersion = InfluxDbApiVersion.Auto;
        }

        /// <summary>
        /// Creates an InfluxDB connection with the provided configuration details.
        /// </summary>
        /// <param name="id">A unique ID to assign the connection.</param>
        /// <param name="name">The human-readable name for the connection.</param>
        /// <param name="host">The host URL for the InfluxDB server to connect to.</param>
        /// <param name="port">The port number to use when connecting to the InfluxDB server.</param>
        /// <param name="username">The username to use when connecting to the InfluxDB server.</param>
        /// <param name="password">The password to use when connecting to the InfluxDB server.</param>
        /// <param name="useSsl">Whether or not to use SSL to communicate with the InfluxDB server.</param>
        /// <param name="database">The name of the preferred database to use (optional).</param>
        /// <param name="securityToken">The API token to use (optional).</param>
        /// <param name="apiVersion">The target InfluxDB API version or Auto for detection.</param>
        /// <param name="organization">The InfluxDB organization name or ID (optional).</param>
        public InfluxDbConnection(string id, string name, string host, ushort port, 
            string username, string password, bool useSsl = true, string database = null, string securityToken = null,
            InfluxDbApiVersion apiVersion = InfluxDbApiVersion.Auto, string organization = null)
        {
            Id = id;
            Name = name;
            Host = host;
            Port = port;
            Username = username;
            Password = password;
            UseSsl = useSsl;
            Database = database;
            SecurityToken = securityToken;
            ApiVersion = apiVersion;
            Organization = organization;
        }

        #endregion Constructors

        #region Methods

        #endregion Methods
    }
}
