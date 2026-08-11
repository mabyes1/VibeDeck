using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using VibeDeck.Host.Dashboard;

using static VibeDeck.Host.CustomSources.CustomSourceValidation;

namespace VibeDeck.Host.CustomSources
{
    public sealed class CustomSourceService
    {
        private readonly CustomSourceStore store;
        private readonly CustomSourceOptions options;
        private readonly DashboardEventHub eventHub;
        private readonly CustomSourceWriteAccess writeAccess;

        public CustomSourceService(
            CustomSourceStore store,
            CustomSourceOptions options,
            DashboardEventHub eventHub)
        {
            this.store = store;
            this.options = options;
            this.options.Normalize();
            this.eventHub = eventHub;
            writeAccess = new CustomSourceWriteAccess(store, this.options);
        }

        public IReadOnlyList<CustomSourceManagementView> GetSources(DateTimeOffset now)
        {
            return store.GetSources()
                .Where(source => !CustomSourceWriteAccess.IsSystemSource(source.SourceKey))
                .Select(source =>
                {
                    var itemCount = store.GetItemCount(source, now);
                    var view = CustomSourceStore.ToManagementView(source, now);
                    view.ItemCount = itemCount;
                    return view;
                })
                .ToList();
        }

        public CustomCardsResponse GetCardSnapshot(DateTimeOffset now)
        {
            return new CustomCardsResponse
            {
                GeneratedAt = CustomSourceDateTime.ToText(now),
                Cards = store.GetCardSnapshots(now)
            };
        }

        public CustomCardSettingsResponse GetCardSettings(string cardId)
        {
            var source = store.GetSourceByCardId(cardId);
            if (source == null) Problem(404, "card_not_found", "The custom card was not found.");
            return BuildCardSettings(source, store.GetCardSettings(source.Card.Id));
        }

        public CustomCardSettingsResponse UpdateCardSettings(
            string cardId,
            CustomCardSettingsUpdateRequest request,
            DateTimeOffset now)
        {
            if (request == null) Problem(400, "invalid_request", "A JSON request body is required.");
            var source = store.GetSourceByCardId(cardId);
            if (source == null) Problem(404, "card_not_found", "The custom card was not found.");
            var current = store.GetCardSettings(source.Card.Id);
            var maxItems = request.MaxItems ?? source.Card.MaxItems;
            var streamEnabled = request.StreamEnabled ?? current.StreamEnabled;
            var streamCharDelayMs = request.StreamCharDelayMs ?? current.StreamCharDelayMs;

            if (maxItems < CustomCardSettingsDefaults.MinVisibleItems || maxItems > CustomCardSettingsDefaults.MaxVisibleItems)
            {
                Problem(400, "invalid_card_settings", $"maxItems must be between {CustomCardSettingsDefaults.MinVisibleItems} and {CustomCardSettingsDefaults.MaxVisibleItems}.", "maxItems", "range");
            }
            if (streamCharDelayMs < CustomCardSettingsDefaults.MinStreamCharDelayMs || streamCharDelayMs > CustomCardSettingsDefaults.MaxStreamCharDelayMs)
            {
                Problem(400, "invalid_card_settings", $"streamCharDelayMs must be between {CustomCardSettingsDefaults.MinStreamCharDelayMs} and {CustomCardSettingsDefaults.MaxStreamCharDelayMs}.", "streamCharDelayMs", "range");
            }

            var updated = store.UpdateCardSettings(source.Card.Id, maxItems, streamEnabled, streamCharDelayMs, now);
            var settings = BuildCardSettings(updated, store.GetCardSettings(updated.Card.Id));
            eventHub.Publish("custom-card", new
            {
                cardId = updated.Card.Id,
                sourceKey = updated.SourceKey,
                revision = updated.Card.Revision,
                reason = "config"
            });
            return settings;
        }

        public CustomClearResult ClearCard(string cardId, DateTimeOffset now)
        {
            var result = store.ClearCard(cardId, now);
            if (result.Cleared)
            {
                eventHub.Publish("custom-card", new
                {
                    cardId = result.CardId,
                    sourceKey = result.SourceKey,
                    revision = result.Revision,
                    reason = "cleared"
                });
            }
            return result;
        }

