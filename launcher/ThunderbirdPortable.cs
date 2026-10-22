// Mozilla Thunderbird Portable Launcher
// Standalone Windows .NET Framework launcher. No installer or registry integration.
// Released as MIT-license source by the portable builder project.
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Security.Cryptography;

internal static class ThunderbirdPortable
{
    private static int Main(string[] args)
    {
        // IMPORTANT: self-test MUST come first, before missing-file checks or UI dialogs.
        if (args.Length == 1 && args[0] == "--portable-self-test")
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string app = Path.GetFullPath(Path.Combine(root, "App", "Thunderbird", "thunderbird.exe"));
            string profile = Path.GetFullPath(Path.Combine(root, "Data", "profile"));
            return app.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                && profile.StartsWith(Path.GetFullPath(Path.Combine(root, "Data")), StringComparison.OrdinalIgnoreCase)
                && !profile.StartsWith(Path.GetFullPath(Path.Combine(root, "App")), StringComparison.OrdinalIgnoreCase)
                ? 0 : 10;
        }

        if (args.Length == 1 && args[0] == "--traceguard-self-test")
            return TraceGuard.SelfTest();

        if (args.Length == 1 && args[0] == "--registrytrace-self-test")
            return RegistryTraceGuard.SelfTest();

        try { return Launch(args); }
        catch (Exception ex)
        {
            MessageBox.Show("Thunderbird Portable could not start.\r\n\r\n" + ex.Message,
                "Thunderbird Portable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Launch(string[] additionalArguments)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string exe = Path.Combine(root, "App", "Thunderbird", "thunderbird.exe");
        string profile = Path.Combine(root, "Data", "profile");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("App\\Thunderbird\\thunderbird.exe was not found. Extract the complete portable ZIP.");
        }

        // One launcher for one portable profile at a time. Avoid profile corruption.
        string name = "Local\\ThunderbirdPortable_" + ShortHash(Path.GetFullPath(profile).ToUpperInvariant());
        bool owned;
        using (Mutex mutex = new Mutex(true, name, out owned))
        {
            if (!owned)
            {
                MessageBox.Show("This Thunderbird Portable profile is already open. Close it before starting another copy.",
                    "Thunderbird Portable", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 2;
            }
            try
            {
                Directory.CreateDirectory(profile);
                var info = new ProcessStartInfo();
                info.FileName = exe;
                info.WorkingDirectory = Path.GetDirectoryName(exe);
                info.UseShellExecute = false;
                info.Arguments = "-no-remote -profile " + Quote(profile);
                foreach (string arg in additionalArguments)
                    info.Arguments += " " + Quote(arg);
                // Keep crash reporter disabled to reduce host-side crash diagnostics.
                // Some Windows integrations, browser OAuth and caches can still create host traces.
                info.EnvironmentVariables["MOZ_CRASHREPORTER_DISABLE"] = "1";
                // Capture ONLY the traces that exist before this launcher starts Thunderbird.
                // A pre-existing installed Thunderbird / Firefox profile is never cleaned.
                TraceGuard traceGuard = new TraceGuard(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Path.Combine(root, "Data", "TraceGuard", "last-run.log"), false);
                // Record narrow registry scopes before Thunderbird starts; separately log
                // new keys/values and remove only safe Thunderbird-specific path traces.
                RegistryTraceGuard registryGuard = new RegistryTraceGuard(
                    exe, Path.Combine(root, "Data", "TraceGuard", "registry.log"));
                try
                {
                    using (Process p = Process.Start(info))
                    {
                        if (p == null) throw new Exception("Windows did not start Thunderbird.");
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
                finally
                {
                    try { registryGuard.Cleanup(); }
                    finally { traceGuard.Cleanup(); }
                }
            }
            finally { mutex.ReleaseMutex(); }
        }
    }

    private static string ShortHash(string input)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < 12; i++) result.Append(hash[i].ToString("x2"));
            return result.ToString();
        }
    }

    private static string Quote(string text)
    {
        // Correct Windows process argument quoting, including trailing backslashes.
        StringBuilder s = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in text)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"')
            {
                s.Append('\\', backslashes * 2 + 1);
                s.Append('"');
                backslashes = 0;
                continue;
            }
            if (backslashes > 0) { s.Append('\\', backslashes); backslashes = 0; }
            s.Append(c);
        }
        if (backslashes > 0) s.Append('\\', backslashes * 2);
        return s.Append('"').ToString();
    }
}
