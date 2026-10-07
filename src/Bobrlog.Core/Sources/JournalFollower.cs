using System.Runtime.CompilerServices;
using Bobrlog.Core.Models;

namespace Bobrlog.Core.Sources;

/// <summary>
/// Live tail implemented by polling with cursors. Unlike <c>journalctl -f</c> this does not depend on
/// inotify, which silently stops working when the per-user inotify watch limit is exhausted.
/// </summary>
public static class JournalFollower
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    public static async IAsyncEnumerable<LogEntry> FollowAsync(IJournalSource source, JournalQuery query,
        [EnumeratorCancellation] CancellationToken ct = default, TimeSpan? interval = null)
    {
        var baseQuery = query with { Since = null, Until = null, AfterCursor = null };

        // Start from the newest existing entry that matches.
        string? cursor = null;
        await foreach (var e in source.QueryAsync(baseQuery with { NewestFirst = true, Limit = 1 }, ct).ConfigureAwait(false))
            cursor = e.Cursor;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval ?? DefaultInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            var next = cursor is null
                ? baseQuery with { NewestFirst = true, Limit = 1 }
                : baseQuery with { NewestFirst = false, AfterCursor = cursor, Limit = 5000 };

            var batch = new List<LogEntry>();
            await foreach (var e in source.QueryAsync(next, ct).ConfigureAwait(false))
                batch.Add(e);

            if (cursor is null)
            {
                // Nothing matched before; the first match becomes the starting point and is reported.
                cursor = batch.LastOrDefault()?.Cursor;
                foreach (var e in batch)
                    yield return e;
                continue;
            }

            foreach (var e in batch)
            {
                cursor = e.Cursor ?? cursor;
                yield return e;
            }
        }
    }
}
