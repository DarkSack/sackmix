using SackMix.Core;

namespace SackMix.Tests;

public class AutoLevelTests
{
    private const float Tick = 0.1f;

    private static AutoLevel Run(AutoLevel level, float peak, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick) level.Update(peak, Tick);
        return level;
    }

    [Fact]
    public void NormalVoiceIsLeftAlone()
    {
        var level = Run(new AutoLevel(), 0.1f, 10);
        Assert.Equal(1f, level.Gain);
    }

    [Fact]
    public void AShoutComesDownFastToTheTarget()
    {
        var level = new AutoLevel();
        Run(level, 1f, 0.5f);
        // Within half a second a full-scale shout is heard near the normal level.
        Assert.InRange(level.Gain * 1f, AutoLevel.Target * 0.9f, AutoLevel.Target * 1.3f);
    }

    [Fact]
    public void ItComesBackSlowlyAfterTheShout()
    {
        var level = Run(new AutoLevel(), 1f, 1);
        Run(level, 0.05f, 1);
        Assert.True(level.Gain < 0.6f, $"one second later it is still careful ({level.Gain})");
        Run(level, 0.05f, 15);
        Assert.Equal(1f, level.Gain);
    }

    [Fact]
    public void SilenceChangesNothing()
    {
        var level = Run(new AutoLevel(), 1f, 1);
        var during = level.Gain;
        Run(level, 0f, 10);
        Assert.Equal(during, level.Gain);
    }

    [Fact]
    public void NeverBelowTheFloorNorAboveOne()
    {
        var level = new AutoLevel();
        for (var i = 0; i < 300; i++)
        {
            level.Update(i % 2 == 0 ? 1f : 0.02f, Tick);
            Assert.InRange(level.Gain, AutoLevel.Floor, 1f);
        }
    }

    [Fact]
    public void ReportsOnlyChangesWorthSending()
    {
        var level = new AutoLevel();
        Assert.False(level.Update(0.1f, Tick));
        Assert.True(level.Update(1f, Tick));
        Assert.False(level.Update(float.NaN, Tick));
    }

    [Fact]
    public void ResetGivesTheLevelBack()
    {
        var level = Run(new AutoLevel(), 1f, 1);
        level.Reset();
        Assert.Equal(1f, level.Gain);
        Assert.False(level.Update(0.1f, Tick));
    }
}
