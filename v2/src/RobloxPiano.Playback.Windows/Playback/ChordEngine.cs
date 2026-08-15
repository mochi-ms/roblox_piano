using System.Diagnostics;
using RobloxPiano.Core.Music;
using RobloxPiano.Core.Piano;

namespace RobloxPiano.Playback.Windows.Playback;

public class ChordEngine
{
    private readonly KeyStateManager _keyState;
    private readonly RobloxPianoMapper _mapper;
    private readonly ConflictPolicy _conflictPolicy;
    private readonly double _conflictDelayMs;
    private readonly double _defaultHoldDurationMs;
    private readonly double _modifierSettleMs;
    private readonly double _randomizationMs;

    public KeyStateManager KeyState => _keyState;
    public RobloxPianoMapper Mapper => _mapper;
    public ConflictPolicy ConflictPolicy => _conflictPolicy;

    public ChordEngine(
        KeyStateManager keyState,
        RobloxPianoMapper mapper,
        ConflictPolicy conflictPolicy = ConflictPolicy.MicroArpeggio,
        double conflictDelayMs = 8.0,
        double defaultHoldDurationMs = 30.0,
        double modifierSettleMs = 2.0,
        double randomizationMs = 0.0)
    {
        _keyState = keyState;
        _mapper = mapper;
        _conflictPolicy = conflictPolicy;
        _conflictDelayMs = conflictDelayMs;
        _defaultHoldDurationMs = defaultHoldDurationMs;
        _modifierSettleMs = modifierSettleMs;
        _randomizationMs = randomizationMs;
    }

