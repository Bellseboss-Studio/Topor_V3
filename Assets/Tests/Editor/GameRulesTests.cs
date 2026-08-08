using System;
using NUnit.Framework;

// EditMode tests for pure mole rules (Strict TDD — written before GameRules extensions exist).
// Deterministic: injected Func<float,float> curves + Func<float> randomUnit + exact nowMs.
//
// grid-v2 evolution: config-driven HOLE ADJACENCY (hole -> 0..4 candidate crops, row index =
// hole index), mole DECIDES its target among ALIVE candidates at spawn (two rolls: hole, then
// target), TryHit window = Rising OR Up, WIN by survival (time-up gate, gate closes BEFORE
// escapes/hits/spawns), dead-target escape is a no-op, HUD timer helper FormatRemainingMs.
//
// Default 17-hole damero table (grid-v2 RELAYOUT; hole index -> candidate crop rows,
// row-major over the 7x5 pattern skipping V cells). Crop cover per approved design:
//   h0->[0] h1->[1] h2->[0] h3->[0,1] h4->[1] h5->[0,2] h6->[1,3] h7->[2] h8->[2,3]
//   h9->[3] h10->[2,4] h11->[3,5] h12->[4] h13->[4,5] h14->[5] h15->[4] h16->[5]
// Perimeter singles (1 candidate): h0,h1,h2,h4,h7,h9,h12,h14,h15,h16.
// Shared-2 (mole decides between two adjacent fruits): h3,h5,h6,h8,h10,h11,h13.
// NO shared-4 hubs remain in the 17-hole damero.
//
// Timing contract (one phase transition per Update call):
//   spawn @t0 -> Telegraphing(0)
//   Update(t0 + telegraphMs)        -> Rising          (HITTABLE in grid-v2)
//   Update(t0 + telegraph+rise)     -> Up              (HITTABLE)
//   Update(t0 + telegraph+rise+upw) -> escape          (Sinking + steal + event)
//   Update(escape + sinkMs)         -> Sunk
public class GameRulesTests
{
    private static readonly int[][] DefaultCandidates =
    {
        // 17-hole damero: row-major over the 7x5 pattern, skipping V cells.
        // (row0,col1) -> h0 [0]     (row0,col3) -> h1 [1]
        // (row1,col0) -> h2 [0]     (row1,col2) -> h3 [0,1]   (row1,col4) -> h4 [1]
        // (row2,col1) -> h5 [0,2]   (row2,col3) -> h6 [1,3]
        // (row3,col0) -> h7 [2]     (row3,col2) -> h8 [2,3]   (row3,col4) -> h9 [3]
        // (row4,col1) -> h10 [2,4]  (row4,col3) -> h11 [3,5]
        // (row5,col0) -> h12 [4]    (row5,col2) -> h13 [4,5]  (row5,col4) -> h14 [5]
        // (row6,col1) -> h15 [4]    (row6,col3) -> h16 [5]
        new[] { 0 },
        new[] { 1 },
        new[] { 0 },
        new[] { 0, 1 },
        new[] { 1 },
        new[] { 0, 2 },
        new[] { 1, 3 },
        new[] { 2 },
        new[] { 2, 3 },
        new[] { 3 },
        new[] { 2, 4 },
        new[] { 3, 5 },
        new[] { 4 },
        new[] { 4, 5 },
        new[] { 5 },
        new[] { 4 },
        new[] { 5 },
    };

