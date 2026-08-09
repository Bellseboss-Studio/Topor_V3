using NUnit.Framework;
using UnityEngine;

// EditMode tests for MoleAnimator pure static helpers.
// No Time, no GameObjects, no assets — pure math validation.
//
// B-5 spec: defaults match HEAD procedural behavior
// (scale tween 0.2↔1.0, pop ×1.3 with ease-out)

public class MoleAnimatorDefaultTests
{
    // --- RiseScale01 ---

    [Test]
    public void RiseScale01_AtStart_ReturnsMinimum()
    {
        float result = MoleAnimator.RiseScale01(0f);
        Assert.That(result, Is.EqualTo(0.2f).Within(0.001f),
            "Rise starts at 0.2 — mole barely visible at hole lip");
    }

    [Test]
    public void RiseScale01_AtEnd_ReturnsFull()
    {
        float result = MoleAnimator.RiseScale01(1f);
        Assert.That(result, Is.EqualTo(1.0f).Within(0.001f),
            "Rise ends at 1.0 — mole fully visible");
    }

    [Test]
    public void RiseScale01_AtMidpoint_ReturnsMidScale()
    {
        float result = MoleAnimator.RiseScale01(0.5f);
        Assert.That(result, Is.EqualTo(0.6f).Within(0.001f),
            "0.2 + 0.8 × 0.5 = 0.6 — linear interpolation");
    }

    [Test]
    public void RiseScale01_AtQuarter_CalculatesCorrectly()
    {
        float result = MoleAnimator.RiseScale01(0.25f);
        Assert.That(result, Is.EqualTo(0.4f).Within(0.001f),
            "0.2 + 0.8 × 0.25 = 0.4");
    }

    [Test]
    public void RiseScale01_ClampsNegativeToMinimum()
    {
        float result = MoleAnimator.RiseScale01(-1f);
        Assert.That(result, Is.EqualTo(0.2f).Within(0.001f),
            "Negative t must clamp to 0, returning minimum 0.2");
    }

    [Test]
    public void RiseScale01_ClampsOverOneToMaximum()
    {
        float result = MoleAnimator.RiseScale01(2f);
        Assert.That(result, Is.EqualTo(1.0f).Within(0.001f),
            "t > 1 must clamp to 1, returning maximum 1.0");
    }

    // --- SinkScale01 ---

    [Test]
    public void SinkScale01_AtStart_ReturnsFull()
    {
        float result = MoleAnimator.SinkScale01(0f);
        Assert.That(result, Is.EqualTo(1.0f).Within(0.001f),
            "Sink starts at 1.0 — mole fully visible");
    }

    [Test]
    public void SinkScale01_AtEnd_ReturnsMinimum()
    {
        float result = MoleAnimator.SinkScale01(1f);
        Assert.That(result, Is.EqualTo(0.2f).Within(0.001f),
            "Sink ends at 0.2 — mole nearly hidden");
    }

    [Test]
    public void SinkScale01_AtMidpoint_ReturnsMidScale()
    {
        float result = MoleAnimator.SinkScale01(0.5f);
        Assert.That(result, Is.EqualTo(0.6f).Within(0.001f),
            "1 - 0.8 × 0.5 = 0.6 — linear interpolation");
    }

    [Test]
    public void SinkScale01_AtThreeQuarters_CalculatesCorrectly()
    {
        float result = MoleAnimator.SinkScale01(0.75f);
        Assert.That(result, Is.EqualTo(0.4f).Within(0.001f),
            "1 - 0.8 × 0.75 = 0.4");
    }

    [Test]
    public void SinkScale01_ClampsNegativeToFull()
    {
        float result = MoleAnimator.SinkScale01(-1f);
        Assert.That(result, Is.EqualTo(1.0f).Within(0.001f),
            "Negative t must clamp to 0, returning full scale 1.0");
    }

    [Test]
    public void SinkScale01_ClampsOverOneToMinimum()
    {
        float result = MoleAnimator.SinkScale01(2f);
        Assert.That(result, Is.EqualTo(0.2f).Within(0.001f),
            "t > 1 must clamp to 1, returning minimum 0.2");
    }

    // --- PopScale ---

