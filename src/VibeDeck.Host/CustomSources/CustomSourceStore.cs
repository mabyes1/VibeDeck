using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace VibeDeck.Host.CustomSources
{
    public sealed class CustomSourceStore
    {
        public const int SchemaVersion = 1;

        private readonly object initializationGate = new object();
        private readonly string databaseDirectory;
        private readonly string databasePath;
        private bool available;
        private Exception initializationError;

        public CustomSourceStore() : this(null)
        {
        }

        public CustomSourceStore(string databasePathOverride)
        {
            databasePath = string.IsNullOrWhiteSpace(databasePathOverride)
                ? Path.Combine(AppPaths.CustomSourcesDirectory, "custom-sources.db")
                : Path.GetFullPath(databasePathOverride);
            databaseDirectory = Path.GetDirectoryName(databasePath);

            try
            {
                Initialize();
                available = true;
            }
            catch (Exception error)
            {
                if (IsDatabaseCorruption(error))
                {
                    try
                    {
                        QuarantineCorruptDatabase();
                        Initialize();
                        available = true;
                    }
                    catch (Exception recoveryError)
                    {
                        initializationError = recoveryError;
                        available = false;
                    }
                }
                else
                {
                    initializationError = error;
                    available = false;
                }
            }
        }

        public string DatabasePath => databasePath;
        public bool IsAvailable => available;
        public Exception InitializationError => initializationError;

        public IReadOnlyList<CustomSourceRecord> GetSources()
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT s.id, s.source_key, s.display_name, s.token_hash, s.enabled,
       s.created_at, s.updated_at, s.last_received_at,
       c.id, c.source_id, c.card_key, c.type, c.title, c.position,
       c.stale_after_seconds, c.default_ttl_seconds, c.max_items,
       c.revision, c.created_at, c.updated_at
FROM custom_sources s
INNER JOIN custom_cards c ON c.source_id = s.id AND c.card_key = 'default'
ORDER BY c.position ASC, c.title COLLATE NOCASE ASC, c.id ASC;";

            var result = new List<CustomSourceRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var source = ReadSource(reader);
                source.Card = ReadCard(reader, 8);
                result.Add(source);
            }

            return result;
        }

        public CustomSourceRecord GetSource(string sourceKey)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            return ReadSource(connection, null, sourceKey);
        }

        public CustomSourceRecord GetSourceByCardId(string cardId)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            return ReadCardSourceByCardId(connection, null, cardId);
        }

        public CustomCardSettingsRecord GetCardSettings(string cardId)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            var source = ReadCardSourceByCardId(connection, null, cardId);
            return source == null ? null : ReadCardSettings(connection, null, cardId, source.Card.UpdatedAt);
        }

        public CustomSourceRecord EnsureSystemSource(CustomSourceRecord source, CustomCardRecord card)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var existing = ReadSource(connection, transaction, source.SourceKey);
            if (existing != null)
            {
                transaction.Rollback();
                return existing;
            }

            using (var insertSource = connection.CreateCommand())
            {
                insertSource.Transaction = transaction;
                insertSource.CommandText = @"
INSERT INTO custom_sources
  (id, source_key, display_name, token_hash, enabled, created_at, updated_at, last_received_at)
VALUES
  (@id, @sourceKey, @displayName, @tokenHash, @enabled, @createdAt, @updatedAt, NULL);";
                Add(insertSource, "@id", source.Id);
                Add(insertSource, "@sourceKey", source.SourceKey);
                Add(insertSource, "@displayName", source.DisplayName);
                Add(insertSource, "@tokenHash", source.TokenHash);
                Add(insertSource, "@enabled", source.Enabled ? 1 : 0);
                Add(insertSource, "@createdAt", CustomSourceDateTime.ToText(source.CreatedAt));
                Add(insertSource, "@updatedAt", CustomSourceDateTime.ToText(source.UpdatedAt));
                insertSource.ExecuteNonQuery();
            }

            using (var insertCard = connection.CreateCommand())
            {
                insertCard.Transaction = transaction;
                insertCard.CommandText = @"
INSERT INTO custom_cards
  (id, source_id, card_key, type, title, position, stale_after_seconds,
   default_ttl_seconds, max_items, revision, created_at, updated_at)
VALUES
  (@id, @sourceId, @cardKey, @type, @title, @position, @staleAfter,
   @defaultTtl, @maxItems, @revision, @createdAt, @updatedAt);";
                Add(insertCard, "@id", card.Id);
                Add(insertCard, "@sourceId", source.Id);
                Add(insertCard, "@cardKey", card.CardKey);
                Add(insertCard, "@type", card.Type);
                Add(insertCard, "@title", card.Title);
                Add(insertCard, "@position", card.Position);
                Add(insertCard, "@staleAfter", card.StaleAfterSeconds);
                Add(insertCard, "@defaultTtl", card.DefaultTtlSeconds);
                Add(insertCard, "@maxItems", card.MaxItems);
                Add(insertCard, "@revision", card.Revision);
                Add(insertCard, "@createdAt", CustomSourceDateTime.ToText(card.CreatedAt));
                Add(insertCard, "@updatedAt", CustomSourceDateTime.ToText(card.UpdatedAt));
                insertCard.ExecuteNonQuery();
            }

            transaction.Commit();
            return GetSource(source.SourceKey);
        }

        public CustomSourceCreateResponse CreateSource(
            CustomSourceRecord source,
            CustomCardRecord card,
            int maxSources,
            string endpointUrl,
            string localEndpointUrl,
            string token)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                using (var count = connection.CreateCommand())
                {
                    count.Transaction = transaction;
                    count.CommandText = "SELECT COUNT(*) FROM custom_sources;";
                    if (Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture) >= maxSources)
                    {
                        throw new CustomSourceProblemException(
                            409,
                            "source_limit_reached",
                            $"The Host already has the maximum of {maxSources} custom sources.");
                    }
                }

                using (var insertSource = connection.CreateCommand())
                {
                    insertSource.Transaction = transaction;
                    insertSource.CommandText = @"
INSERT INTO custom_sources
  (id, source_key, display_name, token_hash, enabled, created_at, updated_at, last_received_at)
VALUES
  (@id, @sourceKey, @displayName, @tokenHash, @enabled, @createdAt, @updatedAt, NULL);";
                    Add(insertSource, "@id", source.Id);
                    Add(insertSource, "@sourceKey", source.SourceKey);
                    Add(insertSource, "@displayName", source.DisplayName);
                    Add(insertSource, "@tokenHash", source.TokenHash);
                    Add(insertSource, "@enabled", source.Enabled ? 1 : 0);
                    Add(insertSource, "@createdAt", CustomSourceDateTime.ToText(source.CreatedAt));
                    Add(insertSource, "@updatedAt", CustomSourceDateTime.ToText(source.UpdatedAt));
                    insertSource.ExecuteNonQuery();
                }

                using (var insertCard = connection.CreateCommand())
                {
                    insertCard.Transaction = transaction;
                    insertCard.CommandText = @"
INSERT INTO custom_cards
  (id, source_id, card_key, type, title, position, stale_after_seconds,
   default_ttl_seconds, max_items, revision, created_at, updated_at)
VALUES
  (@id, @sourceId, @cardKey, @type, @title, @position, @staleAfter,
   @defaultTtl, @maxItems, @revision, @createdAt, @updatedAt);";
                    Add(insertCard, "@id", card.Id);
                    Add(insertCard, "@sourceId", source.Id);
                    Add(insertCard, "@cardKey", card.CardKey);
                    Add(insertCard, "@type", card.Type);
                    Add(insertCard, "@title", card.Title);
                    Add(insertCard, "@position", card.Position);
                    Add(insertCard, "@staleAfter", card.StaleAfterSeconds);
                    Add(insertCard, "@defaultTtl", card.DefaultTtlSeconds);
                    Add(insertCard, "@maxItems", card.MaxItems);
                    Add(insertCard, "@revision", card.Revision);
                    Add(insertCard, "@createdAt", CustomSourceDateTime.ToText(card.CreatedAt));
                    Add(insertCard, "@updatedAt", CustomSourceDateTime.ToText(card.UpdatedAt));
                    insertCard.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (CustomSourceProblemException)
            {
                transaction.Rollback();
                throw;
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(
                    409,
                    "source_key_exists",
                    "The source key is already in use.",
                    new Dictionary<string, string> { ["sourceKey"] = "already_exists" });
            }

            return new CustomSourceCreateResponse
            {
                Source = ToManagementView(source, card, 0, DateTimeOffset.UtcNow),
                Ingest = new CustomSourceIngestInfo
                {
                    EndpointPath = $"/api/custom-sources/{source.SourceKey}/events",
                    EndpointUrl = endpointUrl,
                    LocalEndpointUrl = localEndpointUrl,
                    Token = token
                }
            };
        }

        public CustomSourceRecord UpdateSource(
            string sourceKey,
            string displayName,
            bool enabled,
            string title,
            int position,
            int staleAfterSeconds,
            int defaultTtlSeconds,
            int maxItems,
            bool cardChanged,
            DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadSource(connection, transaction, sourceKey);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "source_not_found", "The custom source was not found.");
            }

            using (var updateSource = connection.CreateCommand())
            {
                updateSource.Transaction = transaction;
                updateSource.CommandText = @"
UPDATE custom_sources
SET display_name = @displayName, enabled = @enabled, updated_at = @updatedAt
WHERE id = @id;";
                Add(updateSource, "@displayName", displayName);
                Add(updateSource, "@enabled", enabled ? 1 : 0);
                Add(updateSource, "@updatedAt", CustomSourceDateTime.ToText(now));
                Add(updateSource, "@id", source.Id);
                updateSource.ExecuteNonQuery();
            }

            using (var updateCard = connection.CreateCommand())
            {
                updateCard.Transaction = transaction;
                updateCard.CommandText = @"
UPDATE custom_cards
SET title = @title,
    position = @position,
    stale_after_seconds = @staleAfter,
    default_ttl_seconds = @defaultTtl,
    max_items = @maxItems,
    revision = revision + @revisionDelta,
    updated_at = @updatedAt
WHERE id = @id;";
                Add(updateCard, "@title", title);
                Add(updateCard, "@position", position);
                Add(updateCard, "@staleAfter", staleAfterSeconds);
                Add(updateCard, "@defaultTtl", defaultTtlSeconds);
                Add(updateCard, "@maxItems", maxItems);
                Add(updateCard, "@revisionDelta", cardChanged || source.Enabled != enabled ? 1 : 0);
                Add(updateCard, "@updatedAt", CustomSourceDateTime.ToText(now));
                Add(updateCard, "@id", source.Card.Id);
                updateCard.ExecuteNonQuery();
            }

            transaction.Commit();
            return GetSource(sourceKey);
        }

        public CustomSourceRecord UpdateCardSettings(
            string cardId,
            int maxItems,
            bool streamEnabled,
            int streamCharDelayMs,
            DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadCardSourceByCardId(connection, transaction, cardId);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "card_not_found", "The custom card was not found.");
            }

            using (var updateCard = connection.CreateCommand())
            {
                updateCard.Transaction = transaction;
                updateCard.CommandText = @"
UPDATE custom_cards
SET max_items = @maxItems, revision = revision + 1, updated_at = @updatedAt
WHERE id = @id;";
                Add(updateCard, "@maxItems", maxItems);
                Add(updateCard, "@updatedAt", CustomSourceDateTime.ToText(now));
                Add(updateCard, "@id", cardId);
                updateCard.ExecuteNonQuery();
            }

            if (CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                using var trim = connection.CreateCommand();
                trim.Transaction = transaction;
                trim.CommandText = @"
DELETE FROM custom_items
WHERE card_id = @cardId
  AND item_key IN (
    SELECT item_key FROM custom_items
    WHERE card_id = @cardId
    ORDER BY revision DESC
    LIMIT -1 OFFSET @maxItems
  );";
                Add(trim, "@cardId", cardId);
                Add(trim, "@maxItems", maxItems);
                trim.ExecuteNonQuery();
            }

            using (var settings = connection.CreateCommand())
            {
                settings.Transaction = transaction;
                settings.CommandText = @"
INSERT INTO custom_card_settings (card_id, stream_enabled, stream_char_delay_ms, updated_at)
VALUES (@cardId, @streamEnabled, @streamCharDelayMs, @updatedAt)
ON CONFLICT(card_id) DO UPDATE SET
  stream_enabled = excluded.stream_enabled,
  stream_char_delay_ms = excluded.stream_char_delay_ms,
  updated_at = excluded.updated_at;";
                Add(settings, "@cardId", cardId);
                Add(settings, "@streamEnabled", streamEnabled ? 1 : 0);
                Add(settings, "@streamCharDelayMs", streamCharDelayMs);
                Add(settings, "@updatedAt", CustomSourceDateTime.ToText(now));
                settings.ExecuteNonQuery();
            }

            transaction.Commit();
            return GetSourceByCardId(cardId);
        }

        public void RotateToken(string sourceKey, string tokenHash, DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE custom_sources SET token_hash = @tokenHash, updated_at = @updatedAt
WHERE source_key = @sourceKey;";
            Add(command, "@tokenHash", tokenHash);
            Add(command, "@updatedAt", CustomSourceDateTime.ToText(now));
            Add(command, "@sourceKey", sourceKey);
            if (command.ExecuteNonQuery() == 0)
            {
                throw new CustomSourceProblemException(404, "source_not_found", "The custom source was not found.");
            }
        }

        public CustomCardChange DeleteSource(string sourceKey)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadSource(connection, transaction, sourceKey);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "source_not_found", "The custom source was not found.");
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM custom_sources WHERE id = @id;";
            Add(command, "@id", source.Id);
            command.ExecuteNonQuery();
            transaction.Commit();

            return new CustomCardChange
            {
                SourceKey = source.SourceKey,
                CardId = source.Card.Id,
                Revision = source.Card.Revision + 1,
                Reason = "deleted"
            };
        }

        public CustomIngestResult Ingest(CustomSourceRecord authenticatedSource, CustomNormalizedPayload payload)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadSource(connection, transaction, authenticatedSource.SourceKey);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(401, "invalid_source_token", "The source token is invalid.");
            }

            if (!source.Enabled)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(403, "source_disabled", "The custom source is disabled.");
            }

            var nextRevision = source.Card.Revision + 1;
            var exists = false;
            using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = @"