        public CustomSourceRecord EnsureSystemSource(DateTimeOffset now)
        {
            var source = store.GetSource(CustomSourceKeys.WindowsNotifications);
            if (source != null)
            {
                if (!CustomSourceCardTypes.IsFeed(source.Card.Type))
                {
                    throw new CustomSourceStoreUnavailableException(
                        $"Reserved source '{CustomSourceKeys.WindowsNotifications}' has an incompatible card type.");
                }
                return source;
            }

            var systemSource = new CustomSourceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceKey = CustomSourceKeys.WindowsNotifications,
                DisplayName = "Windows 通知",
                // Per-install random hash of a secret that is never issued to any client.
                // No supplied token can ever match it, so even if the write-path guard were
                // removed the source could not be driven by a remote caller.
                TokenHash = CustomSourceWriteAccess.GenerateSystemSourceTokenHash(),
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            systemSource.Card = new CustomCardRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceId = systemSource.Id,
                CardKey = "default",
                Type = CustomSourceCardTypes.MessageFeed,
                Title = "Windows 通知",
                Position = 0,
                StaleAfterSeconds = 0,
                DefaultTtlSeconds = 0,
                MaxItems = 30,
                Revision = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            return store.EnsureSystemSource(systemSource, systemSource.Card);
        }

        public CustomIngestResult IngestSystem(string sourceKey, JsonElement payload, DateTimeOffset now)
        {
            if (!CustomSourceWriteAccess.IsSystemSource(sourceKey))
            {
                Problem(404, "source_not_found", "The system source was not found.");
            }

            var source = store.GetSource(CustomSourceKeys.WindowsNotifications);
            if (source == null) Problem(503, "custom_sources_unavailable", "The Windows notification source is unavailable.");
            var normalized = CustomSourcePayloadNormalizer.Normalize(source, payload, now);
            var result = store.Ingest(source, normalized);
            eventHub.Publish("custom-card", new
            {
                cardId = result.CardId,
                sourceKey = result.SourceKey,
                revision = result.Revision,
                reason = "updated"
            });
            return result;
        }

        public CustomSourceCreateResponse Create(
            CustomSourceCreateRequest request,
            string endpointUrl,
            string localEndpointUrl,
            DateTimeOffset now)
        {
            if (request == null) Problem(400, "invalid_request", "A JSON request body is required.");
            var sourceKey = CustomSourceWriteAccess.NormalizeSourceKey(request.SourceKey, true);
            CustomSourceWriteAccess.EnsureNotSystemSource(sourceKey);
            var displayName = RequiredText(request.DisplayName, "displayName", 80);
            if (request.Card == null) Problem(400, "invalid_request", "card is required.");

            var type = NormalizeCardType(request.Card.Type);
            var title = string.IsNullOrWhiteSpace(request.Card.Title) ? displayName : RequiredText(request.Card.Title, "card.title", 80);
            var position = request.Card.Position ?? NextPosition();
            ValidatePosition(position);
            var staleAfter = request.Card.StaleAfterSeconds ?? 300;
            var defaultTtl = request.Card.DefaultTtlSeconds ?? 0;
            var maxItems = request.Card.MaxItems ?? 20;
            ValidateDurations(staleAfter, defaultTtl);
            ValidateMaxItems(maxItems);

            var token = CustomSourceWriteAccess.GenerateToken();
            var source = new CustomSourceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceKey = sourceKey,
                DisplayName = displayName,
                TokenHash = CustomSourceWriteAccess.HashToken(token),
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            var card = new CustomCardRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceId = source.Id,
                CardKey = "default",
                Type = type,
                Title = title,
                Position = position,
                StaleAfterSeconds = staleAfter,
                DefaultTtlSeconds = defaultTtl,
                MaxItems = maxItems,
                Revision = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            source.Card = card;

            var response = store.CreateSource(source, card, options.MaxSources, endpointUrl, localEndpointUrl, token);
            response.Source = CustomSourceStore.ToManagementView(source, now);
            response.Source.Card.Revision = 0;
            return response;
        }

