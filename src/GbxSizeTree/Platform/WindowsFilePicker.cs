using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace GbxSizeTree.Cli.Platform;

/// <summary>Uses the native Windows open dialog to select a Trackmania map file.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFilePicker : IFilePicker
{
    private const int BufferSize = 32 * 1024;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnNoChangeDir = 0x00000008;

    public string? PickMapFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var fileBuffer = new StringBuilder(BufferSize);
            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Filter = "Trackmania maps\0*.Map.Gbx\0All files\0*.*\0\0",
                File = fileBuffer,
                MaxFile = fileBuffer.Capacity,
                InitialDirectory = GetInitialDirectory(),
                Title = "Select a Trackmania map",
                Flags = OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir,
            };

            return GetOpenFileName(ref dialog) ? fileBuffer.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetInitialDirectory()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        // Trackmania map naming/location follows docs/FORMAT-NOTES.md.
        var maps = Path.Combine(profile, "Documents", "Trackmania", "Maps");
        return Directory.Exists(maps) ? maps : profile;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW",
        ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName openFileName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Filter;
        [MarshalAs(UnmanagedType.LPWStr)] public string? CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)] public StringBuilder? File;
        public int MaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public StringBuilder? FileTitle;
        public int MaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TemplateName;
        public IntPtr Reserved;
        public int ReservedValue;
        public int FlagsEx;
    }
}
