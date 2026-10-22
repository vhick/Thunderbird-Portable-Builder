// Thunderbird Portable Registry TraceGuard v1.2 (MIT license).
// Registry AUDIT: narrow HKCU/HKLM scopes only. HKLM + Windows mail integrations are NEVER edited.
// Registry CLEANUP: only new, unchanged, portable-path-linked values in three known HKCU
// Thunderbird trace leaves, plus newly created EMPTY keys; never delete a registry tree.
// Fail-closed when snapshots or permissions are incomplete; avoid interfering with Firefox
// or an installed Thunderbird. No admin privileges, no background service, no persistence.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

internal sealed class RegistryTraceGuard
{
    private sealed class Watch
    {
        internal RegistryHive Hive;
        internal RegistryView View;
        internal string Path;
        internal int Depth;
        internal bool CleanupAllowed;
        internal Watch(RegistryHive hive, RegistryView view, string path, int depth, bool cleanup)
        { Hive = hive; View = view; Path = path; Depth = depth; CleanupAllowed = cleanup; }
    }

    private sealed class Snapshot
    {
        internal readonly Dictionary<string, Dictionary<string, string>> Keys =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        internal bool Complete = true;
        internal int TotalValues;
    }

    private readonly List<Watch> watches;
    private readonly Dictionary<Watch, Snapshot> before = new Dictionary<Watch, Snapshot>();
    private readonly List<string> messages = new List<string>();
    private readonly string executable;
    private readonly string binaryDirectory;
    private readonly string logFile;
    private readonly bool testing;
    private readonly bool mozillaInitiallyRunning;
    private const int MaxKeys = 500;
    private const int MaxValues = 5000;
    private const int MaxLogs = 350;

    internal RegistryTraceGuard(string portableExecutable, string logDestination)
        : this(portableExecutable, logDestination, StandardWatches(), false) { }

    private RegistryTraceGuard(string portableExecutable, string logDestination, List<Watch> targets, bool isTest)
    {
        executable = Path.GetFullPath(portableExecutable).TrimEnd('\\');
        binaryDirectory = Path.GetDirectoryName(executable).TrimEnd('\\');
        logFile = logDestination;
        watches = targets;
        testing = isTest;
        mozillaInitiallyRunning = !testing && MozillaIsRunning();
        Log("Registry TraceGuard v1.2: monitoring narrow Thunderbird-related registry scopes. HKLM and mail registrations are report-only.");
        foreach (Watch w in watches)
        {
            Snapshot snap = Capture(w);
            before[w] = snap;
            if (!snap.Complete) Log("SKIP incomplete before-snapshot: " + Describe(w));
        }
    }