SELECT COUNT(*) FROM custom_items WHERE card_id = @cardId AND item_key = @itemKey;";
                Add(check, "@cardId", source.Card.Id);
                Add(check, "@itemKey", payload.ItemKey);
                exists = Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }

            using (var item = connection.CreateCommand())
            {
                item.Transaction = transaction;
                item.CommandText = exists
                    ? @"
UPDATE custom_items
SET payload_json = @payload, occurred_at = @occurredAt, received_at = @receivedAt,
    expires_at = @expiresAt, revision = @revision
WHERE card_id = @cardId AND item_key = @itemKey;"
                    : @"
INSERT INTO custom_items
  (card_id, item_key, payload_json, occurred_at, received_at, expires_at, revision)
VALUES
  (@cardId, @itemKey, @payload, @occurredAt, @receivedAt, @expiresAt, @revision);";
                Add(item, "@payload", payload.ContentJson);
                Add(item, "@occurredAt", CustomSourceDateTime.ToText(payload.OccurredAt));
                Add(item, "@receivedAt", CustomSourceDateTime.ToText(payload.ReceivedAt));
                Add(item, "@expiresAt", CustomSourceDateTime.ToText(payload.ExpiresAt));
                Add(item, "@revision", nextRevision);
                Add(item, "@cardId", source.Card.Id);
                Add(item, "@itemKey", payload.ItemKey);
                item.ExecuteNonQuery();
            }

            if (CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                using var trim = connection.CreateCommand();
                trim.Transaction = transaction;
                trim.CommandText = @"
DELETE FROM custom_items
WHERE card_id = @cardId
  AND item_key IN (
    SELECT item_key FROM custom_items
    WHERE card_id = @cardId
    ORDER BY revision DESC
    LIMIT -1 OFFSET @maxItems
  );";
                Add(trim, "@cardId", source.Card.Id);
                Add(trim, "@maxItems", source.Card.MaxItems);
                trim.ExecuteNonQuery();
            }

            UpdateRevisionAndReceivedAt(connection, transaction, source, nextRevision, payload.ReceivedAt);
            transaction.Commit();

            return new CustomIngestResult
            {
                SourceKey = source.SourceKey,
                CardId = source.Card.Id,
                Revision = nextRevision,
                ReceivedAt = CustomSourceDateTime.ToText(payload.ReceivedAt),
                Operation = CustomSourceCardTypes.IsFeed(source.Card.Type)
                    ? (exists ? "updated" : "inserted")
                    : "replaced"
            };
        }

        public CustomDeleteResult DeleteItem(string sourceKey, string itemKey, DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadSource(connection, transaction, sourceKey);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "source_not_found", "The custom source was not found.");
            }

            if (!CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(409, "card_type_mismatch", "Item deletion only applies to message-feed cards.");
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = @"
DELETE FROM custom_items WHERE card_id = @cardId AND item_key = @itemKey;";
            Add(delete, "@cardId", source.Card.Id);
            Add(delete, "@itemKey", itemKey);
            if (delete.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "item_not_found", "The item was not found.");
            }

            var revision = source.Card.Revision + 1;
            UpdateCardRevision(connection, transaction, source, revision, now);
            transaction.Commit();
            return new CustomDeleteResult
            {
                Deleted = true,
                SourceKey = source.SourceKey,
                CardId = source.Card.Id,
                Revision = revision
            };
        }

        public CustomClearResult ClearState(string sourceKey, DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadSource(connection, transaction, sourceKey);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "source_not_found", "The custom source was not found.");
            }

            if (CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(409, "card_type_mismatch", "State deletion does not apply to message-feed cards.");
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = @"
DELETE FROM custom_items WHERE card_id = @cardId AND item_key = 'current';";
            Add(delete, "@cardId", source.Card.Id);
            var cleared = delete.ExecuteNonQuery() > 0;
            var revision = source.Card.Revision;
            if (cleared)
            {
                revision++;
                UpdateCardRevision(connection, transaction, source, revision, now);
            }

            transaction.Commit();
            return new CustomClearResult
            {
                Cleared = cleared,
                SourceKey = source.SourceKey,
                CardId = source.Card.Id,
                Revision = revision
            };
        }

        public CustomClearResult ClearCard(string cardId, DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var source = ReadCardSourceByCardId(connection, transaction, cardId);
            if (source == null)
            {
                transaction.Rollback();
                throw new CustomSourceProblemException(404, "card_not_found", "The custom card was not found.");
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM custom_items WHERE card_id = @cardId;";
            Add(delete, "@cardId", cardId);
            var cleared = delete.ExecuteNonQuery() > 0;
            var revision = source.Card.Revision;
            if (cleared)
            {
                revision++;
                UpdateCardRevision(connection, transaction, source, revision, now);
            }

            transaction.Commit();
            return new CustomClearResult
            {
                Cleared = cleared,
                SourceKey = source.SourceKey,
                CardId = source.Card.Id,
                Revision = revision
            };
        }

        public IReadOnlyList<CustomCardChange> CleanupExpired(DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var cardIds = new List<string>();
            using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = @"
SELECT DISTINCT card_id FROM custom_items
WHERE expires_at IS NOT NULL AND expires_at <= @now;";
                Add(find, "@now", CustomSourceDateTime.ToText(now));
                using var reader = find.ExecuteReader();
                while (reader.Read()) cardIds.Add(reader.GetString(0));
            }

            if (cardIds.Count == 0)
            {
                transaction.Rollback();
                return Array.Empty<CustomCardChange>();
            }

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = @"
DELETE FROM custom_items
WHERE expires_at IS NOT NULL AND expires_at <= @now;";
                Add(delete, "@now", CustomSourceDateTime.ToText(now));
                delete.ExecuteNonQuery();
            }

            var changes = new List<CustomCardChange>();
            foreach (var cardId in cardIds.Distinct(StringComparer.Ordinal))
            {
                var row = ReadCardSourceByCardId(connection, transaction, cardId);
                if (row == null) continue;
                var revision = row.Card.Revision + 1;
                UpdateCardRevision(connection, transaction, row, revision, now);
                changes.Add(new CustomCardChange
                {
                    SourceKey = row.SourceKey,
                    CardId = row.Card.Id,
                    Revision = revision,
                    Reason = "expired"
                });
            }

            transaction.Commit();
            return changes;
        }

        public IReadOnlyList<CustomCardSnapshotResponse> GetCardSnapshots(DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT s.id, s.source_key, s.display_name, s.token_hash, s.enabled,
       s.created_at, s.updated_at, s.last_received_at,
       c.id, c.source_id, c.card_key, c.type, c.title, c.position,
       c.stale_after_seconds, c.default_ttl_seconds, c.max_items,
       c.revision, c.created_at, c.updated_at
FROM custom_sources s
INNER JOIN custom_cards c ON c.source_id = s.id AND c.card_key = 'default'
WHERE s.enabled = 1
ORDER BY c.position ASC, c.title COLLATE NOCASE ASC, c.id ASC;";

            var cards = new List<CustomCardSnapshotResponse>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var source = ReadSource(reader);
                source.Card = ReadCard(reader, 8);
                var items = ReadActiveItems(connection, source.Card, now);
                cards.Add(BuildSnapshot(connection, source, items, now));
            }

            return cards;
        }

        private void Initialize()
        {
            lock (initializationGate)
            {
                if (available) return;
                Directory.CreateDirectory(databaseDirectory);
                using var connection = OpenConnectionWithoutAvailabilityCheck();
                using (var integrity = connection.CreateCommand())
                {
                    integrity.CommandText = "PRAGMA quick_check;";
                    var result = Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture);
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Custom Sources database integrity check failed: {result}");
                    }
                }
                using (var versionCommand = connection.CreateCommand())
                {
                    versionCommand.CommandText = "PRAGMA user_version;";
                    var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                    if (version > SchemaVersion)
                    {
                        throw new InvalidOperationException($"Custom Sources database version {version} is newer than supported version {SchemaVersion}.");
                    }
                }

                using var transaction = connection.BeginTransaction();
                using (var schema = connection.CreateCommand())
                {
                    schema.Transaction = transaction;
                    schema.CommandText = @"
CREATE TABLE IF NOT EXISTS custom_sources (
  id TEXT PRIMARY KEY,
  source_key TEXT NOT NULL COLLATE NOCASE UNIQUE,
  display_name TEXT NOT NULL,
  token_hash TEXT NOT NULL,
  enabled INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  last_received_at TEXT NULL
);

CREATE TABLE IF NOT EXISTS custom_cards (
  id TEXT PRIMARY KEY,
  source_id TEXT NOT NULL,
  card_key TEXT NOT NULL,
  type TEXT NOT NULL,
  title TEXT NOT NULL,
  position INTEGER NOT NULL,
  stale_after_seconds INTEGER NOT NULL,
  default_ttl_seconds INTEGER NOT NULL,
  max_items INTEGER NOT NULL,
  revision INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(source_id, card_key),
  FOREIGN KEY(source_id) REFERENCES custom_sources(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS custom_items (
  card_id TEXT NOT NULL,
  item_key TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  occurred_at TEXT NULL,
  received_at TEXT NOT NULL,
  expires_at TEXT NULL,
  revision INTEGER NOT NULL,
  PRIMARY KEY(card_id, item_key),
  FOREIGN KEY(card_id) REFERENCES custom_cards(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS custom_card_settings (
  card_id TEXT PRIMARY KEY,
  stream_enabled INTEGER NOT NULL DEFAULT 1,
  stream_char_delay_ms INTEGER NOT NULL DEFAULT 28,
  updated_at TEXT NOT NULL,
  FOREIGN KEY(card_id) REFERENCES custom_cards(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_custom_cards_position ON custom_cards(position, title);
CREATE INDEX IF NOT EXISTS ix_custom_items_expiry ON custom_items(expires_at);
CREATE INDEX IF NOT EXISTS ix_custom_items_revision ON custom_items(card_id, revision DESC);
PRAGMA user_version = 1;";
                    schema.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        private SqliteConnection OpenConnection()
        {
            EnsureAvailable();
            return OpenConnectionWithoutAvailabilityCheck();
        }

        private SqliteConnection OpenConnectionWithoutAvailabilityCheck()
        {
            var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=5;Pooling=False");
            try
            {
                connection.Open();
                using (var foreignKeys = connection.CreateCommand())
                {
                    // The Host can be terminated by Windows Setup after the SCM
                    // shutdown timeout. This store is small and write-light, so a
                    // rollback journal with FULL sync is safer than leaving a WAL
                    // checkpoint in flight during a service upgrade.
                    foreignKeys.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA journal_mode = DELETE; PRAGMA synchronous = FULL;";
                    foreignKeys.ExecuteNonQuery();
                }
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private void EnsureAvailable()
        {
            if (!available)
            {
                throw new CustomSourceStoreUnavailableException(
                    "Custom source storage is unavailable.",
                    initializationError);
            }
        }

        private static bool IsDatabaseCorruption(Exception error)
        {
            if (error is InvalidDataException) return true;
            if (error is SqliteException sqlite && (sqlite.SqliteErrorCode == 11 || sqlite.SqliteErrorCode == 26)) return true;
            return error?.InnerException != null && IsDatabaseCorruption(error.InnerException);
        }

        private void QuarantineCorruptDatabase()
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var source = databasePath + suffix;
                if (!File.Exists(source)) continue;
                var destination = databasePath + $".corrupt-{stamp}{suffix}";
                File.Move(source, destination, false);
            }
        }

        private static CustomSourceRecord ReadSource(SqliteConnection connection, SqliteTransaction transaction, string sourceKey)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT s.id, s.source_key, s.display_name, s.token_hash, s.enabled,
       s.created_at, s.updated_at, s.last_received_at,
       c.id, c.source_id, c.card_key, c.type, c.title, c.position,
       c.stale_after_seconds, c.default_ttl_seconds, c.max_items,
       c.revision, c.created_at, c.updated_at
FROM custom_sources s
INNER JOIN custom_cards c ON c.source_id = s.id AND c.card_key = 'default'
WHERE s.source_key = @sourceKey COLLATE NOCASE
LIMIT 1;";
            Add(command, "@sourceKey", sourceKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            var source = ReadSource(reader);
            source.Card = ReadCard(reader, 8);
            return source;
        }

        private static CustomSourceRecord ReadSource(SqliteDataReader reader)
        {
            return new CustomSourceRecord
            {
                Id = reader.GetString(0),
                SourceKey = reader.GetString(1),
                DisplayName = reader.GetString(2),
                TokenHash = reader.GetString(3),
                Enabled = reader.GetInt32(4) != 0,
                CreatedAt = ParseDate(reader.GetString(5)),
                UpdatedAt = ParseDate(reader.GetString(6)),
                LastReceivedAt = ReadNullableDate(reader, 7)
            };
        }

        private static CustomCardRecord ReadCard(SqliteDataReader reader, int offset)
        {
            return new CustomCardRecord
            {
                Id = reader.GetString(offset),
                SourceId = reader.GetString(offset + 1),
                CardKey = reader.GetString(offset + 2),
                Type = reader.GetString(offset + 3),
                Title = reader.GetString(offset + 4),
                Position = reader.GetInt32(offset + 5),
                StaleAfterSeconds = reader.GetInt32(offset + 6),
                DefaultTtlSeconds = reader.GetInt32(offset + 7),
                MaxItems = reader.GetInt32(offset + 8),
                Revision = reader.GetInt64(offset + 9),
                CreatedAt = ParseDate(reader.GetString(offset + 10)),
                UpdatedAt = ParseDate(reader.GetString(offset + 11))
            };
        }

        private static CustomSourceRecord ReadCardSourceByCardId(SqliteConnection connection, SqliteTransaction transaction, string cardId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT s.id, s.source_key, s.display_name, s.token_hash, s.enabled,
       s.created_at, s.updated_at, s.last_received_at,
       c.id, c.source_id, c.card_key, c.type, c.title, c.position,
       c.stale_after_seconds, c.default_ttl_seconds, c.max_items,
       c.revision, c.created_at, c.updated_at
FROM custom_sources s
INNER JOIN custom_cards c ON c.source_id = s.id
WHERE c.id = @cardId
LIMIT 1;";
            Add(command, "@cardId", cardId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            var source = ReadSource(reader);
            source.Card = ReadCard(reader, 8);
            return source;
        }

        private static List<CustomItemRow> ReadActiveItems(SqliteConnection connection, CustomCardRecord card, DateTimeOffset now)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT item_key, payload_json, occurred_at, received_at, expires_at, revision
FROM custom_items
WHERE card_id = @cardId
  AND (expires_at IS NULL OR expires_at > @now)
ORDER BY revision DESC
LIMIT @maxItems;";
            Add(command, "@cardId", card.Id);
            Add(command, "@now", CustomSourceDateTime.ToText(now));
            Add(command, "@maxItems", CustomSourceCardTypes.IsFeed(card.Type) ? card.MaxItems : 1);
            var rows = new List<CustomItemRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new CustomItemRow
                {
                    ItemKey = reader.GetString(0),
                    PayloadJson = reader.GetString(1),
                    OccurredAt = ReadNullableDate(reader, 2),
                    ReceivedAt = ParseDate(reader.GetString(3)),
                    ExpiresAt = ReadNullableDate(reader, 4),
                    Revision = reader.GetInt64(5)
                });
            }
            return rows;
        }

        private static CustomCardSnapshotResponse BuildSnapshot(
            SqliteConnection connection,
            CustomSourceRecord source,
            List<CustomItemRow> items,
            DateTimeOffset now)
        {
            var lastReceivedAt = items.Count == 0 ? (DateTimeOffset?)null : items.Max(item => item.ReceivedAt);
            object content = null;
            var settings = ReadCardSettings(connection, null, source.Card.Id, source.Card.UpdatedAt);

            if (CustomSourceCardTypes.IsFeed(source.Card.Type))
            {
                var feedItems = items
                    .OrderByDescending(item => item.Revision)
                    .Select(item => ParseJson(item.PayloadJson))
                    .ToList();
                if (feedItems.Count > 0)
                {
                    content = new Dictionary<string, object> { ["items"] = feedItems };
                }
            }
            else if (items.Count > 0)
            {
                content = ParseJson(items[0].PayloadJson);
            }

            var freshness = "empty";
            if (content != null)
            {
                freshness = source.Card.StaleAfterSeconds == 0 ||
                    (lastReceivedAt.HasValue && now - lastReceivedAt.Value <= TimeSpan.FromSeconds(source.Card.StaleAfterSeconds))
                    ? "fresh"
                    : "stale";
            }

            return new CustomCardSnapshotResponse
            {
                CardId = source.Card.Id,
                SourceKey = source.SourceKey,
                Title = source.Card.Title,
                Type = source.Card.Type,
                Position = source.Card.Position,
                Revision = source.Card.Revision,
                Freshness = freshness,
                LastReceivedAt = CustomSourceDateTime.ToText(lastReceivedAt),
                MaxItems = source.Card.MaxItems,
                StreamEnabled = settings.StreamEnabled,
                StreamCharDelayMs = settings.StreamCharDelayMs,
                Content = content
            };
        }

        private static CustomCardSettingsRecord ReadCardSettings(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string cardId,
            DateTimeOffset fallbackUpdatedAt)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT stream_enabled, stream_char_delay_ms, updated_at
FROM custom_card_settings
WHERE card_id = @cardId
LIMIT 1;";
            Add(command, "@cardId", cardId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return new CustomCardSettingsRecord
                {
                    CardId = cardId,
                    StreamEnabled = CustomCardSettingsDefaults.StreamEnabled,
                    StreamCharDelayMs = CustomCardSettingsDefaults.StreamCharDelayMs,
                    UpdatedAt = fallbackUpdatedAt
                };
            }

            return new CustomCardSettingsRecord
            {
                CardId = cardId,
                StreamEnabled = reader.GetInt32(0) != 0,
                StreamCharDelayMs = reader.GetInt32(1),
                UpdatedAt = ParseDate(reader.GetString(2))
            };
        }

        private static object ParseJson(string json)
        {
            using var document = JsonDocument.Parse(json, CustomSourceJson.DocumentOptions);
            return document.RootElement.Clone();
        }

        private static CustomSourceManagementView ToManagementView(
            CustomSourceRecord source,
            CustomCardRecord card,
            int itemCount,
            DateTimeOffset now)
        {
            var health = "waiting";
            if (!source.Enabled) health = "disabled";
            else if (source.LastReceivedAt.HasValue && card.StaleAfterSeconds > 0 &&
                now - source.LastReceivedAt.Value > TimeSpan.FromSeconds(card.StaleAfterSeconds)) health = "stale";
            else if (source.LastReceivedAt.HasValue) health = "active";

            return new CustomSourceManagementView
            {
                SourceKey = source.SourceKey,
                DisplayName = source.DisplayName,
                Enabled = source.Enabled,
                CreatedAt = CustomSourceDateTime.ToText(source.CreatedAt),
                UpdatedAt = CustomSourceDateTime.ToText(source.UpdatedAt),
                LastReceivedAt = CustomSourceDateTime.ToText(source.LastReceivedAt),
                ItemCount = itemCount,
                Health = health,
                Card = new CustomCardManagementView
                {
                    CardId = card.Id,
                    CardKey = card.CardKey,
                    Type = card.Type,
                    Title = card.Title,
                    Position = card.Position,
                    StaleAfterSeconds = card.StaleAfterSeconds,
                    DefaultTtlSeconds = card.DefaultTtlSeconds,
                    MaxItems = card.MaxItems,
                    Revision = card.Revision
                }
            };
        }

        public static CustomSourceManagementView ToManagementView(CustomSourceRecord source, DateTimeOffset now)
        {
            return ToManagementView(source, source.Card, 0, now);
        }

        public int GetItemCount(CustomSourceRecord source, DateTimeOffset now)
        {
            EnsureAvailable();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*) FROM custom_items
WHERE card_id = @cardId AND (expires_at IS NULL OR expires_at > @now);";
            Add(command, "@cardId", source.Card.Id);
            Add(command, "@now", CustomSourceDateTime.ToText(now));
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void UpdateRevisionAndReceivedAt(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CustomSourceRecord source,
            long revision,
            DateTimeOffset receivedAt)
        {
            UpdateCardRevision(connection, transaction, source, revision, receivedAt);
            using var updateSource = connection.CreateCommand();
            updateSource.Transaction = transaction;
            updateSource.CommandText = @"
UPDATE custom_sources
SET last_received_at = @receivedAt, updated_at = @receivedAt
WHERE id = @id;";
            Add(updateSource, "@receivedAt", CustomSourceDateTime.ToText(receivedAt));
            Add(updateSource, "@id", source.Id);
            updateSource.ExecuteNonQuery();
        }

        private static void UpdateCardRevision(
            SqliteConnection connection,
            SqliteTransaction transaction,
            CustomSourceRecord source,
            long revision,
            DateTimeOffset now)
        {
            using var updateCard = connection.CreateCommand();
            updateCard.Transaction = transaction;
            updateCard.CommandText = @"
UPDATE custom_cards SET revision = @revision, updated_at = @updatedAt
WHERE id = @id;";
            Add(updateCard, "@revision", revision);
            Add(updateCard, "@updatedAt", CustomSourceDateTime.ToText(now));
            Add(updateCard, "@id", source.Card.Id);
            updateCard.ExecuteNonQuery();
        }

        private static DateTimeOffset ParseDate(string value)
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? (DateTimeOffset?)null : ParseDate(reader.GetString(index));
        }

        private static void Add(SqliteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        private sealed class CustomItemRow
        {
            public string ItemKey { get; set; }
            public string PayloadJson { get; set; }
            public DateTimeOffset? OccurredAt { get; set; }
            public DateTimeOffset ReceivedAt { get; set; }
            public DateTimeOffset? ExpiresAt { get; set; }
            public long Revision { get; set; }
        }
    }
}
