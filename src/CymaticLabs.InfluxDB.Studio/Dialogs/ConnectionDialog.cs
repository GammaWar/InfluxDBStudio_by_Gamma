using System;
using System.Threading.Tasks;
using System.Windows.Forms;

using CymaticLabs.InfluxDB.Data;

namespace CymaticLabs.InfluxDB.Studio.Dialogs
{
    /// <summary>
    /// Dialog used for creating or updating InfluxDB connection information.
    /// </summary>
    public partial class ConnectionDialog : Form
    {
        #region Fields

        #endregion Fields

        #region Properties

        /// <summary>
        /// Gets or sets the connection's unique ID.
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// Gets or sets the connection name in the dialog.
        /// </summary>
        public string ConnectionName
        {
            get { return name.Text; }
            set { name.Text = value; }
        }

        /// <summary>
        /// Gets or sets the host address in the dialog.
        /// </summary>
        public string Host
        {
            get { return host.Text; }
            set { host.Text = value; }
        }

        /// <summary>
        /// Gets or sets the host port number.
        /// </summary>
        public int Port
        {
            get { return (int)port.Value; }

            set
            {
                if (value == 0) value = 8086; // set default, don't allow port 0
                port.Value = value;
            }
        }

        /// <summary>
        /// Gets or sets the name of the single database to connect to for the connection.
        /// When specifying a single database name database discovery will not occur which
        /// can be helpful if you don't have sufficient privileges to run those commands.
        /// </summary>
        public string Database
        {
            get { return database.Text; }
            set { database.Text = value; }
        }

        /// <summary>
        /// Gets or sets the connection user name.
        /// </summary>
        public string Username
        {
            get { return username.Text; }
            set { username.Text = value; }
        }

        /// <summary>
        /// Gets or sets the connection password.
        /// </summary>
        public string Password
        {
            get { return password.Text; }
            set { password.Text = value; }
        }

        /// <summary>
        /// Gets or sets whether or not to use SSL for the connection.
        /// </summary>
        public bool UseSsl
        {
            get { return useSsl.Checked; }
            set { useSsl.Checked = value; }
        }

        /// <summary>
        /// Gets or sets the API token for the connection (InfluxDB 2.x/3.x).
        /// </summary>
        public string SecurityToken
        {
            get { return securityToken.Text; }
            set { securityToken.Text = value; }
        }

        /// <summary>
        /// Gets or sets the InfluxDB organization name or ID (InfluxDB 2.x/3.x).
        /// </summary>
        public string Organization
        {
            get { return organization.Text; }
            set { organization.Text = value; }
        }

        /// <summary>
        /// Gets or sets the target InfluxDB API version mode.
        /// </summary>
        public InfluxDbApiVersion ApiVersion
        {
            get
            {
                switch (apiVersionCombo.SelectedIndex)
                {
                    case 1: return InfluxDbApiVersion.V1;
                    case 2: return InfluxDbApiVersion.V2;
                    case 3: return InfluxDbApiVersion.V3;
                    default: return InfluxDbApiVersion.Auto;
                }
            }
            set
            {
                switch (value)
                {
                    case InfluxDbApiVersion.V1: apiVersionCombo.SelectedIndex = 1; break;
                    case InfluxDbApiVersion.V2: apiVersionCombo.SelectedIndex = 2; break;
                    case InfluxDbApiVersion.V3: apiVersionCombo.SelectedIndex = 3; break;
                    default: apiVersionCombo.SelectedIndex = 0; break;
                }
            }
        }

        #endregion Properties

        #region Constructors

        public ConnectionDialog()
        {
            InitializeComponent();
            apiVersionCombo.Items.AddRange(new object[]
            {
                "Auto (Detect)",
                "InfluxDB 1.x (InfluxQL)",
                "InfluxDB 2.x (Flux / Buckets)",
                "InfluxDB 3.x (SQL / InfluxQL)"
            });
            apiVersionCombo.SelectedIndex = 0;
            FormClosing += ConnectionDialog_FormClosing;
        }

        private void ConnectionDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;

            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                AppForm.DisplayError("Connection name cannot be blank.", "Invalid Connection");
                name.Focus();
                e.Cancel = true;
                return;
            }

            var normalizedHost = NormalizeHost(Host);
            if (string.IsNullOrWhiteSpace(normalizedHost))
            {
                AppForm.DisplayError("Host address cannot be blank.", "Invalid Connection");
                host.Focus();
                e.Cancel = true;
                return;
            }

