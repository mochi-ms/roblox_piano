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

    public void PlayChordNotes(
        IReadOnlyList<NoteEvent> notes,
        double? holdDurationMs = null,
        int transpose = 0,
        CancellationToken ct = default)
    {
        if (notes == null || notes.Count == 0 || ct.IsCancellationRequested)
        {
            return;
        }

        double holdMs = holdDurationMs ?? _defaultHoldDurationMs;
        if (holdMs < 1.0) holdMs = 1.0;

        // 1. Map notes to physical keys with transpose
        var mappedKeys = new List<(NoteEvent Note, KeyMapping Mapping)>();
        foreach (var n in notes)
        {
            int effectivePitch = n.Pitch + transpose;
            var km = _mapper.MapPitch(effectivePitch);
            if (km != null)
            {
                mappedKeys.Add((n, km));
            }
        }

        if (mappedKeys.Count == 0)
        {
            return;
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
            }
            mappedKeys = filteredMapped;
        }

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

        if (isMultiGroup)
        {
            // Micro-Arpeggio execution
            for (int i = 0; i < sortedGroups.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var (mods, kms) = sortedGroups[i];

                // Set modifiers for this group
                foreach (var mod in mods)
                {
                    _keyState.SetModifier(mod, true);
                }
                if (mods.Count > 0 && _modifierSettleMs > 0)
                {
                    PreciseDelay(_modifierSettleMs, ct);
                }

                // Press physical keys
                foreach (var km in kms)
                {
                    _keyState.PressPhysicalKey(km.PhysicalKey);
                }

                // Hold duration: conflict delay for intermediate groups, full duration for the last group
                double delay = (i < sortedGroups.Count - 1) ? _conflictDelayMs : holdMs;
                PreciseDelay(delay, ct);

                // Release physical keys
                foreach (var km in kms)
                {
                    _keyState.ReleasePhysicalKey(km.PhysicalKey);
                }

                // Release modifiers
                foreach (var mod in mods)
                {
                    _keyState.SetModifier(mod, false);
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
                _keyState.SetModifier(mod, true);
            }
            if (mods.Count > 0 && _modifierSettleMs > 0)
            {
                PreciseDelay(_modifierSettleMs, ct);
            }

            foreach (var km in kms)
            {
                _keyState.PressPhysicalKey(km.PhysicalKey);
            }

            PreciseDelay(holdMs, ct);

            foreach (var km in kms)
            {
                _keyState.ReleasePhysicalKey(km.PhysicalKey);
            }

            foreach (var mod in mods)
            {
                _keyState.SetModifier(mod, false);
            }
        }
    }

    private static void PreciseDelay(double milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0 || ct.IsCancellationRequested) return;

        long start = Stopwatch.GetTimestamp();
        long targetTicks = (long)(milliseconds * Stopwatch.Frequency / 1000.0);

        if (milliseconds > 5.0)
        {
            int sleepMs = (int)(milliseconds - 3.0);
            if (sleepMs > 0)
            {
                try
                {
                    Thread.Sleep(sleepMs);
                }
                catch
                {
                    // Interrupted or canceled
                }
            }
        }

        while (Stopwatch.GetTimestamp() - start < targetTicks)
        {
            if (ct.IsCancellationRequested) break;
            Thread.Yield();
        }
    }
}
