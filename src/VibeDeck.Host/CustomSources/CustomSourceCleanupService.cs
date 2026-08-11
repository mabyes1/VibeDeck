using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace VibeDeck.Host.CustomSources
{
    public sealed class CustomSourceCleanupService : BackgroundService
    {
        private readonly CustomSourceService sources;
        private readonly CustomSourceOptions options;

        public CustomSourceCleanupService(CustomSourceService sources, CustomSourceOptions options)
        {
            this.sources = sources;
            this.options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.CleanupIntervalSeconds), stoppingToken);
                    sources.CleanupExpired(DateTimeOffset.UtcNow);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // A transient cleanup failure must not stop the Host or the other dashboard services.
                }
            }
        }
    }
}