            Host = normalizedHost;
        }

        private static string NormalizeHost(string rawHost)
        {
            if (string.IsNullOrWhiteSpace(rawHost)) return string.Empty;
            var trimmed = rawHost.Trim().TrimEnd('/');

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    return uri.Host;
                }
            }

            return trimmed;
        }

        #endregion Constructors

        #region Event Handlers

        private async Task RunProbeWithHttpFallbackAsync(Func<InfluxDbClient, Task<string>> action, string successCaption)
        {
            InfluxDbClient client = null;
            try
            {
                client = await InfluxDbClientFactory.CreateAsync(CreateConnection());
                var message = await action(client);
                MessageBox.Show(message, successCaption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (InfluxDbVersionAmbiguousException ambEx)
            {
                MessageBox.Show(
                    ambEx.Message + "\n\nPlease select the API Version explicitly (1.x, 2.x, or 3.x) in the dropdown.",
                    "Version Auto-Detection Ambiguous",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                apiVersionCombo.Focus();
            }
            catch (Exception ex)
            {
                if (UseSsl)
                {
                    InfluxDbClient httpClient = null;
                    try
                    {
                        var httpConn = CreateConnection();
                        httpConn.UseSsl = false;
                        httpClient = await InfluxDbClientFactory.CreateAsync(httpConn);
                        var message = await action(httpClient);

                        var result = MessageBox.Show(
                            "HTTPS connection failed, but connecting via HTTP succeeded!\n\nWould you like to switch this connection to HTTP?",
                            "HTTPS failed - Fallback to HTTP available",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            useSsl.Checked = false;
                            MessageBox.Show(message, successCaption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                    catch
                    {
                        // Fallback also failed; display original exception below
                    }
                    finally
                    {
                        (httpClient as IDisposable)?.Dispose();
                    }
                }

                AppForm.DisplayException(ex, "Failure");
            }
            finally
            {
                (client as IDisposable)?.Dispose();
            }
        }

        // Handles test connection button click
        private async void testButton_Click(object sender, EventArgs e)
        {
            await RunProbeWithHttpFallbackAsync(async client =>
            {
                if (!string.IsNullOrWhiteSpace(client.Connection.Database))
                {
                    await client.GetMeasurementNamesAsync(client.Connection.Database);
                }
                else
                {
                    await client.GetDatabaseNamesAsync();
                }

                var versionInfo = string.IsNullOrWhiteSpace(client.Connection.DetectedServerVersion)
                    ? client.ApiVersion.ToString()
                    : $"{client.ApiVersion} ({client.Connection.DetectedServerVersion})";
                return $"Connection Successful!\nAPI Mode: {versionInfo}";
            }, "Success");
        }

        // Handles ping connection button click
        private async void pingButton_Click(object sender, EventArgs e)
        {
            await RunProbeWithHttpFallbackAsync(async client =>
            {
                var response = await client.PingAsync();
                if (!response.Success) throw new Exception("There was an error connecting to the server.");
                return $"Response Time: {response.ResponseTime.TotalMilliseconds:0} ms\nInfluxDB Version: {response.Version}\nAPI Mode: {client.ApiVersion}";
            }, "Pong");
        }

        #endregion Event Handlers

        #region Methods

        /// <summary>
        /// Resets the dialog's values.
        /// </summary>
        public void ResetValues()
        {
            ConnectionId = null;
            ConnectionName = "New Connection";
            Host = "localhost";
            Port = 8086;
            ApiVersion = InfluxDbApiVersion.Auto;
            Organization = null;
            Database = null;
            Username = null;
            Password = null;
            SecurityToken = null;
            UseSsl = false;
        }

        /// <summary>
        /// Binds the dialog to an InfluxDB connection instance.
        /// </summary>
        /// <param name="connection">The connection to bind to.</param>
        public void BindToConnection(InfluxDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException("connection");

            // Pass the connection values to the dialog
            ConnectionId = connection.Id;
            ConnectionName = connection.Name;
            Host = connection.Host;
            Port = connection.Port;
            ApiVersion = connection.ApiVersion;
            Organization = connection.Organization;
            Database = connection.Database;
            Username = connection.Username;
            Password = connection.Password;
            SecurityToken = connection.SecurityToken;
            UseSsl = connection.UseSsl;
        }

        /// <summary>
        /// Creates an InfluxDB connection using the dialog's current values.
        /// </summary>
        /// <returns>An InfluxDB connection.</returns>
        public InfluxDbConnection CreateConnection()
        {
            return new InfluxDbConnection(Guid.NewGuid().ToString(), ConnectionName?.Trim(), NormalizeHost(Host), 
                (ushort)Port, Username, Password, UseSsl, Database, SecurityToken, ApiVersion, Organization?.Trim());
        }

        /// <summary>
        /// Updates an InfluxDB connection instance using values from the dialog.
        /// </summary>
        /// <param name="connection">The connection to update.</param>
        public void UpdateConnectionFromDialog(InfluxDbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException("connection");

            connection.Id = ConnectionId;
            connection.Name = ConnectionName?.Trim();
            connection.Host = NormalizeHost(Host);
            connection.Port = (ushort)Port;
            connection.ApiVersion = ApiVersion;
            connection.DetectedApiVersion = null;
            connection.DetectedServerVersion = null;
            connection.Organization = Organization?.Trim();
            connection.Database = Database;
            connection.Username = Username;
            connection.Password = Password;
            connection.SecurityToken = SecurityToken;
            connection.UseSsl = UseSsl;
        }

        #endregion Methods
    }
}
