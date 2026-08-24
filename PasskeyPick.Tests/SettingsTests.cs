namespace PasskeyPick.Tests;

public class SettingsTests {

    [Theory]
    [InlineData(int.MinValue, Settings.MAX_TTL_SECONDS)]
    [InlineData(-1,          Settings.MAX_TTL_SECONDS)]
    [InlineData(0,           0)] // 0 = keep until the program exits; a valid choice, not an error
    [InlineData(1,           1)]
    [InlineData(120,         120)]
    [InlineData(600,         600)]
    [InlineData(601,         Settings.MAX_TTL_SECONDS)]
    [InlineData(int.MaxValue, Settings.MAX_TTL_SECONDS)]
    public void NormalizeTtl_ClampsIntoZeroThroughCeiling(int input, int expected) =>
        Assert.Equal(expected, Settings.normalizeTtl(input));

    [Fact]
    public void FreshInstallDefaults_AreStrict() {
        // Static defaults (what a fresh install without settings.json gets): short TTL, forget on lock/sleep/hibernate.
        Assert.Equal(120, Settings.DEFAULT_TTL_SECONDS);
        Assert.Equal(600, Settings.MAX_TTL_SECONDS);
        Assert.Equal(Settings.DEFAULT_TTL_SECONDS, Settings.pinCacheTtlSeconds);
        Assert.True(Settings.pinClearOnLock);
        Assert.True(Settings.pinClearOnSleep);
        Assert.True(Settings.pinClearOnHibernate);
    }

}
