using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using VibeDeck.Host.Quotas;
using VibeDeck.Host.Sideboard;

namespace VibeDeck.Host.Dashboard
{
    public sealed class DashboardEventHub
    {
        private readonly ConcurrentDictionary<Guid, Channel<DashboardEvent>> subscribers = new ConcurrentDictionary<Guid, Channel<DashboardEvent>>();

        public bool HasSubscribers => !subscribers.IsEmpty;

        public Subscription Subscribe()
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateBounded<DashboardEvent>(new BoundedChannelOptions(16)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            subscribers[id] = channel;
            return new Subscription(this, id, channel.Reader);
        }

        public void Publish(string topic)
        {
            Publish(topic, null);
        }

        public void Publish(string topic, object data)
        {
            var dataJson = data == null
                ? null
                : JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            var notification = new DashboardEvent { Topic = topic, DataJson = dataJson };
            foreach (var subscriber in subscribers.Values)
            {
                subscriber.Writer.TryWrite(notification);
            }
        }

        private void Unsubscribe(Guid id)
        {
            if (subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
        }

        public sealed class Subscription : IDisposable
        {
            private readonly DashboardEventHub owner;
            private readonly Guid id;
            public ChannelReader<DashboardEvent> Reader { get; }

            internal Subscription(DashboardEventHub owner, Guid id, ChannelReader<DashboardEvent> reader)
            {
                this.owner = owner;
                this.id = id;
                Reader = reader;
            }

            public void Dispose() => owner.Unsubscribe(id);
        }
    }

    public sealed class DashboardEvent
    {
        public string Topic { get; set; }
        public string DataJson { get; set; }
    }

    public sealed class DashboardChangeMonitor : BackgroundService
    {
        private readonly DashboardEventHub hub;
        private readonly GlanceBoardProxy sideboard;
        private readonly AiQuotaService quotas;
        private string sideboardFingerprint;
        private string quotaFingerprint;
        private DateTimeOffset nextQuotaCheck = DateTimeOffset.MinValue;

        public DashboardChangeMonitor(DashboardEventHub hub, GlanceBoardProxy sideboard, AiQuotaService quotas)
        {
            this.hub = hub;
            this.sideboard = sideboard;
            this.quotas = quotas;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!hub.HasSubscribers)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                try
                {
                    var stats = await sideboard.GetStatsAsync(stoppingToken);
                    var nextSideboard = Fingerprint(stats.Json ?? stats.Error ?? "");
                    if (sideboardFingerprint != null && nextSideboard != sideboardFingerprint) hub.Publish("sideboard");
                    sideboardFingerprint = nextSideboard;

                    if (DateTimeOffset.UtcNow >= nextQuotaCheck)
                    {
                        var snapshot = await quotas.GetSnapshotAsync(stoppingToken);
                        var nextQuota = Fingerprint(System.Text.Json.JsonSerializer.Serialize(snapshot));
                        if (quotaFingerprint != null && nextQuota != quotaFingerprint) hub.Publish("quota");
                        quotaFingerprint = nextQuota;
                        nextQuotaCheck = DateTimeOffset.UtcNow.AddSeconds(15);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    hub.Publish("sync");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }

        private static string Fingerprint(string value)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
        }
    }
}
