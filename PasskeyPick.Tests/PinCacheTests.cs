using System.Reflection;
using System.Runtime.InteropServices;

namespace PasskeyPick.Tests;

/// <summary>Exercises the PIN cache's encrypt/decrypt/zeroing/TTL logic. Static state is reset between tests via
/// reflection, deliberately bypassing the persisted-settings setters so no real settings.json is written.</summary>
[Collection("PinCache")]
public class PinCacheTests: IDisposable {

    private const string TEST_PIN = "密码-1234";

    public PinCacheTests() {
        PinCache.clear();
        setField("ttlSeconds", Settings.DEFAULT_TTL_SECONDS);
    }

    public void Dispose() => PinCache.clear();

    [Fact]
    public void SetThenUse_RoundtripsPinThroughBstr() {
        Assert.True(PinCache.set(TEST_PIN));
        Assert.True(PinCache.hasCached());

        string? received = null;
        Assert.True(PinCache.tryUseCachedPin(bstr => {
            received = Marshal.PtrToStringBSTR(bstr);
            return true;
        }));
        Assert.Equal(TEST_PIN, received);
    }

    [Fact]
    public void UseWithoutCache_NeverCallsTheCallback() {
        bool called = false;
        Assert.False(PinCache.tryUseCachedPin(_ => {
            called = true;
            return true;
        }));
        Assert.False(called);
        Assert.Null(PinCache.remainingSeconds());
    }

    [Fact]
    public void UsePropagatesCallbackFailure() {
        Assert.True(PinCache.set(TEST_PIN));
        Assert.False(PinCache.tryUseCachedPin(_ => false));
    }

    [Fact]
    public void Clear_ZeroesTheEncryptedBuffer() {
        Assert.True(PinCache.set(TEST_PIN));
        byte[] encrypted = getField<byte[]>("encryptedPin");
        Assert.True(encrypted.Any(b => b != 0), "the encrypted PIN buffer should hold ciphertext");

        PinCache.clear();

        Assert.All(encrypted, b => Assert.Equal(0, b));
        Assert.Null(getField<byte[]?>("encryptedPin"));
        Assert.False(PinCache.hasCached());
    }

    [Fact]
    public void ExpiredPin_IsForgottenAndNotDecrypted() {
        Assert.True(PinCache.set(TEST_PIN));
        setField("cachedAtMs", Environment.TickCount64 - (long) TimeSpan.FromHours(1).TotalMilliseconds);

        Assert.False(PinCache.hasCached());
        Assert.False(PinCache.tryUseCachedPin(_ => true));
        Assert.Null(PinCache.remainingSeconds());
    }

    [Fact]
    public void ZeroTtl_KeepsThePinUntilCleared() {
        setField("ttlSeconds", 0);
        Assert.True(PinCache.set(TEST_PIN));
        setField("cachedAtMs", Environment.TickCount64 - (long) TimeSpan.FromDays(7).TotalMilliseconds);

        Assert.True(PinCache.hasCached());
        Assert.Equal(int.MaxValue, PinCache.remainingSeconds());
        Assert.True(PinCache.tryUseCachedPin(_ => true));
    }

    [Fact]
    public void RemainingSeconds_CountsDownFromTtl() {
        setField("ttlSeconds", 120);
        Assert.True(PinCache.set(TEST_PIN));
        Assert.InRange(PinCache.remainingSeconds()!.Value, 110, 120);
    }

    private static void setField(string name, object? value) =>
        typeof(PinCache).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, value);

    private static T getField<T>(string name) =>
        (T) typeof(PinCache).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

}
