using System;
using System.Collections.Generic;

// Pure mole rules — NO UnityEngine, NO Time, NO AnimationCurve.
// Time arrives as absolute nowMs; randomness arrives via injected Func<float> (0..1).
// Testable in EditMode with exact timestamps and scripted stubs.
//
// grid-v2: config-driven HOLE ADJACENCY (hole -> 0..4 candidate crops; row index = hole
// index), mole DECIDES its target uniformly among ALIVE candidates at spawn (two rolls:
// roll1 -> hole among eligible-sunk, roll2 -> target among that hole's alive candidates),
// TryHit window = Rising OR Up, WIN by survival (time-up gate closes BEFORE escapes/hits/
// spawns at the boundary tick), dead-target escape is a strict no-op, crops-as-lives,
// and a pollable escape queue (DrainEscapes) so presentation never gates rule timing.
public enum MolePhase { Sunk, Telegraphing, Rising, Up, Sinking }

// Consequence event pushed when a mole escapes: which mole stole which crop, when.
// Rules never wait on visuals — the event lands at the same Update tick as the steal.
public readonly struct CropStealEvent
{
    public readonly int MoleIndex;
    public readonly int CropIndex;
    public readonly float AtMs;

    public CropStealEvent(int moleIndex, int cropIndex, float atMs)
    {
        MoleIndex = moleIndex;
        CropIndex = cropIndex;
        AtMs = atMs;
    }
}

public sealed class GameRulesConfig
{
    public float MatchDurationMs = 60_000f;
    public float TelegraphDurationMs = 800f;   // announce window before Rising (D4)
    public float UpWindowMs = 1_500f;
    public float RiseDurationMs = 250f;
    public float SinkDurationMs = 250f;
    public int CropCount = 6;                  // crops ARE lives (D3)
    public int HoleCount = 17;                 // grid-v2 RELAYOUT: 17-hole damero default
    // grid-v2 adjacency (A1): hole index -> candidate crop indexes (0..4). Pure C#,
    // no UnityEngine. Row index == hole index == Mole n binding contract (P7).
    public int[][] HoleCandidates;
    public float BaseSpawnIntervalMs = 3_000f;
    // progress (0..1) -> intensity (max concurrent moles). Single source of truth.
    public Func<float, float> IntensityCurve = p => 1f;
}

public sealed class GameRules
{
    private readonly GameRulesConfig _cfg;
    private readonly Func<float> _randomUnit;
    private readonly MolePhase[] _phases;
    private readonly float[] _phaseStartMs;
    private readonly bool[] _wasHit;
    private readonly int[] _threatenedCrop; // per hole: crop index the mole will steal, or -1
    private readonly bool[] _cropAlive;
    private readonly List<CropStealEvent> _escapeQueue = new List<CropStealEvent>();

    public int Score { get; private set; }
    public bool IsGameOver { get; private set; }
    private bool _levelComplete; // distinct terminal state: survived the duration (A4)

    // Lives = current alive crop count — single source of truth (D3).
    public int Lives
    {
        get
        {
            int n = 0;
            for (int c = 0; c < _cfg.CropCount; c++)
                if (_cropAlive[c]) n++;
            return n;
        }
    }

    public int CropCount => _cfg.CropCount;
    public int HoleCount => _cfg.HoleCount;
    public float TelegraphDurationMs => _cfg.TelegraphDurationMs;

    // WIN by survival: gate closed AND not GameOver. Never true under GameOver (A4-2).
    public bool IsWin => _levelComplete && !IsGameOver;