        public CustomSourceManagementView Update(string sourceKey, CustomSourceUpdateRequest request, DateTimeOffset now)
        {
            var normalizedKey = CustomSourceWriteAccess.NormalizeSourceKey(sourceKey, false);
            CustomSourceWriteAccess.EnsureNotSystemSource(normalizedKey);
            var source = store.GetSource(normalizedKey);
            if (source == null) Problem(404, "source_not_found", "The custom source was not found.");
            if (request == null) Problem(400, "invalid_request", "A JSON request body is required.");

            RejectImmutableUpdate(request.SourceKey, "sourceKey");
            RejectImmutableUpdate(request.CardId, "cardId");
            RejectImmutableUpdate(request.CardKey, "cardKey");
            RejectImmutableUpdate(request.Type, "type");
            if (request.Card != null)
            {
                RejectImmutableUpdate(request.Card.Type, "card.type");
                RejectImmutableUpdate(request.Card.CardId, "card.cardId");
                RejectImmutableUpdate(request.Card.CardKey, "card.cardKey");
            }

            var displayName = request.DisplayName == null
                ? source.DisplayName
                : RequiredText(request.DisplayName, "displayName", 80);
            var enabled = request.Enabled ?? source.Enabled;
            var card = source.Card;
            var title = card.Title;
            var position = card.Position;
            var staleAfter = card.StaleAfterSeconds;
            var defaultTtl = card.DefaultTtlSeconds;
            var maxItems = card.MaxItems;

            if (request.Card != null)
            {
                if (request.Card.Title != null) title = RequiredText(request.Card.Title, "card.title", 80);
                if (request.Card.Position.HasValue) position = request.Card.Position.Value;
                if (request.Card.StaleAfterSeconds.HasValue) staleAfter = request.Card.StaleAfterSeconds.Value;
                if (request.Card.DefaultTtlSeconds.HasValue) defaultTtl = request.Card.DefaultTtlSeconds.Value;
                if (request.Card.MaxItems.HasValue) maxItems = request.Card.MaxItems.Value;
            }

            ValidatePosition(position);
            ValidateDurations(staleAfter, defaultTtl);
            ValidateMaxItems(maxItems);
            var cardChanged = !string.Equals(card.Title, title, StringComparison.Ordinal) ||
                card.Position != position ||
                card.StaleAfterSeconds != staleAfter ||
                card.DefaultTtlSeconds != defaultTtl ||
                card.MaxItems != maxItems;
            var visibilityChanged = source.Enabled != enabled;

            var updated = store.UpdateSource(
                normalizedKey,
                displayName,
                enabled,
                title,
                position,
                staleAfter,
                defaultTtl,
                maxItems,
                cardChanged,
                now);
            var view = CustomSourceStore.ToManagementView(updated, now);
            view.ItemCount = store.GetItemCount(updated, now);
            if (cardChanged || visibilityChanged)
            {
                eventHub.Publish("custom-card", new
                {
                    cardId = updated.Card.Id,
                    sourceKey = updated.SourceKey,
                    revision = updated.Card.Revision,
                    reason = "config"
                });
            }
            return view;
        }

        public CustomSourceCreateResponse RotateToken(
            string sourceKey,
            string endpointUrl,
            string localEndpointUrl,
            DateTimeOffset now)
        {
            var normalizedKey = CustomSourceWriteAccess.NormalizeSourceKey(sourceKey, false);
            CustomSourceWriteAccess.EnsureNotSystemSource(normalizedKey);
            var source = store.GetSource(normalizedKey);
            if (source == null) Problem(404, "source_not_found", "The custom source was not found.");
            var token = CustomSourceWriteAccess.GenerateToken();
            store.RotateToken(normalizedKey, CustomSourceWriteAccess.HashToken(token), now);
            var response = new CustomSourceCreateResponse
            {
                Source = CustomSourceStore.ToManagementView(source, now),
                Ingest = new CustomSourceIngestInfo
                {
                    EndpointPath = $"/api/custom-sources/{normalizedKey}/events",
                    EndpointUrl = endpointUrl,
                    LocalEndpointUrl = localEndpointUrl,
                    Token = token
                }
            };
            response.Source.ItemCount = store.GetItemCount(source, now);
            return response;
        }

