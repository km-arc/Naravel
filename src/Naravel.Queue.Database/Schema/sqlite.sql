-- Naravel.Queue.Database - SQLite schema for the default table names.
--
-- Mirrors what `modelBuilder.ConfigureQueueJobs()` maps, with the UTC-ticks value converter on the JobRecord
-- date columns (SQLite cannot compare or order DateTimeOffset values stored as text, so the queue's polling
-- query needs them as INTEGER). Production deployments should use EF Core migrations; this script is for
-- development, tests and tooling. It is idempotent (IF NOT EXISTS).

CREATE TABLE IF NOT EXISTS "QueueJobs" (
    "Id"                TEXT    NOT NULL CONSTRAINT "PK_QueueJobs" PRIMARY KEY,
    "Queue"             TEXT    NOT NULL,
    "Connection"        TEXT    NULL,
    "JobType"           TEXT    NOT NULL,
    "Payload"           TEXT    NOT NULL,
    "Attempts"          INTEGER NOT NULL,
    "MaxAttempts"       INTEGER NOT NULL,
    "Priority"          INTEGER NOT NULL,
    "CreatedAt"         INTEGER NOT NULL,
    "AvailableAt"       INTEGER NOT NULL,
    "ReservedAt"        INTEGER NULL,
    "ReservationId"     TEXT    NULL,
    "FailedAt"          INTEGER NULL,
    "Error"             TEXT    NULL,
    "ChainedJobPayload" TEXT    NULL,
    "BatchId"           TEXT    NULL,
    "TraceParent"       TEXT    NULL,
    "TraceState"        TEXT    NULL
);

CREATE INDEX IF NOT EXISTS "IX_QueueJobs_Polling" ON "QueueJobs" ("Queue", "ReservedAt", "FailedAt", "AvailableAt");

CREATE TABLE IF NOT EXISTS "QueueFailedJobs" (
    "Id"       TEXT NOT NULL CONSTRAINT "PK_QueueFailedJobs" PRIMARY KEY,
    "Envelope" TEXT NOT NULL,
    "Error"    TEXT NOT NULL,
    "FailedAt" TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_QueueFailedJobs_FailedAt" ON "QueueFailedJobs" ("FailedAt");

CREATE TABLE IF NOT EXISTS "QueueBatches" (
    "Id"                       TEXT    NOT NULL CONSTRAINT "PK_QueueBatches" PRIMARY KEY,
    "TotalJobs"                INTEGER NOT NULL,
    "CompletedJobs"            INTEGER NOT NULL,
    "FailedJobs"               INTEGER NOT NULL,
    "CancelledJobs"            INTEGER NOT NULL,
    "AllowFailures"            INTEGER NOT NULL,
    "IsCancelled"              INTEGER NOT NULL,
    "ThenCallbacks"            TEXT    NOT NULL,
    "CatchCallbacks"           TEXT    NOT NULL,
    "FinallyCallbacks"         TEXT    NOT NULL,
    "ThenCallbacksCompleted"   INTEGER NOT NULL,
    "CatchCallbacksCompleted"  INTEGER NOT NULL,
    "FinallyCallbacksCompleted" INTEGER NOT NULL
);