    private static GameRulesConfig MakeConfig(Func<float, float> curve = null, int[][] candidates = null,
        float matchMs = 60_000f, float telegraphMs = 800f, float upMs = 1_500f, float riseMs = 250f,
        float sinkMs = 250f, int cropCount = 6, float baseIntervalMs = 3_000f)
    {
        var table = candidates ?? DefaultCandidates;
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
            IntensityCurve = curve ?? (p => 1f),
        };
    }

    // Deterministic random sequence — consumes one value per roll.
    private static Func<float> Seq(params float[] values)
    {
        int i = 0;
        return () => values[Math.Min(i++, values.Length - 1)];
    }

    // Parabola matching spec keys (0,0) (0.5,2) (1,0): f(p) = 8p(1-p)
    private static float PeakCurve(float p) => 8f * p * (1f - p);

    // --- Curve mapping (unchanged semantics) ---

    [Test]
    public void MaxConcurrentAt_PeakMidpoint_IsTwo()
    {
        var rules = new GameRules(MakeConfig(PeakCurve), () => 0f);
        Assert.AreEqual(2, rules.MaxConcurrentAt(0.5f));
    }

    [Test]
    public void MaxConcurrentAt_StartAndEndOfMatch_IsZero()
    {
        var rules = new GameRules(MakeConfig(PeakCurve), () => 0f);
        Assert.AreEqual(0, rules.MaxConcurrentAt(0f));
        Assert.AreEqual(0, rules.MaxConcurrentAt(1f));
    }

    [Test]
    public void MaxConcurrentAt_ProgressOutsideUnitRange_IsClamped()
    {
        var rules = new GameRules(MakeConfig(PeakCurve), () => 0f);
        Assert.AreEqual(0, rules.MaxConcurrentAt(-0.5f)); // clamped to 0 -> curve(0)=0
        Assert.AreEqual(0, rules.MaxConcurrentAt(1.5f));  // clamped to 1 -> curve(1)=0
        Assert.AreEqual(2, rules.MaxConcurrentAt(0.5f));  // sanity: mid still peaks
    }

    [Test]
    public void Progress01_ClampsToUnitRange()
    {
        var rules = new GameRules(MakeConfig(matchMs: 60_000f), () => 0f);
        rules.StartMatch();
        Assert.AreEqual(0f, rules.Progress01(0f));
        Assert.AreEqual(0.5f, rules.Progress01(30_000f), 0.0001f);
        Assert.AreEqual(1f, rules.Progress01(120_000f)); // beyond match -> clamped
        Assert.AreEqual(0f, rules.Progress01(-1_000f));  // negative -> clamped
    }

    [Test]
    public void SpawnIntervalMs_ScalesWithCurveIntensity()
    {
        var idle = new GameRules(MakeConfig(p => 1f, baseIntervalMs: 3_000f), () => 0f);
        idle.StartMatch();
        Assert.AreEqual(3_000f, idle.SpawnIntervalMs(0f), 0.0001f); // max 1 -> base interval

        var busy = new GameRules(MakeConfig(p => 2f, baseIntervalMs: 3_000f), () => 0f);
        busy.StartMatch();
        Assert.AreEqual(1_500f, busy.SpawnIntervalMs(0f), 0.0001f); // max 2 -> half interval
    }

    // --- Concurrency limit (UpCount = non-Sunk active moles, D5) ---

    [Test]
    public void TrySpawn_AtMaxConcurrent_ReturnsFalse()
    {
        var rules = new GameRules(MakeConfig(p => 2f, cropCount: 6), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));     // mole 0 Telegraphing(0)
        Assert.IsTrue(rules.TrySpawn(1_000f)); // mole 1 Telegraphing(1000) — UpCount 1 < 2
        rules.Update(800f);                    // mole0 Rising
        rules.Update(1_050f);                  // mole0 Up
        rules.Update(1_800f);                  // mole1 Rising (1000+800)
        rules.Update(2_050f);                  // mole1 Up (1000+800+250)
        Assert.AreEqual(2, rules.UpCount);
        Assert.IsFalse(rules.TrySpawn(2_100f)); // third mole blocked by limit
        Assert.AreEqual(2, rules.UpCount);
    }

    [Test]
    public void TrySpawn_NoSunkSlot_ReturnsFalse()
    {
        var rules = new GameRules(MakeConfig(p => 2f, candidates: new[] { new[] { 0 } }, cropCount: 6), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));      // only hole now telegraphing
        Assert.IsFalse(rules.TrySpawn(1_000f)); // no sunk slot remains
    }

    [Test]
    public void TrySpawn_SelectsRandomSunkSlot()
    {
        // 3 rows: hole0->[0], hole1->[1], hole2->[5]. randomUnit 0.9 -> hole index 2 of 3.
        var rules = new GameRules(
            MakeConfig(p => 1f, candidates: new[] { new[] { 0 }, new[] { 1 }, new[] { 5 } }, cropCount: 6),
            () => 0.9f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(2)); // 0.9 * 3 sunk -> floor 2
        Assert.AreEqual(5, rules.ThreatenedCrop(2));                // row h2 -> candidate crop 5
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(1));
    }

    // --- Adjacency seam (A1) ---

