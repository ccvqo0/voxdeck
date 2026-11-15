using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
namespace VoxDeck.Platform;

/// <summary>
/// The virtual microphone. Windows only lets a kernel driver create a recording device, so VoxDeck installs one itself
/// (VB-CABLE, downloaded from its vendor) and can make it the system microphone while the app is running.
/// </summary>
public static class VirtualMic
{
    public static readonly string[] DriverUrls =
    {
        "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip",
        "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip",
    };
    public const string RenderName = "cable input", CaptureName = "cable output";

    // ---------- device lookup ----------
    public static bool IsCable(string name) => !string.IsNullOrEmpty(name) && (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) && name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));
    static List<(string id, string name)> Devices(DataFlow flow)
    {
        var list = new List<(string, string)>();
        if (!OperatingSystem.IsWindows()) return list;
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active)) { try { list.Add((d.ID, d.FriendlyName)); } catch { } }
        }
        catch { }
        return list;
    }
    public static (string id, string name) FindCapture() => Devices(DataFlow.Capture).FirstOrDefault(d => d.name.ToLowerInvariant().Contains(CaptureName));
    public static (string id, string name) FindRender() => Devices(DataFlow.Render).FirstOrDefault(d => d.name.ToLowerInvariant().Contains(RenderName));
    public static bool Installed => FindCapture().id != null && FindRender().id != null;

    public static string DefaultId(DataFlow flow, Role role)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { using var en = new MMDeviceEnumerator(); if (!en.HasDefaultAudioEndpoint(flow, role)) return null; using var d = en.GetDefaultAudioEndpoint(flow, role); return d.ID; }
        catch { return null; }
    }
    public static string NameOf(string id)
    {
        if (id == null || !OperatingSystem.IsWindows()) return null;
        try { using var en = new MMDeviceEnumerator(); using var d = en.GetDevice(id); return d.FriendlyName; } catch { return null; }
    }
    /// <summary>A real microphone to pass through: the given one if it still exists, else the first capture device that is not virtual.</summary>
    public static string RealMic(string preferredId)
    {
        var all = Devices(DataFlow.Capture);
        if (preferredId != null && all.Any(d => d.id == preferredId && !Audio.AudioEngine.LooksVirtual(d.name))) return preferredId;
        return all.FirstOrDefault(d => !Audio.AudioEngine.LooksVirtual(d.name)).id;
    }

    // ---------- default device (the same call the Sound control panel makes) ----------
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat(); [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod(); [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility();
    }
    /// <summary>Makes a device the default for every role (console, multimedia, communications).</summary>
    public static bool SetDefault(string deviceId)
    {
        if (deviceId == null || !OperatingSystem.IsWindows()) return false;
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigClient(); bool ok = true;
            for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(deviceId, role) >= 0;
            Marshal.ReleaseComObject(pc);
            return ok;
        }
        catch { return false; }
    }

    /// <summary>Turns the cable into the system microphone and returns the microphone that was default before (null if nothing changed).</summary>
    public static string TakeOver()
    {
        var cable = FindCapture(); if (cable.id == null) return null;
        string before = DefaultId(DataFlow.Capture, Role.Communications) ?? DefaultId(DataFlow.Capture, Role.Console);
        if (before == cable.id) return null;
        return SetDefault(cable.id) ? before ?? "" : null;
    }
    public static bool IsDefaultMic { get { var c = FindCapture(); return c.id != null && DefaultId(DataFlow.Capture, Role.Communications) == c.id; } }

    // ---------- signature check ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice; public IntPtr pFile;
        public int dwStateAction; public IntPtr hWVTStateData, pwszURLReference; public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
    }
    [DllImport("wintrust.dll")] static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    /// <summary>True when the file carries a valid Authenticode signature; publisher receives the signer name.</summary>
    public static bool VerifySignature(string path, out string publisher)
    {
        publisher = null;
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
            IntPtr pFile = Marshal.AllocHGlobal(fi.cbStruct);
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var data = new WINTRUST_DATA { cbStruct = Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /*none*/, fdwRevocationChecks = 0, dwUnionChoice = 1 /*file*/, pFile = pFile, dwProvFlags = 0x1000 /*cache only url retrieval*/ };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            }
            finally { Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile); Marshal.FreeHGlobal(pFile); }
