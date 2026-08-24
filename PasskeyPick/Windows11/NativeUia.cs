using System.Runtime.InteropServices;

namespace PasskeyPick.Windows11;

/// <summary>
/// Minimal hand-written COM interop for native UI Automation, used for exactly one job: setting the FIDO PIN field's
/// value from an unmanaged <c>BSTR</c> via <c>IUIAutomationValuePattern::SetValue</c>, so the plaintext never becomes
/// a managed string on the GC heap (the managed <c>ValuePattern.SetValue(string)</c> would force one). Only a handful
/// of methods are ever called; the unused vtable slots before them are declared anyway, because COM interop resolves
/// methods by vtable offset and the offsets must match UIAutomationClient.h.
/// </summary>
internal static class NativeUia {

    private static readonly Logger LOGGER = LogManager.GetLogger(typeof(NativeUia).FullName!);

    private const int UIA_VALUE_PATTERN_ID       = 10002; // UIA_ValuePatternId
    private const int UIA_ISPASSWORD_PROPERTY_ID = 30019; // UIA_IsPasswordPropertyId
    private const int TREE_SCOPE_DESCENDANTS     = 0x4;

    /// <summary>Finds the password field under the given top-level FIDO dialog window and sets its value from
    /// <paramref name="bstrPin"/>, a BSTR allocated by <see cref="PinCache"/> and zeroed by it after this call
    /// returns. Never throws; returns <see langword="false"/> on any COM or lookup failure (no managed fallback —
    /// the caller asks the user to type the PIN instead).</summary>
    public static bool setPasswordValue(IntPtr windowHandle, IntPtr bstrPin) {
        try {
            // The cast goes through object: a ComImport coclass has no compile-time conversion to the interface,
            // and casting the RCW at runtime performs the QueryInterface.
            var automation = (IUIAutomation) (object) new CUIAutomation();
            automation.ElementFromHandle(windowHandle, out IUIAutomationElement? root);
            if (root is null) {
                LOGGER.Warn("Native UIA found no element for the FIDO dialog window");
                return false;
            }
            automation.CreatePropertyCondition(UIA_ISPASSWORD_PROPERTY_ID, true, out IUIAutomationCondition? isPassword);
            if (isPassword is null) {
                LOGGER.Warn("Native UIA could not build the IsPassword condition");
                return false;
            }
            root.FindFirst(TREE_SCOPE_DESCENDANTS, isPassword, out IUIAutomationElement? target);
            if (target is null) {
                LOGGER.Warn("Native UIA found no password field under the FIDO dialog window");
                return false;
            }
            Guid valuePatternIid = typeof(IUIAutomationValuePattern).GUID;
            target.GetCurrentPatternAs(UIA_VALUE_PATTERN_ID, ref valuePatternIid, out IntPtr patternPtr);
            if (patternPtr == IntPtr.Zero) {
                LOGGER.Warn("The FIDO PIN field does not expose the native Value pattern");
                return false;
            }
            try {
                ((IUIAutomationValuePattern) Marshal.GetObjectForIUnknown(patternPtr)).SetValue(bstrPin);
                return true;
            } finally {
                Marshal.Release(patternPtr);
            }
        } catch (Exception e) when (e is not OutOfMemoryException) {
            LOGGER.Warn("Native UIA SetValue for the PIN field failed ({message})", e.Message);
            return false;
        }
    }

    [ComImport]
    [Guid("ff48dba4-60ef-4201-aa87-54103eef594e")] // CLSID_CUIAutomation, inbox since Windows Vista
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class CUIAutomation { }

    [ComImport]
    [Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")] // IID_IUIAutomation
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation {
        // Only ElementFromHandle and CreatePropertyCondition are used; the slots between them are declared so the
        // vtable offsets stay correct. Parameters of unused methods are IntPtr-flattened VARIANTs/pointers.
        void CompareElements(IntPtr el1, IntPtr el2, [MarshalAs(UnmanagedType.Bool)] out bool areSame);
        void CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, [MarshalAs(UnmanagedType.Bool)] out bool areSame);
        void GetRootElement(out IUIAutomationElement? root);
        void ElementFromHandle(IntPtr hwnd, out IUIAutomationElement? element);
        void ElementFromPoint(POINT pt, out IUIAutomationElement? element);
        void GetFocusedElement(out IUIAutomationElement? element);
        void GetRootElementBuildCache(IntPtr cacheRequest, out IUIAutomationElement? root);
        void ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest, out IUIAutomationElement? element);
        void ElementFromPointBuildCache(POINT pt, IntPtr cacheRequest, out IUIAutomationElement? element);
        void GetFocusedElementBuildCache(IntPtr cacheRequest, out IUIAutomationElement? element);
        void CreateTreeWalker(IntPtr condition, out IntPtr walker);
        void get_ControlViewWalker(out IntPtr walker);
        void get_ContentViewWalker(out IntPtr walker);
        void get_RawViewWalker(out IntPtr walker);
        void get_RawViewCondition(out IntPtr condition);
        void get_ControlViewCondition(out IntPtr condition);
        void get_ContentViewCondition(out IntPtr condition);
        void CreateCacheRequest(out IntPtr cacheRequest);
        void CreateTrueCondition(out IntPtr condition);
        void CreateFalseCondition(out IntPtr condition);
        void CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, out IUIAutomationCondition? condition);
    }

    [ComImport]
    [Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")] // IID_IUIAutomationElement
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement {
        // Only FindFirst and GetCurrentPatternAs are used; the slots between them are declared so the vtable offsets
        // stay correct.
        void SetFocus();
        void GetRuntimeId(out IntPtr runtimeId);
        void FindFirst(int scope, IUIAutomationCondition condition, out IUIAutomationElement? found);
        void FindAll(int scope, IUIAutomationCondition condition, out IntPtr found);
        void FindFirstBuildCache(int scope, IUIAutomationCondition condition, IntPtr cacheRequest, out IntPtr found);
        void FindAllBuildCache(int scope, IUIAutomationCondition condition, IntPtr cacheRequest, out IntPtr found);
        void BuildUpdatedCache(IntPtr cacheRequest, out IntPtr updatedElement);
        void GetCurrentPropertyValue(int propertyId, out IntPtr value);
        void GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, out IntPtr value);
        void GetCachedPropertyValue(int propertyId, out IntPtr value);
        void GetCachedPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, out IntPtr value);
        void GetCurrentPatternAs(int patternId, ref Guid riid, out IntPtr patternObject);
    }

    [ComImport]
    [Guid("352ffba8-0973-437c-a61f-f64cafd81df9")] // IID_IUIAutomationCondition (marker interface)
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition { }

    [ComImport]
    [Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9")] // IID_IUIAutomationValuePattern
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationValuePattern {
        // The BSTR arrives as a raw pointer on purpose: the plaintext lives only in that unmanaged allocation
        // (SysAllocString in PinCache) and is RtlZeroMemory'd by the caller once this returns.
        void SetValue(IntPtr bstr);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT {
        public int x;
        public int y;
    }

}
