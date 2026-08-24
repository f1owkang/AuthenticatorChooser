using System.Text;

namespace PasskeyPick.Tests;

public class GpgToolsTests: IDisposable {

    private readonly List<string> tempFiles = [];

    public void Dispose() {
        foreach (string path in tempFiles) {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void ParseSocketFile_StandardVariant() {
        byte[] nonce = Enumerable.Range(1, 16).Select(i => (byte) i).ToArray();
        string path = writeTemp(Encoding.UTF8.GetBytes("54321").Concat(nonce).ToArray());

        var parsed = GpgTools.parseSocketFile(path);

        Assert.NotNull(parsed);
        Assert.Equal(54321, parsed.Value.port);
        Assert.Equal(nonce, parsed.Value.nonce);
    }

    [Fact]
    public void ParseSocketFile_CygwinVariant() {
        // "!<socket >PORT s 8HEX-8HEX-8HEX-8HEX"; GnuPG prints %08x and reads the bytes back little-endian.
        string path = writeTemp("!<socket >4321 s 01020304-05060708-090a0b0c-0d0e0f10"u8.ToArray());

        var parsed = GpgTools.parseSocketFile(path);

        Assert.NotNull(parsed);
        Assert.Equal(4321, parsed.Value.port);
        Assert.Equal(
            [0x04, 0x03, 0x02, 0x01, 0x08, 0x07, 0x06, 0x05, 0x0c, 0x0b, 0x0a, 0x09, 0x10, 0x0f, 0x0e, 0x0d],
            parsed.Value.nonce);
    }

    [Theory]
    [InlineData("0")]      // port too small
    [InlineData("65536")]  // port too large
    [InlineData("abc")]    // not a number
    public void ParseSocketFile_RejectsBadPorts(string portText) {
        string path = writeTemp(Encoding.UTF8.GetBytes(portText).Concat(new byte[16]).ToArray());
        Assert.Null(GpgTools.parseSocketFile(path));
    }

    [Fact]
    public void ParseSocketFile_RejectsTruncatedFile() {
        string path = writeTemp("1234"u8.ToArray()); // shorter than the 16-byte nonce alone
        Assert.Null(GpgTools.parseSocketFile(path));
    }

    [Fact]
    public void ParseSocketFile_RejectsMissingFile() =>
        Assert.Null(GpgTools.parseSocketFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    private string writeTemp(byte[] content) {
        string path = Path.Combine(Path.GetTempPath(), $"socket-{Guid.NewGuid():N}");
        File.WriteAllBytes(path, content);
        tempFiles.Add(path);
        return path;
    }

}