[Test]
    public void CandidatesForHole_ReturnsConfiguredRow_PerimeterSingle_AndInteriorShared()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        // S1: perimeter single — h0 (row0,col1) threatens exactly one crop: crop 0.
        var corner = rules.CandidatesForHole(0);
        Assert.AreEqual(1, corner.Length);
        Assert.AreEqual(0, corner[0]);
        // S2: interior shared — h3 (row1,col2) sits between crops 0 and 1: mole decides (A2).
        var shared3 = rules.CandidatesForHole(3);
        Assert.AreEqual(2, shared3.Length);
        Assert.AreEqual(0, shared3[0]);
        Assert.AreEqual(1, shared3[1]);
        // h8 (row3,col2) also shared-2 between crops 2 and 3.
        var shared8 = rules.CandidatesForHole(8);
        Assert.AreEqual(2, shared8.Length);
        Assert.AreEqual(2, shared8[0]);
        Assert.AreEqual(3, shared8[1]);
    }

    [Test]
    public void GameRulesConfig_DefaultHoleCount_Mirrors17HoleTable()
    {
        // RELAYOUT guard: the bare config default must match the 17-hole damero, so a
        // consumer that forgets to derive HoleCount from the table still gets 17.
        Assert.AreEqual(17, new GameRulesConfig().HoleCount);
        Assert.AreEqual(DefaultCandidates.Length, new GameRulesConfig().HoleCount);
    }

    [Test]
    public void Mole_Decides_BetweenTwoCrops_AtSharedHole3()
    {
        // h3 (default 17-hole table) is shared [0,1]. Force eligible hole 3 via hole roll
        // 0.2 (floor(0.2*17)=3), then target roll 0.9 maps within alive [0,1] -> crop 1.
        var rules = new GameRules(MakeConfig(cropCount: 6), Seq(0.2f, 0.9f));
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(3));
        Assert.AreEqual(1, rules.ThreatenedCrop(3)); // 0.9 within [0,1] -> index 1 -> crop 1
    }

    // --- Scoring (timings shifted +TelegraphDurationMs) ---

    [Test]
    public void TryHit_HittableMole_IncrementsScoreAndSinksImmediately()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);   // telegraph done -> Rising
        rules.Update(1_050f); // rise done -> Up
        Assert.IsTrue(rules.TryHit(0, 1_100f));
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.IsTrue(rules.WasHit(0));
    }

    [Test]
    public void TryHit_NonHittablePhases_NoScoreChange()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();

        Assert.IsFalse(rules.TryHit(0, 0f));    // Sunk -> no-op
        Assert.AreEqual(0, rules.Score);

        rules.TrySpawn(0f);
        Assert.IsFalse(rules.TryHit(0, 100f));  // Telegraphing -> no-op (M2)
        Assert.AreEqual(0, rules.Score);

        rules.Update(800f);
        // P6: Rising is NOW hittable — TryHit succeeds mid-rise.
        Assert.IsTrue(rules.TryHit(0, 900f));
        Assert.AreEqual(1, rules.Score);

        Assert.IsFalse(rules.TryHit(0, 1_000f)); // Sinking -> no-op
        Assert.AreEqual(1, rules.Score);
    }

    [Test]
    public void Score_NeverDecreases_AcrossHitsAndEscapes()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);     // Rising
        rules.Update(1_050f);   // Up
        Assert.IsTrue(rules.TryHit(0, 1_100f)); // +1 -> Sinking(1100)
        rules.Update(1_350f);    // sink completes -> Sunk
        rules.TrySpawn(1_350f);  // same hole re-telegraphs (crop 0 alive)
        rules.Update(2_150f);    // Rising
        rules.Update(2_400f);    // Up
        rules.Update(3_900f);    // escapes (2400+1500) -> steals crop 0, score untouched
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(5, rules.Lives);
    }

    // --- Crops as lives (cropCount replaces InitialLives) ---

    [Test]
    public void Escape_StealsBoundCrop_ConsumesOneLife()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);   // mole 0 -> hole 0 -> bound candidate crop 0
        Assert.AreEqual(0, rules.ThreatenedCrop(0));
        rules.Update(800f);   // Rising
        rules.Update(1_050f); // Up
        rules.Update(2_550f); // exact expiry (1050+1500) -> escape
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(5, rules.Lives);
        Assert.IsFalse(rules.IsCropAlive(0)); // bound crop 0 stolen
        Assert.AreEqual(0, rules.Score);
    }

    [Test]
    public void Escape_Steals_OnlyBoundCrop_OthersRemain()
    {
        // randomUnit 0.99 -> hole roll (int)(0.99*17)=16 -> hole 16 (row6,col3), row [5].
        // target roll (int)(0.99*1)=0 -> single candidate crop 5.
        var rules = new GameRules(MakeConfig(cropCount: 6), () => 0.99f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(16));
        Assert.AreEqual(5, rules.ThreatenedCrop(16)); // mole decided crop 5

        rules.Update(800f);   // Rising
        rules.Update(1_050f); // Up
        rules.Update(2_550f); // escape -> steals crop 5
        Assert.IsFalse(rules.IsCropAlive(5)); // crop 5 removed
        for (int c = 0; c < 5; c++)
            Assert.IsTrue(rules.IsCropAlive(c), "crop " + c + " should remain");
        Assert.AreEqual(5, rules.Lives); // 6 - 1
    }

    [Test]
    public void Escape_ConsequenceInstantAtT_StateUnchangedAfterwards()
    {
        var rules = new GameRules(MakeConfig(cropCount: 6), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);   // Up
        rules.Update(2_550f);   // exact expiry T
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(5, rules.Lives);      // decremented AT T
        Assert.IsFalse(rules.IsCropAlive(0)); // crop removed AT T

        rules.Update(2_550f + 800f);          // ~0.8s carry-off window elapses
        Assert.AreEqual(5, rules.Lives);      // NEVER changes again
        Assert.IsFalse(rules.IsCropAlive(0)); // crop stays removed
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0)); // mole finished sinking

        var evs = rules.DrainEscapes();
        Assert.AreEqual(1, evs.Count);        // event available when drained
        Assert.AreEqual(0, evs[0].MoleIndex);
        Assert.AreEqual(0, evs[0].CropIndex);
        Assert.AreEqual(2_550f, evs[0].AtMs, 0.0001f);
    }