#pragma warning disable SYSLIB0057
            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            publisher = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
            return true;
        }
        catch { return false; }
    }
    // VB-Audio signs its drivers as a sole trader: "BUREL VINCENT Entrepreneur individuel" (older packs: "Vincent Burel").
    public static bool TrustedPublisher(string p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        return p.Contains("VB-AUDIO", ic) || (p.Contains("Burel", ic) && p.Contains("Vincent", ic));
    }

    // ---------- install ----------
    public enum Step { Download, Verify, Install, Wait, Done }
    public sealed record Result(bool Ok, string Error = null, bool NeedsRestart = false, string Publisher = null);
    static string WorkDir => Path.Combine(Path.GetTempPath(), "VoxDeck-driver");

    /// <summary>Downloads the driver package from VB-Audio and unpacks it. Returns the path of the 64-bit installer.</summary>
    public static async Task<string> Download(CancellationToken ct = default)
    {
        string dir = WorkDir; try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        Exception last = null;
        foreach (var url in DriverUrls)
        {
            try
            {
                var bytes = await Library.Sources.Web.GetByteArrayAsync(url, ct);
                using var zip = new ZipArchive(new MemoryStream(bytes));
                zip.ExtractToDirectory(dir, true);
                var exe = Directory.GetFiles(dir, "VBCABLE_Setup_x64.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (exe != null) return exe;
                last = new FileNotFoundException("Installer not found in the package");
            }
            catch (Exception ex) { last = ex; }
        }
        throw last ?? new InvalidOperationException("Download failed");
    }

    /// <summary>Full one-click setup. Windows shows one administrator prompt (and, the first time, a "trust this publisher" prompt).</summary>
    public static async Task<Result> Install(Action<Step> progress = null, bool uninstall = false, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return new Result(false, "Windows only");
        try
        {
            // Windows tends to make a freshly installed audio device the default one: remember what the user had
            string outBefore = DefaultId(DataFlow.Render, Role.Multimedia), outComm = DefaultId(DataFlow.Render, Role.Communications), micBefore = DefaultId(DataFlow.Capture, Role.Communications);
            progress?.Invoke(Step.Download);
            string exe = await Download(ct);
            progress?.Invoke(Step.Verify);
            if (!VerifySignature(exe, out var publisher) || !TrustedPublisher(publisher)) return new Result(false, "The downloaded installer is not signed by VB-Audio — not running it.", false, publisher);
            progress?.Invoke(Step.Install);
            int code;
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, uninstall ? "-u -h" : "-i -h") { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(exe) });
                using var lim = CancellationTokenSource.CreateLinkedTokenSource(ct); lim.CancelAfter(TimeSpan.FromMinutes(4));
                await p.WaitForExitAsync(lim.Token); code = p.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new Result(false, "cancelled"); }
            progress?.Invoke(Step.Wait);
            bool want = !uninstall, have = false;
            for (int i = 0; i < 40; i++) { have = Installed; if (have == want) break; await Task.Delay(500, ct); }
            if (!uninstall)
            {
                string cabIn = FindRender().id, cabOut = FindCapture().id;
                if (cabIn != null && outBefore != null && outBefore != cabIn && DefaultId(DataFlow.Render, Role.Multimedia) == cabIn) SetDefault(outBefore);
                if (cabOut != null && micBefore != null && micBefore != cabOut && DefaultId(DataFlow.Capture, Role.Communications) == cabOut) SetDefault(micBefore);
                _ = outComm;
            }
            progress?.Invoke(Step.Done);
            try { Directory.Delete(WorkDir, true); } catch { }
            if (have != want) return new Result(false, uninstall ? null : $"The installer finished (code {code}) but the device has not appeared yet.", true, publisher);
            return new Result(true, null, false, publisher);
        }
        catch (OperationCanceledException) { return new Result(false, "cancelled"); }
        catch (Exception ex) { return new Result(false, ex.Message); }
    }
}