    public ChordPlaybackResult PlayChordNotes(
        IReadOnlyList<NoteEvent> notes,
        double? holdDurationMs = null,
        int transpose = 0,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (notes == null || notes.Count == 0)
        {
            return new ChordPlaybackResult(notes?.Count ?? 0, 0, 0, 0, Array.Empty<int>());
        }

        int requestedCount = notes.Count;
        double holdMs = holdDurationMs ?? _defaultHoldDurationMs;
        if (holdMs < 1.0) holdMs = 1.0;

        // 1. Map notes to physical keys with transpose
        var mappedKeys = new List<(NoteEvent Note, KeyMapping Mapping)>();
        int skippedUnmapped = 0;

        foreach (var n in notes)
        {
            int effectivePitch = n.Pitch + transpose;
            var km = _mapper.MapPitch(effectivePitch);
            if (km != null)
            {
                mappedKeys.Add((n, km));
            }
            else
            {
                skippedUnmapped++;
            }
        }

        if (mappedKeys.Count == 0)
        {
            return new ChordPlaybackResult(requestedCount, 0, skippedUnmapped, 0, Array.Empty<int>());
        }

        // 2. Check for same physical key conflicts
        var physicalKeyMap = new Dictionary<string, List<KeyMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, km) in mappedKeys)
        {
            var pk = km.PhysicalKey.ToLowerInvariant();
            if (!physicalKeyMap.TryGetValue(pk, out var list))
            {
                list = new List<KeyMapping>();
                physicalKeyMap[pk] = list;
            }
            list.Add(km);
        }

        bool hasPhysicalConflicts = physicalKeyMap.Values.Any(list => list.Count > 1);
        int skippedConflicts = 0;

        if (hasPhysicalConflicts && _conflictPolicy == ConflictPolicy.SkipConflicted)
        {
            // Keep only highest pitch note for each physical key
            var filteredMapped = new List<(NoteEvent Note, KeyMapping Mapping)>();
            var seenPk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in mappedKeys.OrderByDescending(x => x.Note.Pitch))
            {
                var pk = item.Mapping.PhysicalKey.ToLowerInvariant();
                if (seenPk.Add(pk))
                {
                    filteredMapped.Add(item);
                }
                else
                {
                    skippedConflicts++;
                }
            }
            mappedKeys = filteredMapped;
        }

        var playedPitches = mappedKeys.Select(m => m.Note.Pitch + transpose).ToList();

        // 3. Group by modifier sets
        var modifierGroups = new Dictionary<string, (IReadOnlySet<string> Modifiers, List<KeyMapping> Keys)>();

        foreach (var (_, km) in mappedKeys)
        {
            var keyStr = string.Join("+", km.Modifiers.OrderBy(m => m));
            if (!modifierGroups.TryGetValue(keyStr, out var group))
            {
                group = (km.Modifiers, new List<KeyMapping>());
                modifierGroups[keyStr] = group;
            }
            group.Keys.Add(km);
        }

        // Sort groups: empty modifiers first, then by modifier count and names to prevent modifier bleeding
        var sortedGroups = modifierGroups.Values
            .OrderBy(g => g.Modifiers.Count)
            .ThenBy(g => string.Join("+", g.Modifiers.OrderBy(m => m)))
            .ToList();

        bool isMultiGroup = sortedGroups.Count > 1 || hasPhysicalConflicts;

        // Local tracking for guaranteed balanced cleanup in try/finally
        var locallyPressedKeys = new List<string>();
        var locallyPressedModifiers = new List<string>();

        try
        {
            if (isMultiGroup)
            {
                // Micro-Arpeggio execution
                for (int i = 0; i < sortedGroups.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var (mods, kms) = sortedGroups[i];

                    // Set modifiers for this group
                    foreach (var mod in mods)
                    {
                        ct.ThrowIfCancellationRequested();
                        _keyState.SetModifier(mod, true);
                        locallyPressedModifiers.Add(mod);
                    }
                    if (mods.Count > 0 && _modifierSettleMs > 0)
                    {
                        PreciseDelay(_modifierSettleMs, ct);
                    }

                    // Press physical keys
                    foreach (var km in kms)
                    {
                        ct.ThrowIfCancellationRequested();
                        _keyState.PressPhysicalKey(km.PhysicalKey);
                        locallyPressedKeys.Add(km.PhysicalKey);
                    }

                    // Hold duration: conflict delay for intermediate groups, full duration for the last group
                    double delay = (i < sortedGroups.Count - 1) ? _conflictDelayMs : holdMs;
                    PreciseDelay(delay, ct);

                    // Release physical keys
                    foreach (var km in kms)
                    {
                        _keyState.ReleasePhysicalKey(km.PhysicalKey);
                        locallyPressedKeys.Remove(km.PhysicalKey);
                    }

                    // Release modifiers
                    foreach (var mod in mods)
                    {
                        _keyState.SetModifier(mod, false);
                        locallyPressedModifiers.Remove(mod);
                    }
                    if (mods.Count > 0 && _modifierSettleMs > 0)
                    {
                        PreciseDelay(_modifierSettleMs, ct);
                    }
                }
            }
            else
            {
                // Single modifier group standard execution
                var (mods, kms) = sortedGroups[0];

                foreach (var mod in mods)
                {
                    ct.ThrowIfCancellationRequested();
                    _keyState.SetModifier(mod, true);
                    locallyPressedModifiers.Add(mod);
                }
                if (mods.Count > 0 && _modifierSettleMs > 0)
                {
                    PreciseDelay(_modifierSettleMs, ct);
                }

                foreach (var km in kms)
                {
                    ct.ThrowIfCancellationRequested();
                    _keyState.PressPhysicalKey(km.PhysicalKey);
                    locallyPressedKeys.Add(km.PhysicalKey);
                }

                PreciseDelay(holdMs, ct);

                foreach (var km in kms)
                {
                    _keyState.ReleasePhysicalKey(km.PhysicalKey);
                    locallyPressedKeys.Remove(km.PhysicalKey);
                }

                foreach (var mod in mods)
                {
                    _keyState.SetModifier(mod, false);
                    locallyPressedModifiers.Remove(mod);
                }
            }
        }
        finally
        {
            // Defensive cleanup: always release any remaining keys/modifiers in reverse order
            foreach (var pk in locallyPressedKeys.ToList())
            {
                try
                {
                    _keyState.ReleasePhysicalKey(pk);
                }
                catch { }
            }
            locallyPressedKeys.Clear();

            foreach (var mod in locallyPressedModifiers.ToList())
            {
                try
                {
                    _keyState.SetModifier(mod, false);
                }
                catch { }
            }
            locallyPressedModifiers.Clear();
        }

        return new ChordPlaybackResult(
            requestedCount,
            mappedKeys.Count,
            skippedUnmapped,
            skippedConflicts,
            playedPitches
        );
    }

    private static void PreciseDelay(double milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0) return;
        ct.ThrowIfCancellationRequested();

        long start = Stopwatch.GetTimestamp();
        long targetTicks = (long)(milliseconds * Stopwatch.Frequency / 1000.0);

        if (milliseconds > 2.0)
        {
            int sleepMs = (int)(milliseconds - 1.5);
            if (sleepMs > 0)
            {
                if (ct.WaitHandle.WaitOne(sleepMs))
                {
                    ct.ThrowIfCancellationRequested();
                }
            }
        }

        while (Stopwatch.GetTimestamp() - start < targetTicks)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Yield();
        }
    }
}