        public CustomDeleteResult DeleteSource(string sourceKey)
        {
            var normalizedKey = CustomSourceWriteAccess.NormalizeSourceKey(sourceKey, false);
            CustomSourceWriteAccess.EnsureNotSystemSource(normalizedKey);
            var change = store.DeleteSource(normalizedKey);
            writeAccess.RemoveRateState(normalizedKey);
            eventHub.Publish("custom-card", new
            {
                cardId = change.CardId,
                sourceKey = change.SourceKey,
                revision = change.Revision,
                reason = "deleted"
            });
            return new CustomDeleteResult
            {
                Deleted = true,
                SourceKey = change.SourceKey,
                CardId = change.CardId,
                Revision = change.Revision
            };
        }

        public CustomIngestResult Ingest(string sourceKey, string token, JsonElement payload, DateTimeOffset now)
        {
            var source = writeAccess.Authenticate(sourceKey, token);
            writeAccess.EnsureRateLimit(source, now);

            var normalized = CustomSourcePayloadNormalizer.Normalize(source, payload, now);
            var result = store.Ingest(source, normalized);
            eventHub.Publish("custom-card", new
            {
                cardId = result.CardId,
                sourceKey = result.SourceKey,
                revision = result.Revision,
                reason = "updated"
            });
            return result;
        }

        public CustomDeleteResult DeleteItem(string sourceKey, string token, string itemKey, DateTimeOffset now)
        {
            var source = writeAccess.Authenticate(sourceKey, token);
            writeAccess.EnsureRateLimit(source, now);
            if (!CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                Problem(409, "card_type_mismatch", "Item deletion only applies to message-feed cards.");
            }
            if (!IsValidItemKey(itemKey))
            {
                Problem(400, "invalid_item_key", "The item id is not valid.");
            }
            var result = store.DeleteItem(source.SourceKey, itemKey, now);
            eventHub.Publish("custom-card", new
            {
                cardId = result.CardId,
                sourceKey = result.SourceKey,
                revision = result.Revision,
                reason = "deleted"
            });
            return result;
        }

        public CustomClearResult ClearState(string sourceKey, string token, DateTimeOffset now)
        {
            var source = writeAccess.Authenticate(sourceKey, token);
            writeAccess.EnsureRateLimit(source, now);
            if (CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                Problem(409, "card_type_mismatch", "State deletion does not apply to message-feed cards.");
            }
            var result = store.ClearState(source.SourceKey, now);
            if (result.Cleared)
            {
                eventHub.Publish("custom-card", new
                {
                    cardId = result.CardId,
                    sourceKey = result.SourceKey,
                    revision = result.Revision,
                    reason = "cleared"
                });
            }
            return result;
        }

        public void CleanupExpired(DateTimeOffset now)
        {
            foreach (var change in store.CleanupExpired(now))
            {
                eventHub.Publish("custom-card", new
                {
                    cardId = change.CardId,
                    sourceKey = change.SourceKey,
                    revision = change.Revision,
                    reason = change.Reason
                });
            }
        }

        private static CustomCardSettingsResponse BuildCardSettings(
            CustomSourceRecord source,
            CustomCardSettingsRecord settings)
        {
            return new CustomCardSettingsResponse
            {
                CardId = source.Card.Id,
                SourceKey = source.SourceKey,
                Title = source.Card.Title,
                Type = source.Card.Type,
                MaxItems = source.Card.MaxItems,
                StreamEnabled = settings.StreamEnabled,
                StreamCharDelayMs = settings.StreamCharDelayMs,
                UpdatedAt = CustomSourceDateTime.ToText(settings.UpdatedAt)
            };
        }

        private int NextPosition()
        {
            var sources = store.GetSources();
            return sources.Count == 0 ? 100 : sources.Max(source => source.Card.Position) + 100;
        }

        private static void RejectImmutableUpdate(string value, string field)
        {
            if (!string.IsNullOrWhiteSpace(value)) Problem(400, "immutable_field", $"{field} cannot be changed.", field, "immutable");
        }

    }
}
