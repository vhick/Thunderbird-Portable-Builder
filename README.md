# Thunderbird True Portable Builder v1.2 with Dynamic File + Registry TraceGuard (Windows 64-bit)

**This ZIP is the *GitHub Actions builder kit*, not the finished Thunderbird application.**

A browser-only GitHub Actions builder for the **official Mozilla Thunderbird Windows x64 stable release**. Its portable launcher directs a separate email profile into `Data/profile`; the finished application needs no installation. Build runs **only when you click Run workflow**. The generated application artifact is a single ZIP retained for one day.

## What this builds

```
ThunderbirdPortable/
├── ThunderbirdPortable.exe       <- USE THIS to start Thunderbird
├── App/
│   ├── Thunderbird/
│   │   ├── thunderbird.exe        <- official Mozilla binary, no installation
│   │   ├── distribution/
│   │   │   └── policies.json      <- disable internal app binary updates
│   │   └── ...                   <- original runtime libraries and resources
│   └── AppInfo/
│       ├── appinfo.ini           <- PortableApps.com Menu metadata
│       ├── appicon.ico           <- ORIGINAL Mozilla multi-resolution icon
│       ├── appicon_16.png
│       ├── appicon_32.png
│       └── appicon_128.png
├── Data/
│   ├── profile/                 <- mail, calendars, contacts, extensions, accounts
│   └── TraceGuard/              <- last-run.log and registry.log
└── Other/
    ├── PORTABLE-README.txt
    └── Source/
        ├── ThunderbirdPortable.cs
        ├── TraceGuard.cs
        ├── RegistryTraceGuard.cs
        └── SOURCE-NOTICE.txt
```

## First-time GitHub setup (simple steps)

1. Create a **new empty GitHub repository** named `Thunderbird-Portable-Builder`.
2. Extract this builder-kit ZIP onto your computer.
3. Upload all extracted files into your repository using **Add file > Upload files**. **Preserve the `.github/workflows/build-portable.yml` path**. The `.github` folder may be hidden in some file managers, so verify GitHub displays it.
4. Commit the files to `main`.
5. Go to **Actions > Build Thunderbird Portable (Manual) > Run workflow**.
6. Leave **version = latest** and **language = en-US**, then click **Run workflow**.
7. Open the finished successful run and download the `Thunderbird-Portable-Windows-x64` artifact at the bottom.
8. Extract GitHub's artifact ZIP, then extract the `Thunderbird-Portable-VERSION-Windows-x64.zip` inside it.
9. Run **`ThunderbirdPortable.exe`** at the top of the application folder. Thunderbird will create its new profile in `Data/profile`.

If no manual-run button appears: ensure `.github/workflows/build-portable.yml` is on the default `main` branch and your repository's Actions are enabled.

## What the builder does

- Fetches the stable Mozilla release directly from Mozilla's current download endpoint. You can instead enter a pinned stable version (for example `158.0.1`).
- Validates the Windows **Authenticode signature**, checks for a Mozilla/MZLA signer, and records the SHA-256 checksum.
- Extracts the official 64-bit installer with 7-Zip **without running the installer**.
- Finds the actual original Thunderbird program files, keeping necessary runtime dependencies.
- Extracts the official icon embedded in `thunderbird.exe` into a multi-resolution ICO and creates PortableApps menu icons.
- Compiles a root Windows launcher with that original icon, runs **three headless 15-second self-tests** (launch and TraceGuard), and fails on error.
- Bundles official app files, PortableApps Menu metadata, original-icon images, a blank `Data/profile` folder and portable-source notices into a single app ZIP.
- Disables Thunderbird's internal binary updater, background updates, and telemetry using supported `distribution/policies.json` policies. **You must rebuild for security updates**.
- Performs package safety and structure checks. All source code stays available in this builder repository.

**Important**: `Data/profile` is empty when the app is built. The builder does not collect or include your installed Thunderbird messages, profiles, or credentials.

## Safely updating your portable Thunderbird

1. Fully close Thunderbird and ensure it is no longer running (including in the background).
2. **Back up your entire existing `Data` folder** before making changes.
3. Run the workflow again, using `version = latest`.
4. Extract the newly downloaded application ZIP to a **temporary folder**.
5. Replace only the **`App` and `Other` folders and `ThunderbirdPortable.exe`** in your existing portable folder with the ones from the fresh ZIP.
6. Leave the original **`Data` folder untouched**. Restart using the root launcher.

