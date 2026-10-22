// Thunderbird Portable TraceGuard v1.2
// Conservative snapshot-based cleanup of newly created, EMPTY Thunderbird remnants.
// No recursive file deletion, no registry edits, no elevation, no hard-coded installation hashes.
// Code is MIT licensed as part of the Thunderbird Portable Builder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class TraceGuard
{
    private readonly string roaming;
    private readonly string local;
    private readonly string programData;
    private readonly string logFile;
    private readonly bool testMode;
    private readonly HashSet<string> oldAppDirs = NewSet();
    private readonly HashSet<string> oldUpdateDirs = NewSet();
    private readonly HashSet<string> oldLockFiles = NewSet();
    private readonly HashSet<string> oldMozillaRoots = NewSet();
    private readonly HashSet<string> oldUpdatesParents = NewSet();
    private readonly List<string> messages = new List<string>();
    private bool programDataSnapshotOK;
    private readonly bool otherMozillaInitiallyRunning;

    private static HashSet<string> NewSet() { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

    internal TraceGuard(string roamingRoot, string localRoot, string programDataRoot, string logDestination, bool isTest)
    {
        roaming = roamingRoot ?? "";
        local = localRoot ?? "";
        programData = programDataRoot ?? "";
        logFile = logDestination;
        testMode = isTest;
        otherMozillaInitiallyRunning = !testMode && IsMozillaRunning();
        foreach (string dir in AppPaths())
            if (Directory.Exists(dir)) oldAppDirs.Add(Path.GetFullPath(dir));
        try
        {
            CaptureProgramData(oldMozillaRoots, oldUpdatesParents, oldUpdateDirs, oldLockFiles);
            programDataSnapshotOK = true;
        }
        catch (Exception e)
        {
            programDataSnapshotOK = false;
            Log("SKIP ProgramData snapshot: " + e.GetType().Name + " " + e.Message);
        }
        Log("TraceGuard v1.2; before-launch snapshot complete. No existing paths will be deleted.");
    }

    private IEnumerable<string> AppPaths()
    {
        string rr = Path.Combine(roaming, "Thunderbird");
        yield return Path.Combine(rr, "Crash Reports", "events");
        yield return Path.Combine(rr, "Crash Reports");
        yield return Path.Combine(rr, "Pending Pings");
        yield return rr;
        yield return Path.Combine(local, "Thunderbird");
    }

    private static bool IsMozillaRunning()
    {
        // Never delete a shared Mozilla update marker when Firefox or another
        // Thunderbird instance is observed. Conservatively skip on inspection failure.
        try
        {
            foreach (string name in new string[] { "firefox", "thunderbird" })
            {
                Process[] processes = Process.GetProcessesByName(name);
                if (processes.Length != 0)
                {
                    foreach (Process p in processes) p.Dispose();
                    return true;
                }
            }
            return false;
        }
        catch { return true; }
    }

    private static bool IsMozillaRoot(string name)
    {
        if (string.Equals(name, "Mozilla", StringComparison.OrdinalIgnoreCase)) return true;
        if (!name.StartsWith("Mozilla-", StringComparison.OrdinalIgnoreCase)) return false;
        Guid g;
        return Guid.TryParse(name.Substring(8), out g);
    }

    private static bool IsHash(string name)
    {
        return Regex.IsMatch(name, "^[A-Fa-f0-9]{8,32}$", RegexOptions.CultureInvariant);
    }

    private static bool IsLink(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private void CaptureProgramData(HashSet<string> roots, HashSet<string> updatesParents, HashSet<string> updateDirs, HashSet<string> lockFiles)
    {
        if (string.IsNullOrEmpty(programData) || !Directory.Exists(programData))
            throw new DirectoryNotFoundException("PROGRAMDATA is unavailable");
        if (IsLink(programData)) throw new IOException("PROGRAMDATA is a junction; declining cleanup");
        foreach (string root in Directory.GetDirectories(programData))
        {
            if (!IsMozillaRoot(Path.GetFileName(root)) || IsLink(root)) continue;
            roots.Add(Path.GetFullPath(root));
            string updates = Path.Combine(root, "updates");
            if (Directory.Exists(updates) && !IsLink(updates))
            {
                updatesParents.Add(Path.GetFullPath(updates));
                foreach (string sub in Directory.GetDirectories(updates))
                {
                    if (IsHash(Path.GetFileName(sub)) && !IsLink(sub)) updateDirs.Add(Path.GetFullPath(sub));
                }
            }
            foreach (string file in Directory.GetFiles(root, "UpdateLock-*"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("UpdateLock-", StringComparison.OrdinalIgnoreCase)
                    && IsHash(name.Substring("UpdateLock-".Length)) && !IsLink(file))
                    lockFiles.Add(Path.GetFullPath(file));
            }
        }
    }

    internal void Cleanup()
    {
        Log("Post-exit cleanup starting. Only newly created EMPTY items may be removed.");
        bool otherMozillaNow = !testMode && IsMozillaRunning();
        // These names are Thunderbird-specific, but if another Thunderbird is running
        // we cannot confidently attribute freshly created AppData folders.
        bool otherThunderbirdNow = !testMode && HasThunderbirdProcess();
        if (otherThunderbirdNow)
            Log("SKIP Thunderbird AppData: Thunderbird process is still running.");
        else
            CleanupAppData();

        if (otherMozillaInitiallyRunning || otherMozillaNow)
            Log("SKIP ProgramData: another Mozilla application may own shared update state.");
        else if (!programDataSnapshotOK)
            Log("SKIP ProgramData: before-launch snapshot was incomplete.");
        else
            CleanupProgramData();
        SaveLog();
    }

    private static bool HasThunderbirdProcess()
    {
        try
        {
            Process[] all = Process.GetProcessesByName("thunderbird");
            bool found = all.Length != 0;
            foreach (Process p in all) p.Dispose();
            return found;
        }
        catch { return true; }
    }

    private void CleanupAppData()
    {
        foreach (string path in AppPaths())
        {
            if (oldAppDirs.Contains(Path.GetFullPath(path)))
            {
                Log("PRESERVE existed before launch: " + path);
                continue;
            }
            // No recursive removal: if any content exists, preserve it.
            DeleteEmptyDirectory(path, "Thunderbird AppData", Path.Combine(
                path.StartsWith(roaming, StringComparison.OrdinalIgnoreCase) ? roaming : local, ""));
        }
    }

    private void CleanupProgramData()
    {
        HashSet<string> roots = NewSet();
        HashSet<string> updateDirs = NewSet();
        HashSet<string> updatesParents = NewSet();
        HashSet<string> locks = NewSet();
        try { CaptureProgramData(roots, updatesParents, updateDirs, locks); }
        catch (Exception e) { Log("SKIP ProgramData changed during run: " + e.Message); return; }

        // An empty UpdateLock and an empty updates/<dynamic ID> directory BOTH have
        // to have appeared this session in the SAME Mozilla root with SAME hash.
        // A single lock or directory is not enough evidence to clean a shared root.
        foreach (string updateDir in updateDirs)
        {
            if (oldUpdateDirs.Contains(updateDir)) continue;
            string root = Directory.GetParent(Directory.GetParent(updateDir).FullName).FullName;
            string hash = Path.GetFileName(updateDir);
            string lockFile = Path.Combine(root, "UpdateLock-" + hash);
            if (!locks.Contains(lockFile) || oldLockFiles.Contains(lockFile))
            {
                Log("PRESERVE unpaired or pre-existing Mozilla update ID: " + hash);
                continue;
            }
            try
            {
                if (!IsEntireTreeEmpty(updateDir, root))
                {
                    Log("PRESERVE non-empty or unsafe Mozilla update ID: " + hash);
                    continue;
                }
                if (!File.Exists(lockFile) || IsLink(lockFile) || new FileInfo(lockFile).Length != 0)
                {
                    Log("PRESERVE non-empty/unsafe UpdateLock for ID: " + hash);
                    continue;
                }
                // Cleanup is bounded to exactly this newly created ID; never touch
                // any existing hash directory or other Mozilla application state.
                DeleteEmptyTree(updateDir, root);
                if (!Directory.Exists(updateDir))
                {
                    File.Delete(lockFile);
                    Log("REMOVED newly created empty UpdateLock: " + lockFile);
                    string updatesParent = Path.Combine(root, "updates");
                    // Remove only empty intermediate parents created during this run.
                    if (!oldUpdatesParents.Contains(updatesParent))
                        DeleteEmptyDirectory(updatesParent, "new Mozilla updates parent", root);
                    if (!oldMozillaRoots.Contains(root))
                        DeleteEmptyDirectory(root, "new Mozilla root", programData);
                }
            }
            catch (Exception e)
            {
                Log("SKIP protected ProgramData item: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    private static bool IsUnder(string path, string root)
    {
        string full = Path.GetFullPath(path);
        string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafePath(string path, string boundary)
    {
        if (!IsUnder(path, boundary)) return false;
        for (string current = Path.GetFullPath(path); IsUnder(current, boundary); current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) && IsLink(current)) return false;
        }
        return !IsLink(boundary);
    }

    private static bool IsEntireTreeEmpty(string dir, string boundary)
    {
        if (!Directory.Exists(dir) || !IsSafePath(dir, boundary)) return false;
        if (Directory.GetFiles(dir).Length != 0) return false;
        foreach (string child in Directory.GetDirectories(dir))
            if (!IsEntireTreeEmpty(child, boundary)) return false;
        return true;
    }

    private void DeleteEmptyTree(string dir, string boundary)
    {
        if (!IsEntireTreeEmpty(dir, boundary)) return;
        foreach (string sub in Directory.GetDirectories(dir)) DeleteEmptyTree(sub, boundary);
        DeleteEmptyDirectory(dir, "new Mozilla update ID", boundary);
    }

    private void DeleteEmptyDirectory(string path, string label, string boundary)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            if (!IsSafePath(path, boundary)) { Log("PRESERVE junction/unsafe path: " + path); return; }
            if (Directory.GetFileSystemEntries(path).Length != 0)
            {
                Log("PRESERVE non-empty " + label + ": " + path);
                return;
            }
            Directory.Delete(path, false);
            Log("REMOVED newly created empty " + label + ": " + path);
        }
        catch (Exception e)
        {
            Log("SKIP access or race: " + path + " (" + e.GetType().Name + ")");
        }
    }

    private void Log(string text)
    {
        messages.Add(DateTime.UtcNow.ToString("u") + " " + text);
    }

    private void SaveLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            File.WriteAllLines(logFile, messages.ToArray(), Encoding.UTF8);
        }
        catch { /* Logging never prevents Thunderbird use */ }
    }

    // Runner-only integration regression: no real Windows user profile is touched.
    internal static int SelfTest()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "TB-TraceGuard-Test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string roaming = Path.Combine(baseDir, "Roaming");
            string local = Path.Combine(baseDir, "Local");
            string program = Path.Combine(baseDir, "ProgramData");
            Directory.CreateDirectory(program);
            string old = Path.Combine(roaming, "Thunderbird", "Pending Pings");
            Directory.CreateDirectory(old); // pre-existing, must remain untouched
            string moz = Path.Combine(program, "Mozilla-1de4eec8-1241-4177-a864-e594e8d1fb38");
            string oldHash = "AAAAAAAAAAAAAAAA";
            Directory.CreateDirectory(Path.Combine(moz, "updates", oldHash));
            File.WriteAllBytes(Path.Combine(moz, "UpdateLock-" + oldHash), new byte[0]);
            TraceGuard guard = new TraceGuard(roaming, local, program, Path.Combine(baseDir, "Data", "TraceGuard", "last-run.log"), true);
            Directory.CreateDirectory(Path.Combine(roaming, "Thunderbird", "Crash Reports", "events"));
            Directory.CreateDirectory(Path.Combine(local, "Thunderbird"));
            string newHash = "B19C04C8DD272EFD";
            Directory.CreateDirectory(Path.Combine(moz, "updates", newHash));
            File.WriteAllBytes(Path.Combine(moz, "UpdateLock-" + newHash), new byte[0]);
            string unpaired = "CCCCCCCCCCCCCCCC";
            File.WriteAllBytes(Path.Combine(moz, "UpdateLock-" + unpaired), new byte[0]);
            string filled = "DDDDDDDDDDDDDDDD";
            string filledDir = Path.Combine(moz, "updates", filled);
            Directory.CreateDirectory(filledDir);
            File.WriteAllText(Path.Combine(filledDir, "IMPORTANT.txt"), "must preserve");
            File.WriteAllBytes(Path.Combine(moz, "UpdateLock-" + filled), new byte[0]);
            guard.Cleanup();
            if (!Directory.Exists(old)) return 11;
            if (!Directory.Exists(Path.Combine(moz, "updates", oldHash))) return 12;
            if (Directory.Exists(Path.Combine(moz, "updates", newHash))) return 13;
            if (File.Exists(Path.Combine(moz, "UpdateLock-" + newHash))) return 14;
            if (!File.Exists(Path.Combine(moz, "UpdateLock-" + unpaired))) return 15;
            if (!File.Exists(Path.Combine(filledDir, "IMPORTANT.txt"))) return 16;
            if (Directory.Exists(Path.Combine(local, "Thunderbird"))) return 17;
            if (Directory.Exists(Path.Combine(roaming, "Thunderbird", "Crash Reports"))) return 18;
            if (!File.Exists(Path.Combine(baseDir, "Data", "TraceGuard", "last-run.log"))) return 19;
            return 0;
        }
        catch { return 99; }
        finally
        {
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }
}
