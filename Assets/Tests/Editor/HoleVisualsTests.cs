using NUnit.Framework;

// EditMode tests for pure hole visual-state mapping (Strict TDD — written BEFORE HoleVisuals).
// Priority: active flash > Telegraph > Emphasis (Rising|Up) > Dim. Flash expiry falls back to phase.
// NO UnityEngine dependency in production class under test.
public class HoleVisualsTests
{
    private const int HoleCount = 17;
    private const float FlashMs = 150f;

    private static HoleVisuals Make(float flashMs = FlashMs) => new HoleVisuals(HoleCount, flashMs);

    [Test]
    public void StateFor_SunkMole_ReturnsDim()
    {
        var v = Make();
        Assert.AreEqual(HoleVisualState.Dim, v.StateFor(0, MolePhase.Sunk, 0f));
        Assert.AreEqual(HoleVisualState.Dim, v.StateFor(5, MolePhase.Sinking, 100f));
    }

    [Test]
    public void StateFor_Telegraphing_ReturnsDim()
    {
        // Gameplay (V8): the warning must NOT reveal the spawn hole — hole stays Dim.
        var v = Make();
        Assert.AreEqual(HoleVisualState.Dim, v.StateFor(3, MolePhase.Telegraphing, 0f));
    }

    [Test]
    public void StateFor_Rising_ReturnsEmphasis()
    {
        var v = Make();
        Assert.AreEqual(HoleVisualState.Emphasis, v.StateFor(2, MolePhase.Rising, 500f));
    }

    [Test]
    public void StateFor_Up_ReturnsEmphasis()
    {
        var v = Make();
        Assert.AreEqual(HoleVisualState.Emphasis, v.StateFor(7, MolePhase.Up, 900f));
    }

    [Test]
    public void RegisterTap_Hit_FlashesHitUntilExpiry()
    {
        var v = Make();
        v.RegisterTap(4, wasHit: true, nowMs: 1000f);
        Assert.AreEqual(HoleVisualState.HitFlash, v.StateFor(4, MolePhase.Up, 1000f));
        Assert.AreEqual(HoleVisualState.HitFlash, v.StateFor(4, MolePhase.Up, 1000f + FlashMs - 1f));
        Assert.AreEqual(HoleVisualState.Emphasis, v.StateFor(4, MolePhase.Up, 1000f + FlashMs));
    }

    [Test]
    public void RegisterTap_Miss_FlashesMissUntilExpiry()
    {
        var v = Make();
        v.RegisterTap(1, wasHit: false, nowMs: 200f);
        Assert.AreEqual(HoleVisualState.MissFlash, v.StateFor(1, MolePhase.Sunk, 200f));
        Assert.AreEqual(HoleVisualState.MissFlash, v.StateFor(1, MolePhase.Sunk, 200f + FlashMs - 1f));
        Assert.AreEqual(HoleVisualState.Dim, v.StateFor(1, MolePhase.Sunk, 200f + FlashMs));
    }

    [Test]
    public void Flash_TakesPriorityOverDim()
    {
        var v = Make();
        v.RegisterTap(8, wasHit: true, nowMs: 50f);
        Assert.AreEqual(HoleVisualState.HitFlash, v.StateFor(8, MolePhase.Telegraphing, 50f));
        v.RegisterTap(9, wasHit: false, nowMs: 50f);
        Assert.AreEqual(HoleVisualState.MissFlash, v.StateFor(9, MolePhase.Telegraphing, 50f));
    }

    [Test]
    public void FlashExpiry_WhileTelegraphing_ReturnsDim()
    {
        // V4-S3: flash expiry falls back to phase state (telegraph hole stays dim).
        var v = Make();
        v.RegisterTap(0, wasHit: false, nowMs: 0f);
        Assert.AreEqual(HoleVisualState.MissFlash, v.StateFor(0, MolePhase.Telegraphing, 0f));
        Assert.AreEqual(HoleVisualState.Dim, v.StateFor(0, MolePhase.Telegraphing, FlashMs));
    }

    [Test]
    public void FlashExpiry_WhenSunk_ReturnsDim()
    {
        var v = Make();
        v.RegisterTap(6, wasHit: true, nowMs: 10f);
        Assert.AreEqual(HoleVisualState.HitFlash, v.StateFor(6, MolePhase.Sunk, 10f));
        Assert.AreEqual(HoleVisualState.Dim, v.StateFor(6, MolePhase.Sunk, 10f + FlashMs));
    }

    [Test]
    public void NoTap_NeverFlashes()
    {
        var v = Make();
        // All phases without RegisterTap never yield a flash state.
        Assert.AreNotEqual(HoleVisualState.HitFlash, v.StateFor(0, MolePhase.Sunk, 0f));
        Assert.AreNotEqual(HoleVisualState.MissFlash, v.StateFor(0, MolePhase.Sunk, 0f));
        Assert.AreNotEqual(HoleVisualState.HitFlash, v.StateFor(1, MolePhase.Telegraphing, 0f));
        Assert.AreNotEqual(HoleVisualState.MissFlash, v.StateFor(1, MolePhase.Telegraphing, 0f));
        Assert.AreNotEqual(HoleVisualState.HitFlash, v.StateFor(2, MolePhase.Rising, 0f));
        Assert.AreNotEqual(HoleVisualState.MissFlash, v.StateFor(2, MolePhase.Up, 0f));
    }

    [Test]
    public void TapOnOneHole_OtherHolesStayDim()
    {
        var v = Make();
        v.RegisterTap(3, wasHit: true, nowMs: 100f);
        Assert.AreEqual(HoleVisualState.HitFlash, v.StateFor(3, MolePhase.Sunk, 100f));
        for (int i = 0; i < HoleCount; i++)
        {
            if (i == 3) continue;
            Assert.AreEqual(HoleVisualState.Dim, v.StateFor(i, MolePhase.Sunk, 100f),
                $"hole {i} should stay Dim while only hole 3 flashed");
        }
    }
}