    [Test]
    public void PopScale_AtStart_ReturnsMaxPop()
    {
        float result = MoleAnimator.PopScale(0f);
        Assert.That(result, Is.EqualTo(1.3f).Within(0.001f),
            "Pop starts at 1.3 — maximum exaggeration");
    }

    [Test]
    public void PopScale_AtEnd_ReturnsNormal()
    {
        float result = MoleAnimator.PopScale(1f);
        Assert.That(result, Is.EqualTo(1.0f).Within(0.001f),
            "Pop ends at 1.0 — back to normal scale");
    }

    [Test]
    public void PopScale_IsEaseOut_StaysHighEarly()
    {
        // ease-out quadratic: decays fast early, slower later.
        // At p=0.25: eased = 1 - (0.75)² = 0.4375
        // pop = Lerp(1.3, 1.0, 0.4375) = 1.3*0.5625 + 1.0*0.4375
        //     = 0.73125 + 0.4375 = 1.16875
        float result = MoleAnimator.PopScale(0.25f);
        Assert.That(result, Is.EqualTo(1.16875f).Within(0.0001f),
            "At 25% progress, pop should be ~1.169 (ease-out keeps it high early)");
    }

    [Test]
    public void PopScale_IsEaseOut_MidpointBelowLinear()
    {
        // Linear midpoint would be 1.15, ease-out should be lower at p=0.5
        float result = MoleAnimator.PopScale(0.5f);
        // eased = 1 - (0.5)² = 0.75
        // pop = Lerp(1.3, 1.0, 0.75) = 1.3*0.25 + 1.0*0.75 = 0.325 + 0.75 = 1.075
        Assert.That(result, Is.EqualTo(1.075f).Within(0.0001f),
            "Ease-out at 50% should be ~1.075 (faster than linear 1.15)");
    }

    [Test]
    public void PopScale_AtThreeQuarters_NearlyNormal()
    {
        float result = MoleAnimator.PopScale(0.75f);
        // eased = 1 - (0.25)² = 0.9375
        // pop = Lerp(1.3, 1.0, 0.9375) = 1.3*0.0625 + 1.0*0.9375 = 0.08125 + 0.9375 = 1.01875
        Assert.That(result, Is.EqualTo(1.01875f).Within(0.0001f),
            "At 75% progress, pop should be nearly back to 1.0");
    }

    [Test]
    public void PopScale_ClampsNegativeToMax()
    {
        float result = MoleAnimator.PopScale(-1f);
        Assert.That(result, Is.EqualTo(1.3f).Within(0.001f),
            "Negative progress clamps to 0, result = max pop 1.3");
    }

    [Test]
    public void PopScale_ClampsOverOneToNormal()
    {
        float result = MoleAnimator.PopScale(2f);
        Assert.That(result, Is.EqualTo(1.0f).Within(0.001f),
            "Progress > 1 clamps to 1, result = normal 1.0");
    }

    // --- Symmetry ---

    [Test]
    public void RiseAndSink_AreSymmetric()
    {
        Assert.That(
            MoleAnimator.RiseScale01(0f), Is.EqualTo(MoleAnimator.SinkScale01(1f)).Within(0.001f),
            "Rise start (0.2) must equal Sink end (0.2)");
        Assert.That(
            MoleAnimator.RiseScale01(1f), Is.EqualTo(MoleAnimator.SinkScale01(0f)).Within(0.001f),
            "Rise end (1.0) must equal Sink start (1.0)");
    }

    [Test]
    public void RiseAndSink_Complementary()
    {
        // For any t, Rise(t) + Sink(t) should approach 1.2
        // Rise(t) = 0.2 + 0.8*t, Sink(t) = 1 - 0.8*t
        // Sum = 0.2 + 0.8t + 1 - 0.8t = 1.2
        for (int i = 0; i <= 10; i++)
        {
            float t = i * 0.1f;
            float sum = MoleAnimator.RiseScale01(t) + MoleAnimator.SinkScale01(t);
            Assert.That(sum, Is.EqualTo(1.2f).Within(0.001f),
                $"Rise({t:F1}) + Sink({t:F1}) should equal 1.2");
        }
    }
}
