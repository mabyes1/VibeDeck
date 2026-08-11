using System.Collections.Generic;

namespace VibeDeck.Host.Connect
{
    public sealed class ConnectInfo
    {
        public string HostName { get; set; }
        public string PrimaryAddress { get; set; }
        public string PrettyHostName { get; set; }
        public string PrettyHttpsUrl { get; set; }
        public string PrettyHttpUrl { get; set; }
        public string LocalNameHost { get; set; }
        public string LocalNameHttpUrl { get; set; }
        public string LocalNameHttpsUrl { get; set; }
        public string HttpsUrl { get; set; }
        public string HttpUrl { get; set; }
        public string PreferredUrl { get; set; }
        public bool HttpsAvailable { get; set; }
        public string RootCertificateUrl { get; set; }
        public string HostCertificateUrl { get; set; }
        public string HttpsSetupHint { get; set; }
        public string PublicUrl { get; set; }
        public string InstallationId { get; set; }
        public string PublicBaseDomain { get; set; }
        public bool UsesTrustedPublicUrl { get; set; }
        public bool PublicConnectorManaged { get; set; }
        public string PublicConnectorState { get; set; }
        public bool PublicConnectorRunning { get; set; }
        public bool PublicConnectorHealthy { get; set; }
        public string PublicConnectorError { get; set; }
        public bool IsHttpsRequest { get; set; }
        public bool WakeLockNeedsHttps { get; set; }
        public IReadOnlyList<string> Addresses { get; set; }
    }
}