    private static List<Watch> StandardWatches()
    {
        List<Watch> list = new List<Watch>();
        list.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Thunderbird", 4, true));
        list.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Mozilla\Thunderbird", 4, true));
        // Association/default-client keys are *always* audit-only.
        list.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Clients\Mail", 1, false));
        list.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, @"Software\RegisteredApplications", 0, false));
        list.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\App Paths\thunderbird.exe", 1, false));
        list.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Classes\Applications\thunderbird.exe", 2, false));
        foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            list.Add(new Watch(RegistryHive.LocalMachine, view, @"SOFTWARE\Mozilla Thunderbird", 2, false));
            list.Add(new Watch(RegistryHive.LocalMachine, view, @"SOFTWARE\Mozilla\Thunderbird", 2, false));
            list.Add(new Watch(RegistryHive.LocalMachine, view, @"SOFTWARE\Clients\Mail\Mozilla Thunderbird", 2, false));
            list.Add(new Watch(RegistryHive.LocalMachine, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\thunderbird.exe", 1, false));
        }
        return list;
    }

    private static string Describe(Watch w)
    {
        return (w.Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM") + "[" + w.View.ToString() + @"]\" + w.Path;
    }

    private static Snapshot Capture(Watch w)
    {
        Snapshot result = new Snapshot();
        try
        {
            using (RegistryKey hive = RegistryKey.OpenBaseKey(w.Hive, w.View))
            using (RegistryKey root = hive.OpenSubKey(w.Path, false))
            {
                if (root != null) ReadTree(root, w.Path, w.Depth, result);
            }
        }
        catch { result.Complete = false; }
        return result;
    }

    private static void ReadTree(RegistryKey key, string name, int remaining, Snapshot state)
    {
        if (state.Keys.Count >= MaxKeys) { state.Complete = false; return; }
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string valName in key.GetValueNames())
        {
            state.TotalValues++;
            if (state.TotalValues > MaxValues) { state.Complete = false; return; }
            values[valName] = Fingerprint(key, valName);
        }
        state.Keys[name] = values;
        if (remaining <= 0) return;
        foreach (string child in key.GetSubKeyNames())
        {
            if (!state.Complete) return;
            using (RegistryKey subkey = key.OpenSubKey(child, false))
            {
                if (subkey == null) { state.Complete = false; return; }
                ReadTree(subkey, name + "\\" + child, remaining - 1, state);
            }
        }
    }

    private static string Fingerprint(RegistryKey key, string valName)
    {
        RegistryValueKind kind = key.GetValueKind(valName);
        object value = key.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        StringBuilder b = new StringBuilder(kind.ToString()).Append(':');
        byte[] bytes = value as byte[];
        string[] many = value as string[];
        if (bytes != null) b.Append(Convert.ToBase64String(bytes));
        else if (many != null) foreach (string item in many) b.Append(item.Length).Append(':').Append(item);
        else if (value != null) b.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        using (SHA256 sha = SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())));
    }

    private static bool MozillaIsRunning()
    {
        try
        {
            foreach (string name in new string[] { "firefox", "thunderbird" })
            {
                Process[] processes = Process.GetProcessesByName(name);
                bool found = processes.Length != 0;
                foreach (Process p in processes) p.Dispose();
                if (found) return true;
            }
            return false;
        }
        catch { return true; } // Fail closed.
    }

    // The exact binary path or containing application's binary folder must appear
    // as a complete path component, not merely as an arbitrary shared substring.
    private static bool HasPath(string text, string path)
    {
        if (text == null || path.Length == 0) return false;
        int start = 0;
        while (start < text.Length)
        {
            int i = text.IndexOf(path, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            int end = i + path.Length;
            bool left = i == 0 || !IsPathCharacter(text[i - 1]);
            bool right = end == text.Length || !IsPathCharacter(text[end]);
            if (left && right) return true;
            start = i + 1;
        }
        return false;
    }

    private static bool IsPathCharacter(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '\\' || c == '/';
    }

    private bool IsClearlyPortable(RegistryKey key, string valueName)
    {
        // Never delete binary/unknown data or numeric shared settings.
        RegistryValueKind kind = key.GetValueKind(valueName);
        if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) return false;
        string data = key.GetValue(valueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return HasPath(valueName, executable) || HasPath(valueName, binaryDirectory) ||
            HasPath(data, executable) || HasPath(data, binaryDirectory);
    }

    private static bool IsTraceLeaf(string path)
    {
        return path.EndsWith(@"\Launcher", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(@"\DllPrefetchExperiment", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(@"\TaskBarIDs", StringComparison.OrdinalIgnoreCase);
    }

    internal void Cleanup()
    {
        try
        {
            bool competingMozilla = mozillaInitiallyRunning || (!testing && MozillaIsRunning());
            foreach (Watch w in watches)
            {
                Snapshot old = before[w];
                Snapshot current = Capture(w);
                if (!old.Complete || !current.Complete)
                {
                    Log("SKIP incomplete registry snapshot: " + Describe(w));
                    continue;
                }
                Audit(w, old, current);
                if (!w.CleanupAllowed) continue;
                if (competingMozilla)
                {
                    Log("SKIP registry cleanup: Firefox/other Thunderbird present: " + Describe(w));
                    continue;
                }
                RemoveNewPortableValues(w, old, current);
                RemoveNewEmptyKeys(w, old, current);
            }
        }
        catch (Exception e) { Log("SKIP registry scan unexpected error: " + e.GetType().Name); }
        finally { SaveLog(); }
    }

    private void Audit(Watch w, Snapshot old, Snapshot current)
    {
        foreach (KeyValuePair<string, Dictionary<string, string>> k in current.Keys)
        {
            if (!old.Keys.ContainsKey(k.Key)) Log("DETECTED new key: " + Describe(w) + " :: " + k.Key);
            Dictionary<string, string> oldValues;
            if (!old.Keys.TryGetValue(k.Key, out oldValues)) oldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> v in k.Value)
            {
                string oldHash;
                if (!oldValues.TryGetValue(v.Key, out oldHash))
                    Log("DETECTED new value: " + Describe(w) + " :: " + k.Key + " :: " + v.Key);
                else if (oldHash != v.Value)
                    Log("PRESERVE existing modified value: " + k.Key + " :: " + v.Key);
            }
        }
        foreach (KeyValuePair<string, Dictionary<string, string>> k in old.Keys)
        {
            if (!current.Keys.ContainsKey(k.Key)) Log("DETECTED removed key (never restore): " + k.Key);
        }
    }

    private void RemoveNewPortableValues(Watch w, Snapshot old, Snapshot current)
    {
        foreach (KeyValuePair<string, Dictionary<string, string>> k in current.Keys)
        {
            if (!IsTraceLeaf(k.Key)) continue;
            Dictionary<string, string> oldValues;
            if (!old.Keys.TryGetValue(k.Key, out oldValues)) oldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in k.Value)
            {
                if (oldValues.ContainsKey(entry.Key)) continue; // never touch existing data
                try
                {
                    using (RegistryKey hive = RegistryKey.OpenBaseKey(w.Hive, w.View))
                    using (RegistryKey opened = hive.OpenSubKey(k.Key, true))
                    {
                        if (opened == null) continue;
                        bool exists = false;
                        foreach (string name in opened.GetValueNames())
                            if (string.Equals(name, entry.Key, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                        if (!exists || Fingerprint(opened, entry.Key) != entry.Value) continue; // raced/edited after scan
                        if (!IsClearlyPortable(opened, entry.Key))
                        {
                            Log("PRESERVE unlinked/shared new value: " + k.Key + " :: " + entry.Key);
                            continue;
                        }
                        opened.DeleteValue(entry.Key, false);
                        Log("REMOVED new portable-path-linked value: " + k.Key + " :: " + entry.Key);
                    }
                }
                catch (Exception e) { Log("SKIP registry value access: " + k.Key + " (" + e.GetType().Name + ")"); }
            }
        }
    }

    private void RemoveNewEmptyKeys(Watch w, Snapshot old, Snapshot current)
    {
        // Only keys added this session, only within Thunderbird's own HKCU trees.
        // DeleteSubKey (NON-recursive), only after confirming empty; never ancestors
        // outside the watched scope. No DeleteSubKeyTree in production cleanup.
        List<string> added = new List<string>();
        foreach (string path in current.Keys.Keys)
            if (!old.Keys.ContainsKey(path)) added.Add(path);
        added.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
        foreach (string path in added)
        {
            // Do not delete unknown new keys; empty alone is not evidence of ownership.
            // Allow only known Thunderbird trace leaves and their watched root container.
            if (!IsTraceLeaf(path) &&
                !string.Equals(path, w.Path, StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(@"\Crash Reporter", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                int split = path.LastIndexOf('\\');
                if (split < 0) continue;
                string parentPath = path.Substring(0, split);
                string childName = path.Substring(split + 1);
                using (RegistryKey hive = RegistryKey.OpenBaseKey(w.Hive, w.View))
                using (RegistryKey parent = hive.OpenSubKey(parentPath, true))
                {
                    if (parent == null) continue;
                    using (RegistryKey child = parent.OpenSubKey(childName, false))
                    {
                        if (child == null || child.GetValueNames().Length != 0 || child.GetSubKeyNames().Length != 0) continue;
                    }
                    parent.DeleteSubKey(childName, false); // false = do not throw if missing; NOT recursive
                    Log("REMOVED newly created empty registry key: " + path);
                }
            }
            catch (Exception e) { Log("SKIP registry key access: " + path + " (" + e.GetType().Name + ")"); }
        }
    }

    private void Log(string message)
    {
        if (messages.Count < MaxLogs) messages.Add(DateTime.UtcNow.ToString("u") + " " + message);
        else if (messages.Count == MaxLogs) messages.Add("Log limit reached; further events omitted.");
    }

    private void SaveLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            File.WriteAllLines(logFile, messages.ToArray(), Encoding.UTF8);
        }
        catch { /* logging must never interrupt email usage */ }
    }

    // Test operates ONLY inside a random, explicitly owned HKCU key, never real Thunderbird keys.
    // Cleanup of this owned test key is separate from production's non-recursive cleanup.
    internal static int SelfTest()
    {
        string unique = "ThunderbirdPortableRegistryTraceGuardTest_" + Guid.NewGuid().ToString("N");
        string testRoot = @"Software\" + unique;
        string app = @"C:\PortableTest\ThunderbirdPortable\App\Thunderbird\thunderbird.exe";
        string logDir = Path.Combine(Path.GetTempPath(), unique);
        string log = Path.Combine(logDir, "registry.log");
        try
        {
            using (RegistryKey old = Registry.CurrentUser.CreateSubKey(testRoot + @"\Thunderbird\Launcher"))
                old.SetValue("preexisting", "leave in place", RegistryValueKind.String);
            List<Watch> items = new List<Watch>();
            items.Add(new Watch(RegistryHive.CurrentUser, RegistryView.Default, testRoot + @"\Thunderbird", 4, true));
            RegistryTraceGuard guard = new RegistryTraceGuard(app, log, items, true);
            using (RegistryKey leaf = Registry.CurrentUser.OpenSubKey(testRoot + @"\Thunderbird\Launcher", true))
            {
                leaf.SetValue(app + "|Telemetry", "1", RegistryValueKind.String);
                leaf.SetValue("new unrelated", "must stay", RegistryValueKind.String);
            }
            using (RegistryKey leaf = Registry.CurrentUser.CreateSubKey(testRoot + @"\Thunderbird\DllPrefetchExperiment"))
                leaf.SetValue(app, "1", RegistryValueKind.String);
            using (RegistryKey unused = Registry.CurrentUser.CreateSubKey(testRoot + @"\Thunderbird\Crash Reporter")) { }
            using (RegistryKey unrelated = Registry.CurrentUser.CreateSubKey(testRoot + @"\Thunderbird\UnrelatedEmpty")) { }
            guard.Cleanup();
            using (RegistryKey leaf = Registry.CurrentUser.OpenSubKey(testRoot + @"\Thunderbird\Launcher", false))
            {
                if (leaf.GetValue("preexisting", null) == null) return 11;
                if (leaf.GetValue("new unrelated", null) == null) return 12;
                if (leaf.GetValue(app + "|Telemetry", null) != null) return 13;
            }
            if (Registry.CurrentUser.OpenSubKey(testRoot + @"\Thunderbird\DllPrefetchExperiment") != null) return 14;
            if (Registry.CurrentUser.OpenSubKey(testRoot + @"\Thunderbird\Crash Reporter") != null) return 15;
            if (Registry.CurrentUser.OpenSubKey(testRoot + @"\Thunderbird\UnrelatedEmpty") == null) return 18;
            if (!File.Exists(log)) return 16;
            if (!File.ReadAllText(log).Contains("REMOVED new portable-path-linked value")) return 17;
            return 0;
        }
        catch { return 99; }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testRoot, false); } catch { }
            try { Directory.Delete(logDir, true); } catch { }
        }
    }
}
