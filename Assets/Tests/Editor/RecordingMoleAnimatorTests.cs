using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// EditMode integration tests: RecordingMoleAnimator records virtual method calls.
// Validates the full wiring: GameRules → MoleIntentTracker → Mole.OnIntent/Mole.OnRawHit/Mole.OnRawEscape → MoleAnimator.
//
// TDD: This file references Mole.animator, Mole.OnIntent, Mole.OnRawHit, Mole.OnRawEscape
// which do NOT exist yet → RED (compilation failure).
//
// Spec B-6/B-7/B-9 scenarios: canonical sequences, Hit/Escape ordering, multi-hit, simultaneous, reset, no duplicates.

// Recording subclass: overrides ALL virtual callbacks to record invocation order.
public class RecordingMoleAnimator : MoleAnimator
{
    public List<string> Calls = new List<string>();
    public List<CropStealEvent> EscapeEvents = new List<CropStealEvent>();
    public List<MoleSpecies> SpeciesCalls = new List<MoleSpecies>();

    public void Clear() { Calls.Clear(); EscapeEvents.Clear(); SpeciesCalls.Clear(); }

    protected override void Awake()
    {
        // No-op: skip default sprite/transform auto-resolution (no real assets in EditMode).
    }

    public override void OnHide()     { Calls.Add("OnHide"); }
    public override void OnRise()     { Calls.Add("OnRise"); }
    public override void OnSearch()   { Calls.Add("OnSearch"); }
    public override void OnSink()     { Calls.Add("OnSink"); }
    public override void OnHit()      { Calls.Add("OnHit"); }
    public override void OnEscape(CropStealEvent ev)
    {
        EscapeEvents.Add(ev);
        Calls.Add("OnEscape");
    }
    public override void OnReset()    { Calls.Add("OnReset"); }
    public override void SetSpecies(MoleSpecies species)
    {
        SpeciesCalls.Add(species);
        Calls.Add("SetSpecies");
    }
}

public class RecordingMoleAnimatorTests
{
    // --- 1-hole config with Level (telegraph + species) and 2 crops ---
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