[Test]
    public void Hit_PreventsSteal_ScoreIncrements_KeepsCrop()
    {
        // randomUnit 0.99 -> hole 16 (row6,col3), row [5] -> bound crop 5.
        var rules = new GameRules(MakeConfig(cropCount: 6), () => 0.99f);
        rules.StartMatch();
        rules.TrySpawn(0f);     // mole 16 bound to crop 5
        rules.Update(800f);     // Rising
        rules.Update(1_050f);   // Up -> hittable
        Assert.IsTrue(rules.TryHit(16, 1_100f));
        Assert.AreEqual(1, rules.Score);
        Assert.IsTrue(rules.WasHit(16));

        rules.Update(100_000f); // let all remaining time elapse
        Assert.IsTrue(rules.IsCropAlive(5)); // crop NOT stolen
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(6, rules.Lives);
    }

    // --- Game over / terminal ---

    [Test]
    public void Crops_ClampAtZero_AndTriggerGameOver()
    {
        var rules = new GameRules(MakeConfig(cropCount: 2), () => 0f);
        rules.StartMatch();
        // escape 1 -> 1 crop left, not game over (hole0 -> crop 0; hole1 -> crop 1)
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f);   // mole 0 escapes -> steals crop 0
        Assert.AreEqual(1, rules.Lives);
        Assert.IsFalse(rules.IsGameOver);
        rules.Update(2_800f);   // mole 0 fully sinks -> hole frees up
        // escape 2 -> 0 crops, game over; only alive-crop holes are eligible now
        rules.TrySpawn(2_900f); // -> hole1 (next eligible sunk hole with alive crop)
        rules.Update(3_700f);   // Rising (2900+800)
        rules.Update(3_950f);   // Up (2900+800+250)
        rules.Update(5_450f);   // expiry (3950+1500) -> steals crop 1
        Assert.AreEqual(0, rules.Lives);
        Assert.IsTrue(rules.IsGameOver);
        // no spawns after game over -> lives stay 0, never negative
        Assert.IsFalse(rules.TrySpawn(5_500f));
        Assert.IsFalse(rules.TryHit(0, 5_500f));
        rules.Update(100_000f);
        Assert.AreEqual(0, rules.Lives);
        Assert.IsTrue(rules.IsGameOver);
    }

    [Test]
    public void GameOver_StopsHitsSpawnsAndFurtherCropLoss()
    {
        var rules = new GameRules(MakeConfig(cropCount: 1), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f); // expiry -> crop 0 stolen -> 0 crops -> GameOver
        Assert.IsTrue(rules.IsGameOver);
        Assert.AreEqual(0, rules.Lives);

        Assert.IsFalse(rules.TryHit(0, 3_000f)); // hits ignored
        Assert.AreEqual(0, rules.Score);

        Assert.IsFalse(rules.TrySpawn(3_000f));  // spawns stopped

        rules.Update(100_000f);                  // long idle -> no further loss
        Assert.AreEqual(0, rules.Lives);
    }

    [Test]
    public void GameOver_PersistsUntilStartMatch()
    {
        var rules = new GameRules(MakeConfig(cropCount: 1), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f);
        Assert.IsTrue(rules.IsGameOver);

        rules.StartMatch(); // reset
        Assert.IsFalse(rules.IsGameOver);
        Assert.AreEqual(1, rules.Lives); // restored to config CropCount
        Assert.AreEqual(0, rules.Score);
        Assert.IsTrue(rules.TrySpawn(0f)); // spawning works again
    }

    // --- Lifecycle timing (Rising is now HITTABLE per M2) ---

    [Test]
    public void Lifecycle_ExactTransitions_WithRisingHit_AreExact()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(0));
        Assert.AreEqual(0f, rules.GetPhaseStartMs(0));

        rules.Update(799f);
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(0)); // still telegraphing
        Assert.IsFalse(rules.TryHit(0, 799f));                    // not hittable during telegraph

        rules.Update(800f);
        Assert.AreEqual(MolePhase.Rising, rules.GetPhase(0));  // exact telegraph expiry
        Assert.AreEqual(800f, rules.GetPhaseStartMs(0));

        rules.Update(1_049f);
        Assert.AreEqual(MolePhase.Rising, rules.GetPhase(0));  // still rising (1049 < 1050)
        // P6/M2: hit DURING Rising now counts.
        Assert.IsTrue(rules.TryHit(0, 1045f));
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(1045f, rules.GetPhaseStartMs(0));

        // Sinking(1045) -> Sunk at exact +250 = 1295.
        rules.Update(1294f);
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0)); // 1294-1045 = 249 < 250
        rules.Update(1295f);
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));    // exact sink expiry
        Assert.IsFalse(rules.TryHit(0, 1_400f));               // Sunk -> no-op
        Assert.AreEqual(1, rules.Score);                       // never increased post-hit
    }

    [Test]
    public void UpWindow_HittableUntilLastMillisecond_BeforeExactExpiry()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);   // Up, phaseStart = 1050

        rules.Update(2_549f);   // 2549 < 1050+1500 -> still Up and hittable
        Assert.AreEqual(MolePhase.Up, rules.GetPhase(0));
        Assert.IsTrue(rules.TryHit(0, 2549f)); // hit counts
        Assert.AreEqual(1, rules.Score);
    }

    [Test]
    public void UpWindow_ExactExpiry_SinksAndStealsCrop()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);   // Up, phaseStart = 1050

        rules.Update(2_549f);   // still Up just before expiry
        Assert.AreEqual(MolePhase.Up, rules.GetPhase(0));

        rules.Update(2_550f);   // 2550 >= 1050+1500 -> exact expiry
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(5, rules.Lives);
        Assert.IsFalse(rules.IsCropAlive(0)); // escaped mole stole its crop
        Assert.IsFalse(rules.TryHit(0, 2_550f)); // Sinking -> no-op
    }

    [Test]
    public void Hit_EndsLifecycle_NeverHittableAgainAtSameHole_UntilNewSpawn()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);                      // Rising
        rules.Update(1_050f);                    // Up
        Assert.IsTrue(rules.TryHit(0, 1_100f));  // hit
        Assert.IsFalse(rules.TryHit(0, 1_101f)); // Sinking -> cannot re-hit
        Assert.AreEqual(1, rules.Score);

        rules.Update(1_350f);                      // sunk (1100+250)
        Assert.IsFalse(rules.TryHit(0, 1_350f));   // Sunk -> no-op
        Assert.IsTrue(rules.WasHit(0));            // lifecycle still remembered

        rules.TrySpawn(1_350f);                    // re-telegraphs
        Assert.IsFalse(rules.WasHit(0));           // fresh lifecycle clears flag
        rules.Update(2_150f);                      // Rising
        rules.Update(2_400f);                      // Up (1350+800+250)
        Assert.IsTrue(rules.TryHit(0, 2_500f));    // hittable again
        Assert.AreEqual(2, rules.Score);
    }

    // --- Telegraph announcement ---

    [Test]
    public void Telegraphing_NotHittable_MarksOnlyOneCrop_AcrossAllHoles()
    {
        var rules = new GameRules(MakeConfig(cropCount: 6), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));            // mole0 -> hole0 -> bound crop 0
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(0));
        Assert.AreEqual(0, rules.ThreatenedCrop(0)); // ONLY crop 0 marked

        var markedCount = 0;
        for (int h = 0; h < rules.HoleCount; h++)
            if (rules.ThreatenedCrop(h) >= 0) markedCount++;
        Assert.AreEqual(1, markedCount); // no other mole/hole marks another crop

        // not hittable during the whole telegraph window (M2)
        Assert.IsFalse(rules.TryHit(0, 100f));
        Assert.IsFalse(rules.TryHit(0, 799f));
        Assert.AreEqual(0, rules.Score);
    }

    [Test]
    public void Telegraphing_IsIndependentPerMole_AndConfigurable()
    {
        // Configurable telegraph duration: 500ms -> Rising at 500, Up at 750.
        var quick = new GameRules(MakeConfig(telegraphMs: 500f, cropCount: 6), () => 0f);
        quick.StartMatch();
        quick.TrySpawn(0f);
        Assert.AreEqual(MolePhase.Telegraphing, quick.GetPhase(0));
        quick.Update(500f);
        Assert.AreEqual(MolePhase.Rising, quick.GetPhase(0));
        quick.Update(750f);
        Assert.AreEqual(MolePhase.Up, quick.GetPhase(0));

        // Per-mole anchor: a second mole spawned at 1000 telegraphs until 1800
        // while the first is already Up.
        var rules = new GameRules(MakeConfig(p => 2f, cropCount: 6), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0);      // mole 0 -> hole 0, Telegraphing(0)
        rules.TrySpawn(1_000f); // mole 1 -> hole 1, Telegraphing(1000)
        rules.Update(800f);     // mole0 Rising; mole1 still Telegraphing (elapsed 0)
        rules.Update(1_050f);   // mole0 Up; mole1 still Telegraphing (elapsed 50 < 800)
        Assert.AreEqual(MolePhase.Up, rules.GetPhase(0));
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(1));
    }

    // --- Mole decides its target (A2) ---

    [Test]
    public void Mole_Decides_AmongAliveNeighbors_Seeded()
    {
        // 2-row table: hole0=[1], hole1=[1,2,4], cropCount=5.
        // First spawn (roll 0.0) -> eligible [0,1] -> hole0 -> target roll 0.0 -> alive [1] -> crop1.
        // Hole0 escapes -> crop 1 dead. Second spawn: only hole1 alive-candidate rows remain;
        // target roll 0.9 maps within alive [2,4] -> floor(0.9*2)=1 -> crop 4. (A2 scenario: 0.9->c4)
        var rules = new GameRules(
            MakeConfig(candidates: new[] { new[] { 1 }, new[] { 1, 2, 4 } }, cropCount: 5),
            Seq(0.0f, 0.0f, 0.0f, 0.9f));
        rules.StartMatch();

        Assert.IsTrue(rules.TrySpawn(0f));
        Assert.AreEqual(1, rules.ThreatenedCrop(0)); // hole0 binds crop 1
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(0));

        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f);                     // hole0 escapes -> crop 1 dead
        Assert.IsFalse(rules.IsCropAlive(1));
        rules.Update(2_800f);                     // hole0 sinks

        Assert.IsTrue(rules.TrySpawn(2_900f));    // only hole1 still has an alive candidate
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(1));
        Assert.AreEqual(4, rules.ThreatenedCrop(1)); // 0.9 within alive [2,4] -> crop 4
    }

    [Test]
    public void Molecule_Decides_ExcludesDeadCandidates()
    {
        // hole0=[0], hole1=[0,1], cropCount=2. Kill crop 0 via hole0. Then hole1's rows are
        // [0,1] with crop0 dead -> alive set [1]; a roll 0.4*1=0 -> crop1.
        // If the dead crop had NOT been excluded (0.4*2=0 -> crop 0) the mole would bind a dead crop.
        var rules = new GameRules(
            MakeConfig(candidates: new[] { new[] { 0 }, new[] { 0, 1 } }, cropCount: 2),
            Seq(0.0f, 0.0f, 0.0f, 0.4f));
        rules.StartMatch();

        Assert.IsTrue(rules.TrySpawn(0f)); // hole0 -> crop 0
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f);              // hole0 escapes -> crop 0 dead
        Assert.IsFalse(rules.IsCropAlive(0));
        rules.Update(2_800f);

        Assert.IsTrue(rules.TrySpawn(2_900f)); // only hole1 eligible; dead excluded
        Assert.AreEqual(1, rules.ThreatenedCrop(1));
        Assert.AreEqual(MolePhase.Telegraphing, rules.GetPhase(1));
    }

    [Test]
    public void Mole_Binds_SingleCandidate_RegardlessOfRandom()
    {
        // Single-candidate row [2]: any randomUnit (0.0 or 0.99) still binds crop 2.
        var low = new GameRules(MakeConfig(candidates: new[] { new[] { 2 } }, cropCount: 3), () => 0.0f);
        low.StartMatch();
        Assert.IsTrue(low.TrySpawn(0f));
        Assert.AreEqual(2, low.ThreatenedCrop(0));

        var high = new GameRules(MakeConfig(candidates: new[] { new[] { 2 } }, cropCount: 3), () => 0.99f);
        high.StartMatch();
        Assert.IsTrue(high.TrySpawn(0f));
        Assert.AreEqual(2, high.ThreatenedCrop(0));
    }

    [Test]
    public void NoAliveCandidates_NoSpawn_NoThreat_StateUnchanged()
    {
        // Both rows point only at crop 0. After crop 0 dies, NO hole has an alive candidate:
        // TrySpawn must return false, spawn nothing, threaten nothing.
        var rules = new GameRules(
            MakeConfig(candidates: new[] { new[] { 0 }, new[] { 0 } }, cropCount: 2),
            () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));   // one mole from hole0 -> crop 0
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f);                  // escape -> crop 0 stolen
        Assert.IsFalse(rules.IsCropAlive(0));
        rules.Update(2_800f);                  // mole0 sinks

        Assert.IsFalse(rules.TrySpawn(2_900f)); // no alive candidate -> nothing rises
        Assert.AreEqual(1, rules.Lives);         // crop 1 survives
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(1));
        Assert.AreEqual(-1, rules.ThreatenedCrop(0));
        Assert.AreEqual(-1, rules.ThreatenedCrop(1));
    }

    [Test]
    public void Steal_OfAlreadyDeadTarget_IsExactNoOp()
    {
        // P4 / M3-2: a mole CAN be bound to a crop that dies before ITS escape (here by an
        // earlier mole on the same hole row). Its escape must be a strict no-op: no extra
        // crop loss, no event, state consistent.
var rules = new GameRules(
            MakeConfig(curve: p => 2f, candidates: new[] { new[] { 2 }, new[] { 2 } }, cropCount: 3),
            () => 0f);
        rules.StartMatch();

        Assert.IsTrue(rules.TrySpawn(0f));       // mole0 -> hole0 -> crop 2 (telegraph @0)
        rules.Update(800f);                      // mole0 Rising
        rules.Update(1_050f);                    // mole0 Up
        Assert.IsTrue(rules.TrySpawn(1_000f));   // mole1 -> hole1 -> crop 2 (telegraph @1000)
        rules.Update(1_800f);                    // mole1 Rising (1000+800)
        rules.Update(2_050f);                    // mole1 Up (1000+800+250)
        rules.Update(2_550f);                    // mole0 escapes @2550 -> steals crop 2
        Assert.IsFalse(rules.IsCropAlive(2));
        Assert.AreEqual(2, rules.Lives);
        Assert.AreEqual(1, rules.DrainEscapes().Count); // exactly one steal event

        rules.Update(2_850f);                    // mole0 sinks; mole1 still Up (window 2050..3550)
        rules.Update(3_550f);                    // mole1 escapes @3550 -> target crop 2 ALREADY dead
        Assert.AreEqual(2, rules.Lives);         // no second loss (no-op)
        Assert.IsFalse(rules.IsCropAlive(2));    // crop stays dead
        Assert.AreEqual(0, rules.DrainEscapes().Count); // no event for the no-op steal
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));    // mole0 finished sinking (2550+250)
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(1)); // mole1 just escaped this tick
    }

    [Test]
    public void TwoMolesBoundSameCrop_SecondEscapeIsNoOp()
    {
        // Mole0 and mole1 both bind crop 0 (both rows only list crop 0).
        // Mole0 spawn@0: Telegraph(0..800) -> Rising(800..1050) -> Up(1050..2550) -> escape@2550.
        // Mole1 spawn@400: Telegraph(400..1200) -> Rising(1200..1450) -> Up(1450..2950) -> escape@2950.
        // Mole0 steals crop 0 at 2550; mole1's escape at 2950 is a no-op on the dead crop.
        var rules = new GameRules(
            MakeConfig(curve: p => 2f, candidates: new[] { new[] { 0 }, new[] { 0 } }, cropCount: 2),
            () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));        // mole0 -> hole0 -> crop 0
        Assert.IsTrue(rules.TrySpawn(400f));      // mole1 -> hole1 -> crop 0
        Assert.AreEqual(0, rules.ThreatenedCrop(0));
        Assert.AreEqual(0, rules.ThreatenedCrop(1));

        rules.Update(800f);                       // mole0 Rising
        rules.Update(1_050f);                     // mole0 Up
        rules.Update(1_200f);                     // mole1 Rising (400+800)
        rules.Update(1_650f);                     // mole1 Up (400+800+250)
        rules.Update(2_550f);                     // mole0 escape -> steals crop 0
        Assert.IsFalse(rules.IsCropAlive(0));
        Assert.AreEqual(1, rules.Lives);
        Assert.AreEqual(1, rules.DrainEscapes().Count); // mole0's event only

        rules.Update(2_950f);                     // mole1 escape -> no-op (crop 0 already dead)
        Assert.IsFalse(rules.IsCropAlive(0));     // stays dead
        Assert.AreEqual(1, rules.Lives);          // no second loss
        Assert.AreEqual(0, rules.DrainEscapes().Count); // no event for the no-op steal
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));    // mole0 finished sinking (2550+250)
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(1)); // mole1 just escaped this tick
    }

    // --- Escape queue (DrainEscapes) ---

    [Test]
    public void DrainEscapes_ReturnsQueuedEvent_ThenClears()
    {
        var rules = new GameRules(MakeConfig(cropCount: 6), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f); // escape at 2550

        var evs = rules.DrainEscapes();
        Assert.AreEqual(1, evs.Count);
        Assert.AreEqual(0, evs[0].MoleIndex);
        Assert.AreEqual(0, evs[0].CropIndex);
        Assert.AreEqual(2_550f, evs[0].AtMs, 0.0001f);
        Assert.AreEqual(5, rules.Lives);

        var again = rules.DrainEscapes();
        Assert.AreEqual(0, again.Count); // queue empties after drain
    }

    [Test]
    public void TwoEscapes_SameTick_QueuePreservesBoth_AndGameOverAtZeroCrops()
    {
        var rules = new GameRules(MakeConfig(p => 2f, cropCount: 2), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f)); // mole 0 -> hole0 -> crop 0
        Assert.IsTrue(rules.TrySpawn(0f)); // mole 1 -> hole1 -> crop 1
        rules.Update(800f);                // both Rising
        rules.Update(1_050f);              // both Up (same schedule)
        rules.Update(2_550f);              // BOTH expire same tick

        var evs = rules.DrainEscapes();
        Assert.AreEqual(2, evs.Count);     // queue preserves both
        Assert.IsFalse(rules.IsCropAlive(0));
        Assert.IsFalse(rules.IsCropAlive(1));
        Assert.AreEqual(0, rules.Lives);
        Assert.IsTrue(rules.IsGameOver);
        Assert.IsFalse(rules.TrySpawn(3_000f));
        Assert.IsFalse(rules.TryHit(0, 3_000f));
        Assert.AreEqual(0, rules.DrainEscapes().Count);
    }

    // --- WIN by time (A4) ---

    [Test]
    public void IsWin_AfterSurvivingDuration_IsTrue()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.Update(59_000f);
        Assert.IsFalse(rules.IsWin);          // still playing
        rules.Update(60_000f);                // gate closes
        Assert.IsTrue(rules.IsWin);
        Assert.IsFalse(rules.IsGameOver);
        rules.Update(120_000f);
        Assert.IsTrue(rules.IsWin);           // win persists
    }

    [Test]
    public void TimeUpGate_StopsSpawnsAndHits_FreezesState()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));    // mole telegraphing at 0
        rules.Update(800f);                   // Rising
        rules.Update(1_050f);                 // Up (hittable region)
        rules.Update(60_000f);                // gate closes FIRST (P5) — before any further escape/hit
        Assert.IsTrue(rules.IsWin);
        Assert.IsFalse(rules.TrySpawn(60_000f));  // spawns stop post-gate
        Assert.IsFalse(rules.TryHit(0, 60_000f)); // hits ignored post-gate
        Assert.AreEqual(0, rules.Score);
    }

    [Test]
    public void PostGate_TryHitReturnsFalse_TimeUpHit_NotScored()
    {
        var rules = new GameRules(MakeConfig(cropCount: 6), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);                   // mole is Rising
        rules.Update(1_050f);                 // mole Up — hittable just before gate
        rules.Update(60_000f);                // gate closes FIRST (A4-3)
        Assert.IsTrue(rules.IsWin);
        Assert.IsFalse(rules.TryHit(0, 60_000f)); // hit at boundary is NOT scored
        Assert.AreEqual(0, rules.Score);
        Assert.IsTrue(rules.IsCropAlive(0));       // frozen mole never steals
    }

    [Test]
    public void GameOverWinsPrecedence_IsWinFalseUnderGameOver()
    {
        // Player loses at ~2.6s (crops to 0). Even past MatchDurationMs, GameOver wins.
        var rules = new GameRules(MakeConfig(cropCount: 1), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(800f);
        rules.Update(1_050f);
        rules.Update(2_550f);            // steal crop0 -> GameOver
        Assert.IsTrue(rules.IsGameOver);

        Assert.IsFalse(rules.IsWin);
        rules.Update(30_000f);           // even at 30s
        Assert.IsTrue(rules.IsGameOver);
        Assert.IsFalse(rules.IsWin);
        rules.Update(100_000f);          // and at 60s+
        Assert.IsFalse(rules.IsWin);
        Assert.IsTrue(rules.IsGameOver);
    }

    // --- HUD timer (A5) ---

    [Test]
    public void FormatRemainingMs_MMSS_ClampsBelowZero()
    {
        Assert.AreEqual("01:00", GameRules.FormatRemainingMs(60_000f));
        Assert.AreEqual("00:30", GameRules.FormatRemainingMs(30_000f));
        Assert.AreEqual("00:05", GameRules.FormatRemainingMs(5_000f));
        Assert.AreEqual("01:30", GameRules.FormatRemainingMs(90_000f));
        Assert.AreEqual("00:00", GameRules.FormatRemainingMs(0f));
        Assert.AreEqual("00:00", GameRules.FormatRemainingMs(-1f));     // clamped >= 0
        Assert.AreEqual("00:00", GameRules.FormatRemainingMs(-60_000f)); // clamped >= 0
    }

    // --- Rising+Up window (M2) ---

    [Test]
    public void TryHit_Rising_True_Telegraph_False()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        Assert.IsFalse(rules.TryHit(0, 600f));  // Telegraphing -> false
        rules.Update(800f);                     // -> Rising
        Assert.IsTrue(rules.TryHit(0, 900f));    // Rising -> TRUE (P6)
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.IsFalse(rules.TryHit(0, 901f));   // already Sinking -> false
        Assert.AreEqual(1, rules.Score);
    }
}