**Do not copy a new empty `Data` folder over your existing one, and do not manually downgrade a Thunderbird profile to an older application version.** A profile upgrade by Thunderbird may not be backward compatible.

## Moving to a different PC or after Windows reinstallation

1. Close Thunderbird completely, then copy the entire `ThunderbirdPortable` folder, **including `Data`**.
2. Run the root launcher on the destination computer. Your profile is loaded from the new folder's path.
3. Some email-account login flows or credentials (e.g. browser OAuth or operating-system-backed secrets) may require authentication again.
4. If you changed mail-storage locations, download folders, or external attachments to absolute paths (like `D:\Mail`), review those settings on the new computer.

Never use the same profile concurrently from two computers. Thunderbird's documentation warns that shared or actively synced profile directories can be corrupted.

## Dynamic TraceGuard v1.2: what it cleans and what it protects

TraceGuard runs **inside the root launcher**, without pop-ups or a separate installation. It records a **before-launch snapshot** of a limited set of Thunderbird-specific AppData folders and shared Mozilla ProgramData update markers, waits until the portable Thunderbird process exits, then checks what changed. Its actions are saved to `Data\TraceGuard\last-run.log` after each launch.

It tries to remove **only NEWLY CREATED and EMPTY** items:

- `%LOCALAPPDATA%\Thunderbird` (only if the whole folder is empty)
- `%APPDATA%\Thunderbird\Pending Pings`
- `%APPDATA%\Thunderbird\Crash Reports\events`, then now-empty parent folders
- `%PROGRAMDATA%\Mozilla-*\updates\<DYNAMIC-ID>` **only when** the same newly created ID also has an empty `UpdateLock-<DYNAMIC-ID>` file in the same Mozilla root (also supports `%PROGRAMDATA%\Mozilla`)
- Empty new ProgramData parent folders, only if those parents did **not** exist before launch

**File safety restrictions:** No administrator privilege request, no recursive file deletion, no deletion of populated directories, no cleanup of pre-existing folders/files, no symlink or junction traversal. ProgramData cleanup is skipped when another Mozilla program is detected at startup or after exit, or when snapshot permissions do not allow safe attribution. Without permission to delete a ProgramData lock, the file is left and the reason appears in the log. Some Windows traces (notifications, caches, file history, authentication) are outside this limited cleanup scope. This is not a security eraser or a full sandbox.

**Existing traces from the previous v1.0 launcher remain untouched.** TraceGuard cannot safely prove that pre-existing files/folders belong only to the portable copy. If you want to remove old empty remnants, first close all Thunderbird/Firefox windows and review them manually, without removing non-empty entries or other Mozilla application state.

### Simple test after upgrading

1. Keep and back up your `Data` folder, then replace only `App`, `Other`, and `ThunderbirdPortable.exe` from a newly built v1.2 package.
2. Launch the root `ThunderbirdPortable.exe`, check that your mail and settings are intact, then **exit Thunderbird completely**.
3. Open `Data\TraceGuard\last-run.log`. Look for `REMOVED`, `PRESERVE`, and `SKIP` lines. `SKIP` is intentional when a path is shared, populated, pre-existing, or lacks permission.
4. Compare your host folders again. TraceGuard only cleans traces created **after** v1.2 starts. Its behavior will never depend on the specific `4D9E...` update ID you observed.

## Registry TraceGuard v1.2: what it scans

Registry TraceGuard runs automatically from the same `ThunderbirdPortable.exe` launcher. It makes a **before-launch** snapshot of a short list of registry locations, repeats the scan after Thunderbird closes, and records changes in **`Data\TraceGuard\registry.log`**. It does **not** constantly monitor or scan the entire Windows Registry.

**Scan coverage:**

- Current user's `Software\Thunderbird` and `Software\Mozilla\Thunderbird`, including taskbar IDs, launcher markers, and crash-reporting settings.
- Current user's Windows mail client / registered apps / Thunderbird application-registration locations (**report only**).
- Narrow machine-wide `HKLM\SOFTWARE` Thunderbird, mail registration, and App Paths locations, checked in both 64-bit and 32-bit registry views (**report only**).

