using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CymaticLabs.InfluxDB.Data
{
    /// <summary>
    /// Utility class used to configure SSL/TLS certificates and security protocols.
    /// </summary>
    public static class SslIgnoreValidator
    {
        // Whether or not the SSL validator override has been enabled or not
        private static bool enabled = false;

        // Whether or not to allow untrusted SSL/TLS certificates.
        private static bool allowUntrusted = false;

        static SslIgnoreValidator()
        {
            // Ensure modern TLS protocols are enabled
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            }
            catch
            {
                // Fallback if Tls13 is not supported by underlying OS
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
        }

        /// <summary>
        /// Gets or sets whether or not to allow untrusted SSL/TLS certificates.
        /// </summary>
        public static bool AllowUntrusted
        {
            get { return allowUntrusted; }
            
            set
            {
                allowUntrusted = value;

                // Configure untrusted allowances as needed
                if (allowUntrusted)
                {
                    OverrideValidation();
                    enabled = true;
                }
                else if (enabled)
                {
                    ResetValidation();
                    enabled = false;
                }
            }
        }

        /// <summary>
        /// Creates a configured HttpClientHandler with certificate validation based on current settings.
        /// </summary>
        public static HttpClientHandler CreateHttpClientHandler()
        {
            var handler = new HttpClientHandler();

            if (allowUntrusted)
            {
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            }

            return handler;
        }

        // Custom validation method for legacy ServicePointManager
        private static bool OnValidateCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (allowUntrusted) return true;
            return sslPolicyErrors == SslPolicyErrors.None;
        }

        // Overrides SSL/TLS certificate validation.
        static void OverrideValidation()
        {
            ServicePointManager.ServerCertificateValidationCallback = OnValidateCertificate;
            ServicePointManager.Expect100Continue = true;
        }

        // Resets SSL/TLS certificate validation back to default.
        static void ResetValidation()
        {
            ServicePointManager.ServerCertificateValidationCallback = null;
        }
    }
}
