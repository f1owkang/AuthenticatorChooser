using PasskeyPick.Windows11;

namespace PasskeyPick.Tests;

public class WindowTrustTests {

    [Fact]
    public void TrustedSystemBinary_IsAccepted() {
        foreach (string name in new[] { "CredentialUIBroker.exe", "Consent.exe", "LogonUI.exe", "winlogon.exe" }) {
            Assert.True(WindowTrust.isTrustedProcessPath(Path.Combine(Environment.SystemDirectory, name)), name);
        }
    }

    [Fact]
    public void AllowListIsCaseInsensitive() =>
        Assert.True(WindowTrust.isTrustedProcessPath(Path.Combine(Environment.SystemDirectory, "credentialuibroker.EXE")));

    [Fact]
    public void UntrustedNameUnderSystem32_IsRejected() =>
        Assert.False(WindowTrust.isTrustedProcessPath(Path.Combine(Environment.SystemDirectory, "notepad.exe")));

    [Fact]
    public void TrustedNameOutsideSystem32_IsRejected() {
        // A planted binary with a trusted name but in a user-writable directory must not pass.
        string planted = Path.Combine(Path.GetTempPath(), "CredentialUIBroker.exe");
        Assert.False(WindowTrust.isTrustedProcessPath(planted));
    }

    [Fact]
    public void TrustedNameInSystem32Subdirectory_IsRejected() =>
        Assert.False(WindowTrust.isTrustedProcessPath(Path.Combine(Environment.SystemDirectory, "drivers", "Consent.exe")));

    [Fact]
    public void NullWindowHandle_IsRejected() =>
        Assert.False(WindowTrust.isTrustedSystemProcess(IntPtr.Zero));

}
