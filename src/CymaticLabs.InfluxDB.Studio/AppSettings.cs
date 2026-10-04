using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CymaticLabs.InfluxDB.Data;

namespace CymaticLabs.InfluxDB.Studio
{
    /// <summary>
    /// Intermediate object used to import or export application settings.
    /// </summary>
    public class AppSettings
    {
        #region Fields

        /// <summary>
        /// Time format string for 12 hour time.
        /// </summary>
        public const string TimeFormat12Hour = "hh:mm:ss tt";

        /// <summary>
        /// Time format string for 24 hour time.
        /// </summary>
        public const string TimeFormat24Hour = "HH:mm:ss";

        /// <summary>
        /// Date format string for day-first dates.
        /// </summary>
        public const string DateFormatDay = "d/MM/yyyy";

        /// <summary>
        /// Date format string for month-first dates.
        /// </summary>
        public const string DateFormatMonth = "M/dd/yyyy";

        // Whether or not to allow untrusted SSL certificates
        bool allowUntrustedSsl = false;

        // Internal app time format setting
        string timeFormat;

        // Internal app date format setting
        string dateFormat;

        /// <summary>
        /// Comma CSV delimiter.
        /// </summary>
        public const string CsvDelimiterComma = ",";

        /// <summary>
        /// Semicolon CSV delimiter.
        /// </summary>
        public const string CsvDelimiterSemicolon = ";";

        // Internal app CSV delimiter setting
        string csvDelimiter;

        private static string cachedSettingsFilePath = null;

        #endregion Fields

        #region Properties

        /// <summary>
        /// Gets the path to the portable or local settings.json file.
        /// </summary>
        [JsonIgnore]
        public static string SettingsFilePath
        {
            get
            {
                if (cachedSettingsFilePath != null) return cachedSettingsFilePath;

                try
                {
                    // Check portable directory (next to exe)
                    var appDir = AppContext.BaseDirectory;
                    var portableFile = Path.Combine(appDir, "settings.json");

                    // Test write access to appDir
                    var testFile = Path.Combine(appDir, ".write_test_" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);

                    cachedSettingsFilePath = portableFile;
                    return cachedSettingsFilePath;
                }
                catch
                {
                    // Fallback to LocalAppData
                    var localAppData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CymaticLabs", "InfluxDBStudio");
                    Directory.CreateDirectory(localAppData);
                    cachedSettingsFilePath = Path.Combine(localAppData, "settings.json");
                    return cachedSettingsFilePath;
                }
            }
        }

        /// <summary>
        /// Gets or sets the application version the settings are for/from.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets the current time format setting.
        /// </summary>
        public string TimeFormat
        {
            get { return timeFormat; }

            set
            {
                if (timeFormat != value)
                {
                    timeFormat = value;
                    SaveAll();
                }
            }
        }

        /// <summary>
        /// Gets the current date format setting.
        /// </summary>
        public string DateFormat
        {
            get { return dateFormat; }

            set
            {
                if (dateFormat != value)
                {
                    dateFormat = value;
                    SaveAll();
                }
            }
        }

        /// <summary>
        /// Gets or sets the CSV delimiter setting (e.g. "," or ";").
        /// </summary>
        public string CsvDelimiter
        {
            get { return csvDelimiter; }

            set
            {
                if (csvDelimiter != value)
                {
                    csvDelimiter = value;
                    SaveAll();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether or not the application should allow untrusted SSL certificates
        /// when communicating to InfluxDB servers.
        /// </summary>
        public bool AllowUntrustedSsl
        {
            get { return allowUntrustedSsl; }

            set
            {
                if (allowUntrustedSsl != value)
                {
                    allowUntrustedSsl = value;
                    SaveAll();
                }
            }
        }

        /// <summary>
        /// Gets or sets the available InfluxDB connections.
        /// </summary>
        public List<InfluxDbConnection> Connections { get; set; }

        #endregion Properties

        #region Constructors

        public AppSettings()
        {
            // Initialize default settings
            timeFormat = TimeFormat12Hour;
            dateFormat = DateFormatMonth;
            csvDelimiter = CsvDelimiterComma;
            allowUntrustedSsl = false;
            Connections = new List<InfluxDbConnection>();

            // Set the version string
            Version = GetType().Assembly.GetName().Version.ToString();

            // Try to upgrade legacy properties settings if needed
            try { Properties.Settings.Default.Upgrade(); } catch { }
        }

        #endregion Constructors

        #region Methods

        /// <summary>
        /// Loads settings from disk.
        /// </summary>
        public void LoadAll()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    LoadFromJson(json);
                    return;
                }

                // If settings.json does not exist yet, migrate from legacy sources
                MigrateLegacySettings();
            }
            catch (Exception ex)
            {
                AppForm.DisplayException(ex);
            }
        }

