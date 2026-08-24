using System.Windows.Forms;
using PasskeyPick.Windows11;

namespace PasskeyPick.Tests;

/// <summary>End-to-end regression for the native UIA BSTR path: a real WinForms password box on an STA thread stands
/// in for the FIDO PIN field (UIA needs a message loop on the window-owning thread). Guards, among other things, the
/// hand-written COM interface GUIDs in <see cref="NativeUia"/>.</summary>
[Collection("PinCache")]
public class NativeUiaTests {

    [Fact]
    public void SetPasswordValue_FillsPasswordBoxThroughBstr() {
        using var ready  = new ManualResetEventSlim();
        IntPtr  formHwnd = IntPtr.Zero;
        string? boxText  = null;

        var uiThread = new Thread(() => {
            var form = new Form();
            var box  = new TextBox { UseSystemPasswordChar = true };
            form.Controls.Add(box);
            form.Load += (_, _) => {
                formHwnd = form.Handle;
                ready.Set();
            };
            Application.Run(form);
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "test window did not open");

        try {
            Assert.True(PinCache.set("hunter2"));
            Assert.True(PinCache.tryUseCachedPin(bstr => NativeUia.setPasswordValue(formHwnd, bstr)));

            // Read the box content back on its owning thread.
            var read = new ManualResetEventSlim();
            Form? f = Form.FromHandle(formHwnd) as Form;
            Assert.NotNull(f);
            f!.BeginInvoke(() => {
                boxText = ((TextBox) f.Controls[0]).Text;
                read.Set();
            });
            Assert.True(read.Wait(TimeSpan.FromSeconds(10)), "could not read back the password box");
            Assert.Equal("hunter2", boxText);
        } finally {
            PinCache.clear();
            Form.FromHandle(formHwnd)?.BeginInvoke(() => Application.ExitThread());
            Assert.True(uiThread.Join(TimeSpan.FromSeconds(10)), "test window thread did not exit");
        }
    }

}