        return new GameRulesConfig
        {
            MatchDurationMs = 60_000f,
            TelegraphDurationMs = 800f,
            UpWindowMs = 1_500f,
            RiseDurationMs = 250f,
            SinkDurationMs = 250f,
            CropCount = 2,
            HoleCount = 1,
            HoleCandidates = new[] { new[] { 0, 1 } },
            BaseSpawnIntervalMs = 3_000f,
            IntensityCurve = p => 1f,
            Level = level,
        };
    }

    // --- Helper: creates a Mole GO with RecordingMoleAnimator, wires animator via reflection ---
    private static (GameObject go, Mole mole, RecordingMoleAnimator rec) CreateMoleWithRecorder()
    {
        var go = new GameObject("TestMole");
        var mole = go.AddComponent<Mole>();
        // Mole.Awake runs in EditMode — spriteRenderer/spriteTransform will be null, those are fine.
        var rec = go.AddComponent<RecordingMoleAnimator>();
        rec.Clear();

        // Wire animator into Mole's private field via reflection (simulates Inspector assignment).
        var field = typeof(Mole).GetField("animator", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null, "Mole.animator field must exist");
        field.SetValue(mole, rec);

        return (go, mole, rec);
    }

    // --- Helper: drives one full frame exactly like GameController (spec B-7 frame ordering) ---
    private static void DriveFrame(
        GameRules rules,
        MoleIntentTracker tracker,
        Mole[] moles,
        float nowMs,
        int[] hitHoles = null)
    {
        // 1. Input phase: OnTap → TryHit → OnRawHit (before Update)
        if (hitHoles != null)
        {
            for (int h = 0; h < hitHoles.Length; h++)
            {
                int hole = hitHoles[h];
                if (rules.TryHit(hole, nowMs) && moles[hole] != null)
                    moles[hole].OnRawHit();
            }
        }

        // 2. Update rules (phase transitions)
        rules.Update(nowMs);

        // 3. DrainEscapes → OnRawEscape
        var escapes = rules.DrainEscapes();
        for (int e = 0; e < escapes.Count; e++)
        {
            var ev = escapes[e];
            if (ev.MoleIndex >= 0 && ev.MoleIndex < moles.Length && moles[ev.MoleIndex] != null)
                moles[ev.MoleIndex].OnRawEscape(ev);
        }

        // 4. Poll tracker → OnIntent dispatch BEFORE TrySpawn
        //    (so Sinking→Sunk Reset is captured before a new spawn overwrites the hole)
        var intents = tracker.Poll(rules, nowMs);
        foreach (var ev in intents)
        {
            if (ev.HoleIndex >= 0 && ev.HoleIndex < moles.Length && moles[ev.HoleIndex] != null)
                moles[ev.HoleIndex].OnIntent(ev);
        }

        // 5. TrySpawn (after Poll — new mole edges detected next frame)
        rules.TrySpawn(nowMs);
    }

    [TearDown]
    public void Teardown()
    {
        // Clean up GameObjects created in tests
        var gos = GameObject.FindObjectsOfType<GameObject>();
        foreach (var go in gos)
        {
            if (go.name.StartsWith("TestMole"))
                GameObject.DestroyImmediate(go);
        }
    }

    // --- B-9: Canonical sequences ---

    [Test]
    public void FullEscape_HideRiseSearchEscapeSinkReset_ExactOrdering()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        // Spawn: Sunk→Telegraphing (needs a frame to process)
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f); // Hide
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "SetSpecies", "OnHide" }),
            "Spawn should call SetSpecies then OnHide");

        // Advance to Rising
        rec.Clear();
        DriveFrame(rules, tracker, moles, 800f); // Rise
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnRise" }));

        // Advance to Up
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1050f); // Search
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnSearch" }));

        // Advance to Sinking (escape timeout)
        rec.Clear();
        DriveFrame(rules, tracker, moles, 2550f);
        // DrainEscapes produces OnEscape, tracker produces OnSink
        // Order: DrainEscapes (step 3) BEFORE Poll (step 5) → OnEscape before OnSink
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnEscape", "OnSink" }),
            "Escape must fire BEFORE Sink in the same frame (frame ordering B-7)");
        Assert.That(rec.EscapeEvents.Count, Is.EqualTo(1));
        Assert.That(rec.EscapeEvents[0].MoleIndex, Is.EqualTo(0));

        // Advance to Sunk
        rec.Clear();
        DriveFrame(rules, tracker, moles, 2800f); // Reset
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnReset" }));

        GameObject.DestroyImmediate(go);
    }

    [Test]
    public void HitInUp_HideRiseSearchHitSinkReset_ExactOrdering()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        // Spawn + lifecycle to Up
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f);    // Hide + SetSpecies
        DriveFrame(rules, tracker, moles, 800f);  // Rise
        DriveFrame(rules, tracker, moles, 1050f); // Search

        // Hit at t=1100: mole is Up, lethal hit → OnHit + OnSink
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1100f, hitHoles: new[] { 0 });
        // OnRawHit (step 1, before Update) → OnSink (step 5, after mutations)
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnHit", "OnSink" }),
            "Hit must fire BEFORE Sink in the same frame (input phase before Update)");

        // Advance to Sunk
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1350f); // Reset
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnReset" }));

        GameObject.DestroyImmediate(go);
    }

    [Test]
    public void HitInRising_HideRiseHitSinkReset_NoSearch()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        // Spawn + advance to Rising
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f);   // Hide + SetSpecies
        DriveFrame(rules, tracker, moles, 800f); // Rise

        // Hit while Rising (lethal, 1 hit to kill) → OnHit + OnSink (no Search)
        rec.Clear();
        DriveFrame(rules, tracker, moles, 800f, hitHoles: new[] { 0 });
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnHit", "OnSink" }),
            "Hit in Rising should produce OnHit + OnSink, NO OnSearch");

        // Advance to Sunk
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1050f); // Reset
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnReset" }));

        GameObject.DestroyImmediate(go);
    }

    [Test]
    public void Ninja_RiseSearchSinkReset_NoHide()
    {
        // Config without Level → no mod → TrySpawn defaults to Rising (ninja).
        var cfg = new GameRulesConfig
        {
            MatchDurationMs = 60_000f,
            TelegraphDurationMs = 800f,
            UpWindowMs = 1_500f,
            RiseDurationMs = 250f,
            SinkDurationMs = 250f,
            CropCount = 2,
            HoleCount = 1,
            HoleCandidates = new[] { new[] { 0, 1 } },
            BaseSpawnIntervalMs = 3_000f,
            IntensityCurve = p => 1f,
        };

        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        // Spawn ninja: Sunk→Rising directly (no telegraph)
        rec.Clear();
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f); // Rise
        Assert.That(rec.Calls, Does.Not.Contain("OnHide"),
            "Ninja spawn must NOT call OnHide — mole goes Sunk→Rising directly");
        Assert.That(rec.Calls, Does.Contain("OnRise"),
            "Ninja spawn must call OnRise");

        // Advance to Up → Search
        rec.Clear();
        DriveFrame(rules, tracker, moles, 250f);
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnSearch" }));

        // Escape timeout → Sink + Reset
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1750f);
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnEscape", "OnSink" }));

        rec.Clear();
        DriveFrame(rules, tracker, moles, 2000f);
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnReset" }));

        GameObject.DestroyImmediate(go);
    }

    // --- Simultaneous events ---

    [Test]
    public void Simultaneous_HitAndEscape_DifferentHoles_OrderedCorrectly()
    {
        // 3-hole config: hole 0 gets hit, hole 1 escapes, hole 2 idle
        var species = ScriptableObject.CreateInstance<MoleSpecies>();
        species.useTelegraph = true;
        species.baseHitsToKill = 1;

        var level = new LevelProfile
        {
            moleMods = new[] { new LevelMoleMod { species = species, weight = 1 } },
            cropConfigs = new[] { new LevelCropConfig(), new LevelCropConfig(), new LevelCropConfig() },
            intensityStart = 3f,
            intensityEnd = 3f,
        };

        var cfg = new GameRulesConfig
        {
            MatchDurationMs = 60_000f,
            TelegraphDurationMs = 800f,
            UpWindowMs = 1_500f,
            RiseDurationMs = 250f,
            SinkDurationMs = 250f,
            CropCount = 3,
            HoleCount = 3,
            HoleCandidates = new[] { new[] { 0 }, new[] { 1 }, new[] { 2 } },
            BaseSpawnIntervalMs = 3_000f,
            IntensityCurve = p => 3f,
            Level = level,
        };

        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);

        // Create 3 moles with independent recorders
        var moles = new Mole[3];
        var recorders = new RecordingMoleAnimator[3];
        var gos = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            var (go, mole, rec) = CreateMoleWithRecorder();
            mole.Bind(rules, i);
            moles[i] = mole;
            recorders[i] = rec;
            gos[i] = go;
        }

        // Spawn all 3 moles
        rules.TrySpawn(0f);
        rules.TrySpawn(0f);
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f); // All get Hide + SetSpecies

        // Advance all to Rising
        DriveFrame(rules, tracker, moles, 800f); // All Rise

        // Advance to Up
        DriveFrame(rules, tracker, moles, 1050f); // All Search

        // Now: hit hole 0 at t=1100, let hole 1 escape (timeout at t=2550), hole 2 idle
        // Advance to t=1100 with hit on hole 0
        var hitFrameMs = 1100f;
        rules.Update(hitFrameMs - 1050f); // delta from last frame
        // Actually we need to advance time properly... let's use absolute time
        // Reset and use DriveFrame

        // Simpler: run up to a frame where hole 0 is hit and hole 1 escapes
        // Use absolute drive approach
        rules.Update(1100f);
        rules.TryHit(0, 1100f); // hit hole 0: Sinking
        var esc1100 = rules.DrainEscapes(); // none yet
        rules.TrySpawn(1100f);

        // Hit in OnTap phase happens before Update
        recorders[0].Clear();
        recorders[1].Clear();
        recorders[2].Clear();

        // Simulate: OnTap fires for hole 0
        rules.TryHit(0, 1100f); // already hit, returns false (wasHit already true)
        // Actually let's just do a clean DriveFrame at t=1100 with hit on hole 0
        // We already mutated rules state above, let's reset...

        // Clean reset for simultaneous test
        GameObject.DestroyImmediate(gos[0]);
        GameObject.DestroyImmediate(gos[1]);
        GameObject.DestroyImmediate(gos[2]);

        rules = new GameRules(cfg, () => 0.5f);
        tracker = new MoleIntentTracker(cfg.HoleCount);
        for (int i = 0; i < 3; i++)
        {
            var (go, mole, rec) = CreateMoleWithRecorder();
            mole.Bind(rules, i);
            moles[i] = mole;
            recorders[i] = rec;
            gos[i] = go;
        }

        // Spawn all
        rules.TrySpawn(0f);
        rules.TrySpawn(0f);
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f);   // Hide ×3 + SetSpecies ×3
        DriveFrame(rules, tracker, moles, 800f); // Rise ×3
        DriveFrame(rules, tracker, moles, 1050f); // Search ×3

        // Now at t=1400: hit hole 0 (lethal)
        recorders[0].Clear(); recorders[1].Clear(); recorders[2].Clear();
        DriveFrame(rules, tracker, moles, 1400f, hitHoles: new[] { 0 });
        Assert.That(recorders[0].Calls, Is.EquivalentTo(new[] { "OnHit", "OnSink" }),
            "Hole 0: should receive Hit + Sink");
        Assert.That(recorders[1].Calls, Is.Empty, "Hole 1: should have no intents (still Up)");
        Assert.That(recorders[2].Calls, Is.Empty, "Hole 2: should have no intents (still Up)");

        // Advance hole 0 to Reset. TrySpawn refills the hole after Poll.
        recorders[0].Clear();
        DriveFrame(rules, tracker, moles, 1650f);
        Assert.That(recorders[0].Calls, Is.EquivalentTo(new[] { "OnReset" }));

        // Capture new mole's spawn edge (TrySpawn created it at 1650f after Poll).
        recorders[0].Clear();
        DriveFrame(rules, tracker, moles, 1670f);
        Assert.That(recorders[0].Calls, Is.EquivalentTo(new[] { "SetSpecies", "OnHide" }));

        // Now hole 1 and 2 escape at t=2550. Hole 0 will be Rising by then.
        recorders[0].Clear(); recorders[1].Clear(); recorders[2].Clear();
        DriveFrame(rules, tracker, moles, 2550f);
        Assert.That(recorders[1].Calls, Is.EquivalentTo(new[] { "OnEscape", "OnSink" }));
        Assert.That(recorders[2].Calls, Is.EquivalentTo(new[] { "OnEscape", "OnSink" }));
        // Hole 0: new mole rose from Telegraphing — OnRise only (SetSpecies already dispatched)
        Assert.That(recorders[0].Calls, Is.EquivalentTo(new[] { "OnRise" }),
            "Hole 0: new mole's Rise (Telegraphing→Rising)");

        // Cleanup
        for (int i = 0; i < 3; i++) GameObject.DestroyImmediate(gos[i]);
    }

    // --- Multi-hit (non-lethal) ---

    [Test]
    public void MultiHit_NonLethal_EmitsHitWithoutSink()
    {
        // Config with TotalHits = 2: first hit doesn't kill (mole stays Up)
        var species = ScriptableObject.CreateInstance<MoleSpecies>();
        species.useTelegraph = true;
        species.baseHitsToKill = 2; // 2 hits required

        var level = new LevelProfile
        {
            moleMods = new[] { new LevelMoleMod { species = species, weight = 1 } },
            cropConfigs = new[] { new LevelCropConfig(), new LevelCropConfig() },
            intensityStart = 1f,
            intensityEnd = 1f,
        };

        var cfg = new GameRulesConfig
        {
            MatchDurationMs = 60_000f,
            TelegraphDurationMs = 800f,
            UpWindowMs = 1_500f,
            RiseDurationMs = 250f,
            SinkDurationMs = 250f,
            CropCount = 2,
            HoleCount = 1,
            HoleCandidates = new[] { new[] { 0, 1 } },
            BaseSpawnIntervalMs = 3_000f,
            IntensityCurve = p => 1f,
            Level = level,
        };

        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        // Spawn + lifecycle to Up
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f);    // Hide + SetSpecies
        DriveFrame(rules, tracker, moles, 800f);  // Rise
        DriveFrame(rules, tracker, moles, 1050f); // Search — mole is Up

        // First hit: non-lethal (hitsRemaining still > 0)
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1200f, hitHoles: new[] { 0 });
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnHit" }),
            "Non-lethal hit should emit OnHit without OnSink (mole stays Up)");

        // Second hit: lethal
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1300f, hitHoles: new[] { 0 });
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnHit", "OnSink" }),
            "Lethal hit should emit OnHit + OnSink");

        // Advance to Sunk
        rec.Clear();
        DriveFrame(rules, tracker, moles, 1550f); // Reset
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "OnReset" }));

        GameObject.DestroyImmediate(go);
    }

    // --- Reset ---

    [Test]
    public void Reset_AtStartMatch_ClearsTrackerAllowsFreshCycle()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        // Complete one cycle
        rules.TrySpawn(0f);
        DriveFrame(rules, tracker, moles, 0f);    // Hide
        DriveFrame(rules, tracker, moles, 800f);  // Rise
        DriveFrame(rules, tracker, moles, 1050f); // Search
        DriveFrame(rules, tracker, moles, 2550f); // Escape + Sink
        DriveFrame(rules, tracker, moles, 2800f); // Reset

        rec.Clear();

        // Reset tracker (like StartMatch)
        tracker.Reset();

        // Spawn a new mole — tracker sees it as fresh
        rules.TrySpawn(2800f);
        DriveFrame(rules, tracker, moles, 2800f);
        Assert.That(rec.Calls, Is.EquivalentTo(new[] { "SetSpecies", "OnHide" }),
            "After Reset, new spawn should produce SetSpecies + OnHide just like a fresh start");

        GameObject.DestroyImmediate(go);
    }

    // --- No duplicate polling ---

    [Test]
    public void NoDuplicate_SecondDriveFrameAtSameMs_DoesNotReEmit()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);
        var tracker = new MoleIntentTracker(cfg.HoleCount);
        var (go, mole, rec) = CreateMoleWithRecorder();
        mole.Bind(rules, 0);
        var moles = new Mole[] { mole };

        rules.TrySpawn(0f);

        // First frame: spawn edge detected
        rec.Clear();
        DriveFrame(rules, tracker, moles, 0f);
        Assert.That(rec.Calls.Count, Is.GreaterThan(0), "First poll should produce intents");

        // Second frame at same timestamp: tracker should be idempotent
        rec.Clear();
        DriveFrame(rules, tracker, moles, 0f);
        Assert.That(rec.Calls, Is.Empty,
            "Second frame at same timestamp must NOT re-emit intents (tracker idempotent)");

        GameObject.DestroyImmediate(go);
    }

    // --- Null-safe ---

    [Test]
    public void NullAnimator_OnIntentOnRawHitOnRawEscape_NoNRE()
    {
        // Mole with NO animator assigned: OnIntent/OnRawHit/OnRawEscape must not throw.
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);

        var go = new GameObject("TestMole");
        var mole = go.AddComponent<Mole>();
        mole.Bind(rules, 0);

        // animator is null (no RecordingMoleAnimator added)
        // These calls must not throw:
        Assert.DoesNotThrow(() =>
        {
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Hide, null, 0f));
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Rise, null, 0f));
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Search, null, 0f));
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Sink, null, 0f));
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Reset, null, 0f));
            mole.OnRawHit();
            mole.OnRawEscape(new CropStealEvent(0, 0, 0f));
        }, "All dispatch methods must be null-safe when animator is not assigned");

        GameObject.DestroyImmediate(go);
    }

    [Test]
    public void NullAnimator_SetSpeciesAtCycleStart_DoesNotThrow()
    {
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);

        var go = new GameObject("TestMole");
        var mole = go.AddComponent<Mole>();
        mole.Bind(rules, 0);

        var species = ScriptableObject.CreateInstance<MoleSpecies>();
        Assert.DoesNotThrow(() =>
        {
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Hide, species, 0f));
        }, "SetSpecies dispatch with null animator should be safe");

        Assert.DoesNotThrow(() =>
        {
            mole.OnIntent(new MoleIntentEvent(0, MoleIntent.Rise, species, 0f));
        }, "SetSpecies dispatch on Rise with null animator should be safe");

        GameObject.DestroyImmediate(go);
    }

    // --- Default/fallback: without animator, Mole.cs SyncFromRules still works ---

    [Test]
    public void WithoutAnimator_SyncFromRules_StillFunctions()
    {
        // Verify that Mole.SyncFromRules still works when animator is null.
        // This is the zero-regression test: existing behavior must be preserved.
        var cfg = TelegraphConfig();
        var rules = new GameRules(cfg, () => 0.5f);

        var go = new GameObject("TestMole");
        var mole = go.AddComponent<Mole>(); // NO animator
        mole.Bind(rules, 0);

        // SyncFromRules should run without NRE (the existing code path)
        Assert.DoesNotThrow(() =>
        {
            mole.SyncFromRules(0f);
            mole.SyncFromRules(800f);
            mole.SyncFromRules(1050f);
        }, "SyncFromRules must still work when no animator is assigned (zero regression)");

        // PlayHitJuice/PlayEscapeJuice must also work
        Assert.DoesNotThrow(() =>
        {
            mole.PlayHitJuice();
            mole.PlayEscapeJuice();
        }, "Juice methods must still work (null-safe sprite references)");

        // ContainsPoint must still work
        bool hit = mole.ContainsPoint(Vector2.zero);
        Assert.That(hit, Is.True, "ContainsPoint should still work — mole at (0,0), radius 0.6");

        GameObject.DestroyImmediate(go);
    }
}
