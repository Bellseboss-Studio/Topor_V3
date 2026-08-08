using System;
using System.Collections.Generic;

// Pure mole rules — NO UnityEngine, NO Time, NO AnimationCurve.
// Time arrives as absolute nowMs; randomness arrives via injected Func<float> (0..1).
// Testable in EditMode with exact timestamps and scripted stubs.
public enum MolePhase { Sunk, Rising, Up, Sinking }

public sealed class GameRulesConfig
{
    public float MatchDurationMs = 60_000f;
    public float UpWindowMs = 1_500f;
    public float RiseDurationMs = 250f;
    public float SinkDurationMs = 250f;
    public int InitialLives = 3;
    public int HoleCount = 3;
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

    public int Score { get; private set; }
    public int Lives { get; private set; }
    public bool IsGameOver { get; private set; }

    public GameRules(GameRulesConfig cfg, Func<float> randomUnit)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _randomUnit = randomUnit ?? (() => 0f);
        _phases = new MolePhase[cfg.HoleCount];
        _phaseStartMs = new float[cfg.HoleCount];
        _wasHit = new bool[cfg.HoleCount];
        StartMatch();
    }

    public void StartMatch()
    {
        Score = 0;
        Lives = _cfg.InitialLives;
        IsGameOver = false;
        for (int i = 0; i < _cfg.HoleCount; i++)
        {
            _phases[i] = MolePhase.Sunk;
            _phaseStartMs[i] = 0f;
            _wasHit[i] = false;
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

    public int UpCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _cfg.HoleCount; i++)
                if (_phases[i] == MolePhase.Up) n++;
            return n;
        }
    }

    public MolePhase GetPhase(int i) => _phases[i];
    public float GetPhaseStartMs(int i) => _phaseStartMs[i];
    public bool WasHit(int i) => _wasHit[i];
    public float RiseDurationMs => _cfg.RiseDurationMs;
    public float SinkDurationMs => _cfg.SinkDurationMs;

    public void Update(float nowMs)
    {
        if (IsGameOver) return; // frozen: no escapes, hits, or spawns after game over

        for (int i = 0; i < _cfg.HoleCount; i++)
        {
            float elapsed = nowMs - _phaseStartMs[i];
            switch (_phases[i])
            {
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
                        _phases[i] = MolePhase.Sinking;      // escaped
                        _phaseStartMs[i] += _cfg.UpWindowMs;
                        Lives = Math.Max(0, Lives - 1);      // ate a fruit
                        if (Lives == 0) IsGameOver = true;
                    }
                    break;

                case MolePhase.Sinking:
                    if (elapsed >= _cfg.SinkDurationMs)
                    {
                        _phases[i] = MolePhase.Sunk;
                        _phaseStartMs[i] += _cfg.SinkDurationMs;
                    }
                    break;
            }
        }
    }

    public bool TryHit(int moleIndex, float nowMs)
    {
        if (IsGameOver || moleIndex < 0 || moleIndex >= _cfg.HoleCount) return false;
        if (_phases[moleIndex] != MolePhase.Up) return false;
        Score++;
        _wasHit[moleIndex] = true;
        _phases[moleIndex] = MolePhase.Sinking; // immediate sink, never hittable again this lifecycle
        _phaseStartMs[moleIndex] = nowMs;
        return true;
    }

    public bool TrySpawn(float nowMs)
    {
        if (IsGameOver) return false;
        if (UpCount >= MaxConcurrentAt(Progress01(nowMs))) return false;

        var sunk = new List<int>();
        for (int i = 0; i < _cfg.HoleCount; i++)
            if (_phases[i] == MolePhase.Sunk) sunk.Add(i);
        if (sunk.Count == 0) return false;

        int pick = (int)(_randomUnit() * sunk.Count);
        if (pick >= sunk.Count) pick = sunk.Count - 1;
        int slot = sunk[pick];

        _phases[slot] = MolePhase.Rising;
        _phaseStartMs[slot] = nowMs;
        _wasHit[slot] = false; // fresh lifecycle
        return true;
    }
}
