using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// EditMode tests for MoleIntentTracker edge-driven phase projection.
// Pure contract tests: GameRules public API only, no assets, deterministic.
//
// B-2 spec: tracker emits ONLY phase-edge intents (Hide/Rise/Search/Sink/Reset).
// Hit/Escape are emitted at their real event sources, NOT in the tracker.
// D-HIT/D-ESC binding decisions.
public class MoleIntentTrackerTests
{
    // --- 1-hole damero (hole 0 targets crops 0 and 1) ---
    // Two crops: when the mole escapes and steals one, the other stays alive
    // so IsGameOver stays false and Sinking→Sunk can complete.
    private static readonly int[][] SingleCandidates = { new[] { 0 } };

    private static GameRulesConfig MakeConfig(
        int[][] candidates = null,
        float matchMs = 60_000f,
        float telegraphMs = 800f,
        float upMs = 1_500f,
        float riseMs = 250f,
        float sinkMs = 250f,
        int cropCount = 1,
        float baseIntervalMs = 3_000f,
        LevelProfile level = null)
    {
        var table = candidates ?? SingleCandidates;
        return new GameRulesConfig
        {
            MatchDurationMs = matchMs,
            TelegraphDurationMs = telegraphMs,
            UpWindowMs = upMs,
            RiseDurationMs = riseMs,
            SinkDurationMs = sinkMs,
            CropCount = cropCount,
            HoleCount = table.Length,
            HoleCandidates = table,
            BaseSpawnIntervalMs = baseIntervalMs,
            IntensityCurve = p => 1f,
            Level = level,
        };
    }

    private static Func<float> Seq(params float[] values)
    {
        int i = 0;
        return () => i < values.Length ? values[i++] : 0f;
    }

    private static Func<float> Const(float v) => () => v;

    /// <summary>
    /// 1-hole telegraph config with 2 crops (hole 0 → [0,1]).
    /// Mole steals one crop on escape; the other survives so IsGameOver stays false
    /// and the Sinking→Sunk transition completes.
    /// </summary>
    private static GameRulesConfig TelegraphConfig()
    {
        var species = ScriptableObject.CreateInstance<MoleSpecies>();
        species.useTelegraph = true;
        species.baseHitsToKill = 1;

        var level = new LevelProfile
        {
            moleMods = new[] { new LevelMoleMod { species = species, weight = 1 } },
            cropConfigs = new[] { new LevelCropConfig(), new LevelCropConfig() },
            intensityStart = 1f,
            intensityEnd = 1f,
        };

        return MakeConfig(
            candidates: new[] { new[] { 0, 1 } },
            cropCount: 2,
            level: level);
    }

    // --- B-2 scenarios ---

    [Test]
    public void EscapeSequence_EmitsHideRiseSearchSinkReset()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        // Poll at t=0 before spawn: all Sunk, nothing emitted
        var initial = tracker.Poll(rules, 0f);
        Assert.That(initial.Count, Is.EqualTo(0), "No edges before spawn");