    public GameRules(GameRulesConfig cfg, Func<float> randomUnit)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _randomUnit = randomUnit ?? (() => 0f);
        _phases = new MolePhase[cfg.HoleCount];
        _phaseStartMs = new float[cfg.HoleCount];
        _wasHit = new bool[cfg.HoleCount];
        _threatenedCrop = new int[cfg.HoleCount];
        _cropAlive = new bool[cfg.CropCount];
        StartMatch();
    }

    public void StartMatch()
    {
        Score = 0;
        IsGameOver = false;
        _levelComplete = false;
        _escapeQueue.Clear();
        for (int c = 0; c < _cfg.CropCount; c++) _cropAlive[c] = true;
        for (int i = 0; i < _cfg.HoleCount; i++)
        {
            _phases[i] = MolePhase.Sunk;
            _phaseStartMs[i] = 0f;
            _wasHit[i] = false;
            _threatenedCrop[i] = -1;
        }
    }

    public float Progress01(float nowMs)
    {
        float p = nowMs / Math.Max(1f, _cfg.MatchDurationMs);
        return Math.Clamp(p, 0f, 1f);
    }

    public int MaxConcurrentAt(float progress01)
    {
        float p = Math.Clamp(progress01, 0f, 1f);
        return (int)Math.Ceiling(_cfg.IntensityCurve(p));
    }

    public float SpawnIntervalMs(float nowMs)
    {
        float intensity = Math.Max(1f, MaxConcurrentAt(Progress01(nowMs)));
        return _cfg.BaseSpawnIntervalMs / intensity;
    }

    // Active moles = any non-Sunk phase (Telegraphing/Rising/Up/Sinking count): a
    // telegraphing mole already occupies its hole, so it caps pre-announce pile-up (D5).
    public int UpCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _cfg.HoleCount; i++)
                if (_phases[i] != MolePhase.Sunk) n++;
            return n;
        }
    }

    public MolePhase GetPhase(int i) => _phases[i];
    public float GetPhaseStartMs(int i) => _phaseStartMs[i];
    public bool WasHit(int i) => _wasHit[i];
    public float RiseDurationMs => _cfg.RiseDurationMs;
    public float SinkDurationMs => _cfg.SinkDurationMs;

    public bool IsCropAlive(int cropIndex) =>
        cropIndex >= 0 && cropIndex < _cfg.CropCount && _cropAlive[cropIndex];

    // grid-v2 adjacency seam (A1): the configured row for a hole — crop indexes it may
    // threaten. Empty for perimeter-only holes with no crop neighbor. Controller never
    // reads this; only TrySpawn consumes candidates internally (P2).
    public int[] CandidatesForHole(int holeIndex)
    {
        if (_cfg.HoleCandidates == null ||
            holeIndex < 0 || holeIndex >= _cfg.HoleCandidates.Length)
            return Array.Empty<int>();
        return _cfg.HoleCandidates[holeIndex];
    }

    // Crop currently threatened by the mole in this hole, or -1 if none.
    public int ThreatenedCrop(int moleIndex) => _threatenedCrop[moleIndex];

    // Pollable escape queue: returns queued steals then clears. Controller drains
    // once per frame — rules never block on visuals (D2).
    public List<CropStealEvent> DrainEscapes()
    {
        var events = new List<CropStealEvent>(_escapeQueue);
        _escapeQueue.Clear();
        return events;
    }

    // HUD helper (A5): remaining ms -> "MM:SS", clamped >= 0. Pure, unit-testable.
    public static string FormatRemainingMs(float remainingMs)
    {
        int totalSeconds = (int)(Math.Max(0f, remainingMs) / 1000f);
        return string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
    }

    public void Update(float nowMs)
    {
        if (IsGameOver || _levelComplete) return; // frozen after terminal states

        // P5 gate-first: the boundary tick belongs to the player — win closes the gate
        // BEFORE any escape/hit/steal runs (A4-3), and state freezes here.
        if (nowMs >= _cfg.MatchDurationMs)
        {
            _levelComplete = true;
            return;
        }

        for (int i = 0; i < _cfg.HoleCount; i++)
        {
            float elapsed = nowMs - _phaseStartMs[i];
            switch (_phases[i])
            {
                case MolePhase.Telegraphing:
                    if (elapsed >= _cfg.TelegraphDurationMs)
                    {
                        _phases[i] = MolePhase.Rising;
                        _phaseStartMs[i] += _cfg.TelegraphDurationMs;
                    }
                    break;

                case MolePhase.Rising:
                    if (elapsed >= _cfg.RiseDurationMs)
                    {
                        _phases[i] = MolePhase.Up;
                        _phaseStartMs[i] += _cfg.RiseDurationMs;
                    }
                    break;

                case MolePhase.Up:
                    if (elapsed >= _cfg.UpWindowMs)
                    {
                        _phases[i] = MolePhase.Sinking;       // escaped
                        _phaseStartMs[i] += _cfg.UpWindowMs;
                        StealBoundCrop(i, nowMs);            // consequence lands same tick
                    }
                    break;

                case MolePhase.Sinking:
                    if (elapsed >= _cfg.SinkDurationMs)
                    {
                        _phases[i] = MolePhase.Sunk;
                        _phaseStartMs[i] += _cfg.SinkDurationMs;
                        _threatenedCrop[i] = -1;             // release hole binding
                    }
                    break;
            }
        }
    }

    private void StealBoundCrop(int moleIndex, float nowMs)
    {
        int crop = _threatenedCrop[moleIndex];
        if (crop < 0 || crop >= _cfg.CropCount || !_cropAlive[crop]) return; // dead-target no-op (P4)
        _cropAlive[crop] = false;
        _escapeQueue.Add(new CropStealEvent(moleIndex, crop, nowMs));
        if (Lives == 0) IsGameOver = true;
    }

    public bool TryHit(int moleIndex, float nowMs)
    {
        if (IsGameOver || _levelComplete) return false; // post-gate hits ignored (A4-1)
        if (moleIndex < 0 || moleIndex >= _cfg.HoleCount) return false;
        // M2/P6: hittable during Rising AND Up; never Telegraphing/Sinking/Sunk.
        if (_phases[moleIndex] != MolePhase.Rising &&
            _phases[moleIndex] != MolePhase.Up) return false;
        Score++;
        _wasHit[moleIndex] = true;
        _phases[moleIndex] = MolePhase.Sinking; // immediate sink, never hittable again
        _phaseStartMs[moleIndex] = nowMs;
        return true;
    }

    public bool TrySpawn(float nowMs)
    {
        if (IsGameOver || _levelComplete) return false;
        if (UpCount >= MaxConcurrentAt(Progress01(nowMs))) return false;

        // Eligible holes: sunk AND holding >= 1 alive candidate (A2/A3). Dead candidates
        // are excluded BEFORE selection; a hole with no alive candidate cannot raise a mole.
        var eligible = new List<int>();
        for (int i = 0; i < _cfg.HoleCount; i++)
        {
            if (_phases[i] != MolePhase.Sunk) continue;
            int[] row = CandidatesForHole(i);
            for (int k = 0; k < row.Length; k++)
                if (IsCropAlive(row[k])) { eligible.Add(i); break; }
        }
        if (eligible.Count == 0) return false; // A3: no alive candidate -> no mole rises

        // P3 roll 1: uniform over eligible-sunk holes.
        int hole = eligible[RollIndex(_randomUnit(), eligible.Count)];

        // P3 roll 2: uniform over ALIVE candidates of the chosen hole (dead excluded).
        int[] candidates = CandidatesForHole(hole);
        var alive = new List<int>();
        for (int k = 0; k < candidates.Length; k++)
            if (IsCropAlive(candidates[k])) alive.Add(candidates[k]);
        int target = alive[RollIndex(_randomUnit(), alive.Count)];

        _phases[hole] = MolePhase.Telegraphing; // announce first; rise after telegraph
        _phaseStartMs[hole] = nowMs;
        _wasHit[hole] = false;                 // fresh lifecycle
        _threatenedCrop[hole] = target;        // mole decided its target (A2)
        return true;
    }

    // roll*len -> index, clamped against float precision (0.99*10 = 9.899999 -> 9).
    private static int RollIndex(float roll, int count)
    {
        if (count <= 0) return 0;
        int idx = (int)(roll * count);
        return idx >= count ? count - 1 : idx;
    }
}