        /// <summary>
        /// Saves all current settings to disk.
        /// </summary>
        public void SaveAll()
        {
            try
            {
                // Encrypt sensitive fields before saving to disk
                var encryptedList = new List<InfluxDbConnection>(Connections != null ? Connections.Count : 0);
                if (Connections != null)
                {
                    foreach (var c in Connections)
                    {
                        var copy = new InfluxDbConnection(c.Id, c.Name, c.Host, c.Port, c.Username,
                            ProtectString(c.Password), c.UseSsl, c.Database, ProtectString(c.SecurityToken));
                        encryptedList.Add(copy);
                    }
                }

                var settingsDto = new
                {
                    Version = Version,
                    TimeFormat = TimeFormat,
                    DateFormat = DateFormat,
                    CsvDelimiter = CsvDelimiter,
                    AllowUntrustedSsl = AllowUntrustedSsl,
                    Connections = encryptedList
                };

                var json = JsonConvert.SerializeObject(settingsDto, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);

                // Also update legacy Settings.Default as fallback
                try
                {
                    Properties.Settings.Default.TimeFormat = TimeFormat;
                    Properties.Settings.Default.DateFormat = DateFormat;
                    Properties.Settings.Default.AllowUntrustedSsl = AllowUntrustedSsl;
                    Properties.Settings.Default.ConnectionsJson = JsonConvert.SerializeObject(encryptedList);
                    Properties.Settings.Default.Save();
                }
                catch { }
            }
            catch (Exception ex)
            {
                AppForm.DisplayException(ex);
            }
        }

        /// <summary>
        /// Loads all connections data from disk.
        /// </summary>
        public void LoadConnections()
        {
            LoadAll();
        }

        /// <summary>
        /// Saves current connection data to disk.
        /// </summary>
        public void SaveConnections()
        {
            SaveAll();
        }

        private void LoadFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var jObj = JObject.Parse(json);
                if (jObj["TimeFormat"] != null) timeFormat = (string)jObj["TimeFormat"];
                if (jObj["DateFormat"] != null) dateFormat = (string)jObj["DateFormat"];
                if (jObj["CsvDelimiter"] != null) csvDelimiter = (string)jObj["CsvDelimiter"];
                if (jObj["AllowUntrustedSsl"] != null) allowUntrustedSsl = (bool)jObj["AllowUntrustedSsl"];

                Connections = new List<InfluxDbConnection>();
                if (jObj["Connections"] is JArray array)
                {
                    foreach (var item in array)
                    {
                        try
                        {
                            var connection = item.ToObject<InfluxDbConnection>();
                            if (connection != null)
                            {
                                connection.Password = UnprotectString(connection.Password);
                                connection.SecurityToken = UnprotectString(connection.SecurityToken);
                                Connections.Add(connection);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppForm.DisplayException(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppForm.DisplayException(ex);
            }
        }

        private void MigrateLegacySettings()
        {
            Connections = new List<InfluxDbConnection>();

            // 1. Check for export/import json files in app directory or alteVersion
            var appDir = AppContext.BaseDirectory;
            var searchDirs = new List<string> { appDir, Path.Combine(appDir, "alteVersion") };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        if (Path.GetFileName(file).Equals("settings.json", StringComparison.OrdinalIgnoreCase)) continue;

                        try
                        {
                            var content = File.ReadAllText(file);
                            var jObj = JObject.Parse(content);
                            if (jObj["Connections"] is JArray array)
                            {
                                foreach (var item in array)
                                {
                                    var conn = item.ToObject<InfluxDbConnection>();
                                    if (conn != null) AddConnectionIfUnique(conn);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 2. Check for legacy user.config files in %LOCALAPPDATA%\CymaticLabs
            var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CymaticLabs");
            if (Directory.Exists(localAppData))
            {
                try
                {
                    foreach (var configFile in Directory.GetFiles(localAppData, "user.config", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var doc = XDocument.Load(configFile);
                            foreach (var settingNode in doc.Descendants("setting"))
                            {
                                if ((string)settingNode.Attribute("name") == "ConnectionsJson")
                                {
                                    var val = settingNode.Element("value")?.Value;
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        var loaded = JsonConvert.DeserializeObject(val);
                                        if (loaded is JArray array)
                                        {
                                            foreach (var item in array)
                                            {
                                                var conn = item.ToObject<InfluxDbConnection>();
                                                if (conn != null)
                                                {
                                                    conn.Password = UnprotectString(conn.Password);
                                                    conn.SecurityToken = UnprotectString(conn.SecurityToken);
                                                    AddConnectionIfUnique(conn);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 3. Check legacy Properties.Settings.Default
            try
            {
                var legacyJson = Properties.Settings.Default.ConnectionsJson;
                if (!string.IsNullOrWhiteSpace(legacyJson))
                {
                    var loaded = JsonConvert.DeserializeObject(legacyJson);
                    if (loaded is JArray array)
                    {
                        foreach (var item in array)
                        {
                            var conn = item.ToObject<InfluxDbConnection>();
                            if (conn != null)
                            {
                                conn.Password = UnprotectString(conn.Password);
                                conn.SecurityToken = UnprotectString(conn.SecurityToken);
                                AddConnectionIfUnique(conn);
                            }
                        }
                    }
                }
            }
            catch { }

            // Save migrated settings to settings.json immediately
            SaveAll();
        }

        private void AddConnectionIfUnique(InfluxDbConnection conn)
        {
            if (conn == null || string.IsNullOrWhiteSpace(conn.Name)) return;
            foreach (var existing in Connections)
            {
                if (string.Equals(existing.Name, conn.Name, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            Connections.Add(conn);
        }

        private const string EncryptedPrefix = "enc:";

        private static string ProtectString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            try
            {
                byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
                byte[] cipherBytes = System.Security.Cryptography.ProtectedData.Protect(
                    plainBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return EncryptedPrefix + Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return plainText;
            }
        }

        private static string UnprotectString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (!cipherText.StartsWith(EncryptedPrefix)) return cipherText; // legacy plaintext format

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText.Substring(EncryptedPrefix.Length));
                byte[] plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    cipherBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return cipherText;
            }
        }

        #endregion Methods
    }
}