        // Spawn mole — Sunk→Telegraphing
        bool spawned = rules.TrySpawn(0f);
        Assert.That(spawned, Is.True);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Telegraphing));

        // Hide intent: Sunk→Telegraphing
        var afterSpawn = tracker.Poll(rules, 0f);
        Assert.That(afterSpawn.Count, Is.EqualTo(1));
        Assert.That(afterSpawn[0].Intent, Is.EqualTo(MoleIntent.Hide));
        Assert.That(afterSpawn[0].HoleIndex, Is.EqualTo(0));

        // Advance through telegraph (t=800ms): Telegraphing→Rising
        rules.Update(800f);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Rising));
        var riseIntents = tracker.Poll(rules, 800f);
        Assert.That(riseIntents.Count, Is.EqualTo(1));
        Assert.That(riseIntents[0].Intent, Is.EqualTo(MoleIntent.Rise));

        // Advance through rise (t=1050ms): Rising→Up
        rules.Update(1050f);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Up));
        var searchIntents = tracker.Poll(rules, 1050f);
        Assert.That(searchIntents.Count, Is.EqualTo(1));
        Assert.That(searchIntents[0].Intent, Is.EqualTo(MoleIntent.Search));

        // Advance through up window (t=2550ms): Up→Sinking (escape timeout)
        rules.Update(2550f);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Sinking));
        var sinkIntents = tracker.Poll(rules, 2550f);
        Assert.That(sinkIntents.Count, Is.EqualTo(1));
        Assert.That(sinkIntents[0].Intent, Is.EqualTo(MoleIntent.Sink));

        // Advance through sink (t=2800ms): Sinking→Sunk
        rules.Update(2800f);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Sunk));
        var resetIntents = tracker.Poll(rules, 2800f);
        Assert.That(resetIntents.Count, Is.EqualTo(1));
        Assert.That(resetIntents[0].Intent, Is.EqualTo(MoleIntent.Reset));
    }

    [Test]
    public void Ninja_NoTelegraph_EmitsRiseFirst_NoHide()
    {
        // Config without Level → no mod → TrySpawn defaults to Rising (ninja).
        var cfg = MakeConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        bool spawned = rules.TrySpawn(0f);
        Assert.That(spawned, Is.True);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Rising));

        var intents = tracker.Poll(rules, 0f);
        Assert.That(intents.Count, Is.EqualTo(1));
        Assert.That(intents[0].Intent, Is.EqualTo(MoleIntent.Rise));
    }

    [Test]
    public void HitInRising_EmitsRiseThenSink_NoSearch()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        // Spawn → Hide
        rules.TrySpawn(0f);
        tracker.Poll(rules, 0f); // consume Hide

        // Telegraph→Rising → Rise
        rules.Update(800f);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Rising));
        var riseIntents = tracker.Poll(rules, 800f);
        Assert.That(riseIntents.Count, Is.EqualTo(1));
        Assert.That(riseIntents[0].Intent, Is.EqualTo(MoleIntent.Rise));

        // Hit the mole while Rising (lethal, 1 hit to kill)
        bool hit = rules.TryHit(0, 800f);
        Assert.That(hit, Is.True);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Sinking));

        // Poll after hit: Rising→Sinking → Sink (no Search — mole never reached Up)
        var sinkIntents = tracker.Poll(rules, 800f);
        Assert.That(sinkIntents.Count, Is.EqualTo(1));
        Assert.That(sinkIntents[0].Intent, Is.EqualTo(MoleIntent.Sink));
    }

    [Test]
    public void HitInUp_EmitsSinkAfterSearch_NoHitInTracker()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        // Full lifecycle to Up
        rules.TrySpawn(0f);
        tracker.Poll(rules, 0f);   // Hide
        rules.Update(800f);
        tracker.Poll(rules, 800f); // Rise
        rules.Update(1050f);
        var searchIntents = tracker.Poll(rules, 1050f); // Search
        Assert.That(searchIntents.Count, Is.EqualTo(1));
        Assert.That(searchIntents[0].Intent, Is.EqualTo(MoleIntent.Search));
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Up));

        // Hit the mole while Up
        bool hit = rules.TryHit(0, 1050f);
        Assert.That(hit, Is.True);
        Assert.That(rules.GetPhase(0), Is.EqualTo(MolePhase.Sinking));

        // Tracker emits Sink (Up→Sinking) — Hit is from event source, not tracker
        var sinkIntents = tracker.Poll(rules, 1050f);
        Assert.That(sinkIntents.Count, Is.EqualTo(1));
        Assert.That(sinkIntents[0].Intent, Is.EqualTo(MoleIntent.Sink));
    }

    [Test]
    public void Repoll_SameTimestamp_ReturnsEmptyList()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        var first = tracker.Poll(rules, 0f);
        Assert.That(first.Count, Is.GreaterThan(0), "Should detect spawn edge");

        var second = tracker.Poll(rules, 0f);
        Assert.That(second.Count, Is.EqualTo(0),
            "Repoll at same timestamp MUST return empty — poll is idempotent");
    }

    [Test]
    public void Reset_ClearsState_AllPhasesBackToSunk()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        // Trigger some edges
        rules.TrySpawn(0f);
        var edges = tracker.Poll(rules, 0f);
        Assert.That(edges.Count, Is.GreaterThan(0), "Should have edges before reset");

        // Reset the tracker
        tracker.Reset();

        // Mole is still in Telegraphing — tracker reset to all-Sunk, so it sees Sunk→Telegraphing again
        var afterReset = tracker.Poll(rules, 0f);
        Assert.That(afterReset.Count, Is.EqualTo(1));
        Assert.That(afterReset[0].Intent, Is.EqualTo(MoleIntent.Hide));
    }

    // --- B-3: One-shot vs state ---

    [Test]
    public void Poll_NoPhaseChange_ReturnsEmpty()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        tracker.Poll(rules, 0f); // consume Hide

        // Advance 100ms — still in Telegraphing (800ms duration)
        rules.Update(100f);
        var result = tracker.Poll(rules, 100f);
        Assert.That(result.Count, Is.EqualTo(0),
            "No phase change — should return empty. Search is the only state intent and is NOT re-emitted per frame.");
    }

    // --- B-1: Payload contract ---

    [Test]
    public void Payload_HasCorrectHoleIndexIntentAndAtMs()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        var intents = tracker.Poll(rules, 42f);

        Assert.That(intents.Count, Is.EqualTo(1));
        Assert.That(intents[0].HoleIndex, Is.EqualTo(0));
        Assert.That(intents[0].Intent, Is.EqualTo(MoleIntent.Hide));
        Assert.That(intents[0].AtMs, Is.EqualTo(42f));
        Assert.That(intents[0].Species, Is.Not.Null,
            "Species should be populated from the telegraph mod");
    }

    [Test]
    public void Payload_Ninja_NoSpecies()
    {
        var cfg = MakeConfig(); // no Level → null species
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        var intents = tracker.Poll(rules, 100f);

        Assert.That(intents.Count, Is.EqualTo(1));
        Assert.That(intents[0].Species, Is.Null,
            "Ninja spawn with no mod should have null Species");
    }

    // --- Triangulation: full lifecycle verification ---

    [Test]
    public void FullEscapeLifecycle_ReturnsExactSequence()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        var all = new List<MoleIntent>();

        // Collect all intents across the full lifecycle
        Collect(tracker, rules, 0f, all);     // Hide
        Collect(tracker, rules, 800f, all);   // Rise
        Collect(tracker, rules, 1050f, all);  // Search
        Collect(tracker, rules, 2550f, all);  // Sink
        Collect(tracker, rules, 2800f, all);  // Reset

        var expected = new[]
        {
            MoleIntent.Hide, MoleIntent.Rise, MoleIntent.Search,
            MoleIntent.Sink, MoleIntent.Reset,
        };

        Assert.That(all, Is.EqualTo(expected),
            "Full escape lifecycle sequence must match spec B-2 exactly");
    }

    [Test]
    public void NinjaFullLifecycle_NoHideInSequence()
    {
        // 2-crop ninja config: mole steals one crop on escape,
        // the other survives so Sinking→Sunk can complete.
        var cfg = MakeConfig(
            candidates: new[] { new[] { 0, 1 } },
            cropCount: 2);
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        var all = new List<MoleIntent>();

        Collect(tracker, rules, 0f, all);     // Rise (Sunk→Rising)
        Collect(tracker, rules, 250f, all);   // Search (Rising→Up)
        Collect(tracker, rules, 1750f, all);  // Sink (Up→Sinking)
        Collect(tracker, rules, 2000f, all);  // Reset (Sinking→Sunk)

        var expected = new[]
        {
            MoleIntent.Rise, MoleIntent.Search, MoleIntent.Sink, MoleIntent.Reset,
        };

        Assert.That(all, Is.EqualTo(expected),
            "Ninja lifecycle must NOT include Hide");
    }

    // --- Edge cases ---

    [Test]
    public void TelegraphingToSunk_NoTransition_NoIntentEmitted()
    {
        // Telegraphing→Sunk is not a valid transition path, but if it somehow happens,
        // the tracker should not emit anything (not a defined edge).
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        rules.TrySpawn(0f);
        tracker.Poll(rules, 0f); // Hide — tracker now has Telegraphing stored

        // Manually force an undefined transition — but we can't do that through public API.
        // Instead, verify that only defined edges produce intents by checking that a
        // Telegraphing→Telegraphing poll returns empty (no edge).
        var empty = tracker.Poll(rules, 0f);
        Assert.That(empty.Count, Is.EqualTo(0),
            "No phase change between polls — no intent should be emitted");
    }

    [Test]
    public void Constructed_AllHolesStartAsSunk()
    {
        // Verify Reset() is implied by constructor: initial poll with all-Sunk produces nothing
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, Const(0.5f));
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        var empty = tracker.Poll(rules, 0f);
        Assert.That(empty.Count, Is.EqualTo(0),
            "Constructor should initialize all phases to Sunk — no edges on first poll");
    }

    // --- Helpers ---

    private static void Collect(MoleIntentTracker tracker, GameRules rules, float nowMs, List<MoleIntent> target)
    {
        rules.Update(nowMs);
        foreach (var ev in tracker.Poll(rules, nowMs))
            target.Add(ev.Intent);
    }
}