**Automatic cleanup is deliberately stricter than detection:**

- Only within `HKCU\Software\Thunderbird` and `HKCU\Software\Mozilla\Thunderbird`.
- Only **new values** located in known trace leaves (`Launcher`, `DllPrefetchExperiment`, `TaskBarIDs`), whose **name or string content contains this portable application's full program path**, and whose content has not changed since the post-run scan.
- Newly created **empty** known trace keys and Thunderbird root containers can also be removed, non-recursively. Unknown or populated new keys remain untouched.
- **Never changes HKLM, default-mail handlers, file associations, protocol registrations, existing values, or registry policy keys.** It never restores changed entries because doing so could reverse legitimate user changes.
- If another Firefox or Thunderbird instance is running, or registry access/snapshots are incomplete, Registry TraceGuard logs the changes but **skips cleanup**.
- Does not request elevation, run a service, or write registry changes while Thunderbird runs. It does **not** guarantee zero OS traces or undo settings the user deliberately enabled.

**Existing registry leftovers:** Entries that existed before launching v1.2 are preserved. The scan cannot reliably tell whether an older entry came from the portable version or installed Thunderbird.

### Test the registry guard

1. Build and run the updated portable ZIP once using `ThunderbirdPortable.exe` (never start the internal `thunderbird.exe` directly).
2. Close all Thunderbird windows and let the launcher finish.
3. Open `Data\TraceGuard\registry.log` and look for `DETECTED`, `REMOVED`, `PRESERVE`, and `SKIP`.
4. If entries are reported but not removed, leave them alone until we confirm whether they belong to this portable instance. This is intentional safety behavior.

## Portability limitations

This project aims for **portable profile data**, not full Windows isolation. Native Thunderbird can still create normal OS traces or caches for system integrations, external browser authentication, links opened, notifications, antivirus indexing, crash handling, etc. No host-level registry redirection/sandbox or default-mail registration is attempted. Dynamic TraceGuard handles selected new file traces and safe path-linked registry entries; other OS traces can remain. The builder does not edit any normal installed Thunderbird profile.

**Security**: `Data/profile` can contain downloaded private emails, access credentials and encryption keys. Do not share this folder publicly or back it up to an untrusted service. Keep the portable Thunderbird app updated.

## Troubleshooting

- **Windows signature validation failed:** The builder refuses to repack an untrusted or unsigned installer. Check the official download, language code and release version; do not disable this check.
- **Extraction says thunderbird.exe or omni.ja missing:** Mozilla changed the installer layout; inspect the extraction log before making changes.
- **Original icon extraction failed:** The builder will NOT replace it with a generic icon. Inspect upstream executable resources.
- **Launcher or TraceGuard self-test failed:** No final app ZIP is published. All three headless tests have 15-second timeouts to prevent CI hangs.
- **TraceGuard does not remove a ProgramData marker:** Open `Data\TraceGuard\last-run.log`. It preserves pre-existing, non-empty, unpaired or access-denied items and skips shared Mozilla state whenever another Mozilla process is detected.
- **Thunderbird says the profile is in use:** Close all copies of this portable profile first. Do not force-open an active mail profile.
- **Cannot sign in to an account:** Authentication involving your default browser, network, and an email provider is external to the portable app; retry the account's normal sign-in process.

## Project links and licenses

Thunderbird: https://www.thunderbird.net/en-US/desktop/  
Official download: https://download.mozilla.org/?product=thunderbird-latest&os=win64&lang=en-US  
Thunderbird profile documentation: https://support.mozilla.org/en-US/kb/profiles-where-thunderbird-stores-user-data  
Thunderbird enterprise policy deployment: https://enterprise.thunderbird.net/manage-updates-policies-and-customization/managing-thunderbird-policies/customizing-thunderbird-using-policies-json  

The original Thunderbird application and logo remain Mozilla/MZLA's; they are fetched only during your own GitHub Actions build and are not prepacked in this builder kit. Mozilla code is licensed under its applicable upstream licenses (including MPL 2.0). The additional `ThunderbirdPortable.cs`, `TraceGuard.cs`, `RegistryTraceGuard.cs` and scripts are provided under MIT terms (see `LICENSE-BUILDER.txt`). Not affiliated with or endorsed by Mozilla/MZLA or PortableApps.com.
