using System;
using NUnit.Framework;

// EditMode tests for pure mole rules (Strict TDD — written before GameRules exists).
// Deterministic: injected Func<float,float> curves + Func<float> randomUnit + exact nowMs.
public class GameRulesTests
{
    private static GameRulesConfig MakeConfig(Func<float, float> curve = null, int holeCount = 3,
        float matchMs = 60_000f, float upMs = 1_500f, float riseMs = 250f, float sinkMs = 250f,
        int lives = 3, float baseIntervalMs = 3_000f)
    {
        return new GameRulesConfig
        {
            MatchDurationMs = matchMs,
            UpWindowMs = upMs,
            RiseDurationMs = riseMs,
            SinkDurationMs = sinkMs,
            InitialLives = lives,
            HoleCount = holeCount,
            BaseSpawnIntervalMs = baseIntervalMs,
            IntensityCurve = curve ?? (p => 1f),
        };
    }

    // Parabola matching spec keys (0,0) (0.5,2) (1,0): f(p) = 8p(1-p)
    private static float PeakCurve(float p) => 8f * p * (1f - p);

    // --- Curve mapping ---

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

    // --- Concurrency limit ---

    [Test]
    public void TrySpawn_AtMaxConcurrent_ReturnsFalse()
    {
        var rules = new GameRules(MakeConfig(p => 2f), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));      // mole 0 Rising
        Assert.IsTrue(rules.TrySpawn(1_000f));  // mole 1 Rising (UpCount 0 < limit)
        rules.Update(1_500f);                   // both reach Up: mole0 (250..1750), mole1 (1250..2750)
        Assert.AreEqual(2, rules.UpCount);
        Assert.IsFalse(rules.TrySpawn(1_500f)); // third mole blocked by limit
        Assert.AreEqual(2, rules.UpCount);
    }

    [Test]
    public void TrySpawn_NoSunkSlot_ReturnsFalse()
    {
        var rules = new GameRules(MakeConfig(p => 2f, holeCount: 1), () => 0f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));       // only hole now rising
        Assert.IsFalse(rules.TrySpawn(1_000f));  // no sunk slot remains
    }

    [Test]
    public void TrySpawn_SelectsRandomSunkSlot()
    {
        var rules = new GameRules(MakeConfig(p => 1f, holeCount: 3), () => 0.9f);
        rules.StartMatch();
        Assert.IsTrue(rules.TrySpawn(0f));
        Assert.AreEqual(MolePhase.Rising, rules.GetPhase(2)); // 0.9 * 3 -> floor 2
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(1));
    }

    // --- Scoring ---

    [Test]
    public void TryHit_HittableMole_IncrementsScoreAndSinksImmediately()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f); // rise done -> Up
        Assert.IsTrue(rules.TryHit(0, 300f));
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.IsTrue(rules.WasHit(0));
    }

    [Test]
    public void TryHit_NonHittablePhases_NoScoreChange()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();

        Assert.IsFalse(rules.TryHit(0, 0f));   // Sunk -> no-op
        Assert.AreEqual(0, rules.Score);

        rules.TrySpawn(0f);
        Assert.IsFalse(rules.TryHit(0, 100f)); // Rising -> no-op
        Assert.AreEqual(0, rules.Score);

        rules.Update(250f);                    // Up
        rules.TryHit(0, 300f);                 // hit -> Sinking
        Assert.IsFalse(rules.TryHit(0, 400f)); // Sinking -> no-op
        Assert.AreEqual(1, rules.Score);
    }

    [Test]
    public void Score_NeverDecreases_AcrossHitsAndEscapes()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);
        rules.TryHit(0, 300f);    // +1
        rules.Update(600f);       // sink completes (300+250) -> Sunk
        rules.TrySpawn(600f);     // same mole re-rises
        rules.Update(850f);       // Up again
        rules.Update(2_500f);     // escapes (850+1500) -> life lost, score untouched
        Assert.AreEqual(1, rules.Score);
        Assert.AreEqual(2, rules.Lives);
    }

    // --- Lives / Game Over ---

    [Test]
    public void Escape_ConsumesOneFruit()
    {
        var rules = new GameRules(MakeConfig(lives: 3), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);    // Up
        rules.Update(1_751f);  // past 250+1500 -> expiry
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(2, rules.Lives);
        Assert.AreEqual(0, rules.Score);
    }

    [Test]
    public void Lives_ClampAtZero_AndTriggersGameOver()
    {
        var rules = new GameRules(MakeConfig(lives: 2), () => 0f);
        rules.StartMatch();
        // escape 1 -> 1 life
        rules.TrySpawn(0f);
        rules.Update(250f);
        rules.Update(1_751f);
        Assert.AreEqual(1, rules.Lives);
        Assert.IsFalse(rules.IsGameOver);
        // escape 2 -> 0 lives, game over
        rules.TrySpawn(2_000f);
        rules.Update(2_250f);
        rules.Update(3_751f);
        Assert.AreEqual(0, rules.Lives);
        Assert.IsTrue(rules.IsGameOver);
        // no spawns allowed after game over -> lives stay 0, never negative
        Assert.IsFalse(rules.TrySpawn(4_000f));
        rules.Update(100_000f);
        Assert.AreEqual(0, rules.Lives);
        Assert.IsTrue(rules.IsGameOver);
    }

    [Test]
    public void GameOver_StopsHitsSpawnsAndFurtherLifeLoss()
    {
        var rules = new GameRules(MakeConfig(lives: 1), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);
        rules.Update(1_751f); // expiry -> lives 0, GameOver
        Assert.IsTrue(rules.IsGameOver);
        Assert.AreEqual(0, rules.Lives);

        Assert.IsFalse(rules.TryHit(0, 2_000f)); // hits ignored
        Assert.AreEqual(0, rules.Score);

        Assert.IsFalse(rules.TrySpawn(2_000f)); // spawns stopped

        rules.Update(100_000f);                 // long idle -> no further loss
        Assert.AreEqual(0, rules.Lives);
    }

    [Test]
    public void GameOver_PersistsUntilStartMatch()
    {
        var rules = new GameRules(MakeConfig(lives: 1), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);
        rules.Update(1_751f);
        Assert.IsTrue(rules.IsGameOver);

        rules.StartMatch(); // reset
        Assert.IsFalse(rules.IsGameOver);
        Assert.AreEqual(1, rules.Lives); // restored to config InitialLives (1), not hardcoded
        Assert.AreEqual(0, rules.Score);
        Assert.IsTrue(rules.TrySpawn(0f)); // spawning works again
    }

    // --- Lifecycle timing ---

    [Test]
    public void Lifecycle_RiseUpSinkTransitions_AreExact()
    {
        var rules = new GameRules(MakeConfig(riseMs: 250f, upMs: 1_500f, sinkMs: 250f), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        Assert.AreEqual(MolePhase.Rising, rules.GetPhase(0));
        Assert.AreEqual(0f, rules.GetPhaseStartMs(0));

        rules.Update(249f);
        Assert.AreEqual(MolePhase.Rising, rules.GetPhase(0)); // still rising
        Assert.IsFalse(rules.TryHit(0, 249f));                 // not hittable while rising

        rules.Update(250f);
        Assert.AreEqual(MolePhase.Up, rules.GetPhase(0));      // exact rise expiry
        Assert.AreEqual(250f, rules.GetPhaseStartMs(0));
        Assert.IsTrue(rules.TryHit(0, 300f));                  // hittable while Up
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(300f, rules.GetPhaseStartMs(0));

        rules.Update(549f);
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0)); // still sinking
        rules.Update(550f);
        Assert.AreEqual(MolePhase.Sunk, rules.GetPhase(0));    // exact sink expiry
        Assert.IsFalse(rules.TryHit(0, 600f));                 // Sunk -> no-op
    }

    [Test]
    public void UpWindow_HittableUntilLastMillisecond_BeforeExactExpiry()
    {
        var rules = new GameRules(MakeConfig(riseMs: 250f, upMs: 1_500f, lives: 3), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);   // Up, phaseStart = 250

        rules.Update(1_749f); // 1749 < 250+1500 -> still Up and hittable
        Assert.AreEqual(MolePhase.Up, rules.GetPhase(0));
        Assert.IsTrue(rules.TryHit(0, 1_749f)); // hit counts
        Assert.AreEqual(1, rules.Score);
    }

    [Test]
    public void UpWindow_ExactExpiry_SinksAndCostsLife()
    {
        var rules = new GameRules(MakeConfig(riseMs: 250f, upMs: 1_500f, lives: 3), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);   // Up, phaseStart = 250

        rules.Update(1_749f); // still Up just before expiry
        Assert.AreEqual(MolePhase.Up, rules.GetPhase(0));

        rules.Update(1_750f); // 1750 >= 250+1500 -> exact expiry
        Assert.AreEqual(MolePhase.Sinking, rules.GetPhase(0));
        Assert.AreEqual(2, rules.Lives);
        Assert.IsFalse(rules.TryHit(0, 1_750f)); // Sinking -> no-op
    }

    [Test]
    public void Hit_EndsLifecycle_NeverHittableAgain_UntilRespawn()
    {
        var rules = new GameRules(MakeConfig(), () => 0f);
        rules.StartMatch();
        rules.TrySpawn(0f);
        rules.Update(250f);                       // Up
        Assert.IsTrue(rules.TryHit(0, 300f));     // hit
        Assert.IsFalse(rules.TryHit(0, 301f));    // Sinking -> cannot re-hit
        Assert.AreEqual(1, rules.Score);

        rules.Update(600f);                       // sunk (300+250)
        Assert.IsFalse(rules.TryHit(0, 600f));    // Sunk -> no-op
        Assert.IsTrue(rules.WasHit(0));           // lifecycle still remembered

        rules.TrySpawn(600f);                     // re-rises
        Assert.IsFalse(rules.WasHit(0));          // fresh lifecycle clears flag
        rules.Update(850f);                       // Up again
        Assert.IsTrue(rules.TryHit(0, 900f));     // hittable again after respawn
        Assert.AreEqual(2, rules.Score);
    }
}
