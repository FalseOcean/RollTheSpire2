using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RollCore;

// Independent RollCore library introduced in v17.8-preview4a.
// FastCore Web and future desktop UI should reference this shared core instead of duplicating logic.

public sealed class SeedSearcher
{
    private readonly SearchPlan _plan;
    private readonly OpeningPredictor _predictor;
    private readonly object _writeLock = new();
    private int _found;
    private long _checked;
    private long _lastProgressBucket;

    public SeedSearcher(SearchPlan plan)
    {
        _plan = plan;
        _predictor = new OpeningPredictor(plan);
    }

    public void Search(long start, long end, int maxResults, int printEvery, int threads, string mode, int length)
    {
        Write(new { type = "progress", checked_count = 0, found = 0 });
        if (mode.Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            SearchRandom(end, maxResults, printEvery, threads, length);
        }
        else if (IsFixedSequentialMode(mode))
        {
            SearchFixedSequential(start, end, maxResults, printEvery, threads, length);
        }
        else
        {
            SearchSequential(start, end, maxResults, printEvery, threads);
        }
    }

    private static bool IsFixedSequentialMode(string mode)
    {
        return mode.Equals("normal", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("fixed", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("fixed_sequential", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("padded", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("padded_sequential", StringComparison.OrdinalIgnoreCase);
    }

    private void SearchSequential(long start, long end, int maxResults, int printEvery, int threads)
    {
        using var cts = new CancellationTokenSource();
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads), CancellationToken = cts.Token };
        try
        {
            Parallel.ForEach(Partitioner.Create(start, end), opts, (range, state) =>
            {
                for (long i = range.Item1; i < range.Item2; i++)
                {
                    if (Volatile.Read(ref _found) >= maxResults || cts.IsCancellationRequested)
                    {
                        state.Stop();
                        break;
                    }
                    string seed = SeedText.ToSeedText(i);
                    CheckOne(seed, i, maxResults, printEvery, cts);
                }
            });
        }
        catch (OperationCanceledException) { }
        Write(new { type = "done", checked_count = Volatile.Read(ref _checked), found = Volatile.Read(ref _found) });
    }

    private void SearchFixedSequential(long start, long end, int maxResults, int printEvery, int threads, int length)
    {
        using var cts = new CancellationTokenSource();
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads), CancellationToken = cts.Token };
        try
        {
            Parallel.ForEach(Partitioner.Create(start, end), opts, (range, state) =>
            {
                for (long i = range.Item1; i < range.Item2; i++)
                {
                    if (Volatile.Read(ref _found) >= maxResults || cts.IsCancellationRequested)
                    {
                        state.Stop();
                        break;
                    }
                    string seed = SeedText.ToFixedSeedText(i, length);
                    CheckOne(seed, i, maxResults, printEvery, cts);
                }
            });
        }
        catch (OperationCanceledException) { }
        Write(new { type = "done", checked_count = Volatile.Read(ref _checked), found = Volatile.Read(ref _found) });
    }

    private void SearchRandom(long attempts, int maxResults, int printEvery, int threads, int length)
    {
        using var cts = new CancellationTokenSource();
        var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads), CancellationToken = cts.Token };
        try
        {
            Parallel.For(0, attempts, opts, (i, state) =>
            {
                if (Volatile.Read(ref _found) >= maxResults || cts.IsCancellationRequested)
                {
                    state.Stop();
                    return;
                }
                string seed = SeedText.RandomSeed(length);
                CheckOne(seed, i + 1, maxResults, printEvery, cts);
            });
        }
        catch (OperationCanceledException) { }
        Write(new { type = "done", checked_count = Volatile.Read(ref _checked), found = Volatile.Read(ref _found) });
    }

    private void CheckOne(string seed, long counter, int maxResults, int printEvery, CancellationTokenSource cts)
    {
        // checkedNow is the real number of seeds actually evaluated.
        // counter is only the original sequential index / random loop index; in Parallel.For it can jump far ahead,
        // so it must not be used by the UI as "attempted count".
        long checkedNow = Interlocked.Increment(ref _checked);
        if (checkedNow == 1)
        {
            Write(new { type = "progress", checked_count = checkedNow, found = Volatile.Read(ref _found) });
        }
        else if (printEvery > 0)
        {
            long bucket = checkedNow / printEvery;
            long old = Interlocked.Read(ref _lastProgressBucket);
            if (bucket > old && Interlocked.CompareExchange(ref _lastProgressBucket, bucket, old) == old)
            {
                Write(new { type = "progress", checked_count = checkedNow, found = Volatile.Read(ref _found) });
            }
        }

        OpeningResult result;
        try
        {
            result = _predictor.Check(seed);
        }
        catch (Exception ex)
        {
            Write(new { type = "seed_error", checked_count = checkedNow, counter, seed, message = ex.Message });
            return;
        }
        if (!_plan.Matches(result)) return;

        int foundNow = Interlocked.Increment(ref _found);
        if (foundNow <= maxResults)
        {
            Write(new
            {
                type = "hit",
                index = foundNow,
                checked_count = checkedNow,
                counter,
                seed = result.Seed,
                neow_options = result.NeowOptions,
                bones_relics = result.BonesRelics,
                bones_curse = result.BonesCurse,
                bones_curses = result.BonesCurses,
                opening_routes = result.OpeningRoutes,
                shop_relics = result.ShopRelics,
                potion_sources = result.PotionSources,
                card_source_routes = result.CardSourceRoutes,
                predicted_relic_sources = result.PredictedRelicSources,
                ancient_ids = result.Ancients,
                ancient_options = result.AncientOptions,
                event_queues = result.EventQueues,
                coarse = _plan.HasCoarseFilters,
                coarse_reasons = _plan.CoarseReasons,
            });
        }
        if (foundNow >= maxResults) cts.Cancel();
    }

    private void Write(object obj)
    {
        lock (_writeLock)
        {
            Console.WriteLine(JsonSerializer.Serialize(obj, JsonOut.Options));
            Console.Out.Flush();
        }
    }
}
