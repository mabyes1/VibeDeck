using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace VibeDeck.Host.Updates
{
    /// <summary>HTTP seam so update flows can be tested without the network.</summary>
    public interface IUpdateHttpTransport
    {
        Task<HttpResponseMessage> SendAsync(Uri uri, HttpCompletionOption completionOption);
    }

    public sealed class GitHubUpdateTransport : IUpdateHttpTransport
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public async Task<HttpResponseMessage> SendAsync(Uri uri, HttpCompletionOption completionOption)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd($"VibeDeck/{ProductVersion.Current}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            return await Client.SendAsync(request, completionOption);
        }
    }

    /// <summary>Process-launch seam so tests never execute a real installer.</summary>
    public interface IInstallerLauncher
    {
        /// <returns>false when the OS refused to start the process.</returns>
        bool Launch(string installerPath, string arguments, string workingDirectory);
    }

    public sealed class ShellInstallerLauncher : IInstallerLauncher
    {
        public bool Launch(string installerPath, string arguments, string workingDirectory)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            return process != null;
        }
    }
}
