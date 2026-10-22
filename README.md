# Thunderbird True Portable Builder v1.0 (Windows 64-bit)

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
│   └── profile/                 <- mail, calendars, contacts, extensions, accounts
└── Other/
    ├── PORTABLE-README.txt
    └── Source/
        ├── ThunderbirdPortable.cs
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
- Compiles a root Windows launcher with that original icon, runs a **headless 15-second self-test**, and fails on error.
- Bundles official app files, PortableApps Menu metadata, original-icon images, a blank `Data/profile` folder and portable-source notices into a single app ZIP.
- Disables Thunderbird's internal binary updater with a supported `distribution/policies.json` policy. **You must rebuild for security updates**.
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

## Portability limitations

This project aims for **portable profile data**, not full Windows isolation. Native Thunderbird can still create normal OS traces or caches for system integrations, external browser authentication, links opened, notifications, antivirus indexing, crash handling, etc. No host-level registry redirection/sandbox, default-mail registration or forced cleanup is attempted. The builder does not edit any normal installed Thunderbird profile.

**Security**: `Data/profile` can contain downloaded private emails, access credentials and encryption keys. Do not share this folder publicly or back it up to an untrusted service. Keep the portable Thunderbird app updated.

## Troubleshooting

- **Windows signature validation failed:** The builder refuses to repack an untrusted or unsigned installer. Check the official download, language code and release version; do not disable this check.
- **Extraction says thunderbird.exe or omni.ja missing:** Mozilla changed the installer layout; inspect the extraction log before making changes.
- **Original icon extraction failed:** The builder will NOT replace it with a generic icon. Inspect upstream executable resources.
- **Launcher self-test failed:** No final app ZIP is published. Its headless self-test has a 15-second timeout to avoid repeating an earlier 35-minute CI hang.
- **Thunderbird says the profile is in use:** Close all copies of this portable profile first. Do not force-open an active mail profile.
- **Cannot sign in to an account:** Authentication involving your default browser, network, and an email provider is external to the portable app; retry the account's normal sign-in process.

## Project links and licenses

Thunderbird: https://www.thunderbird.net/en-US/desktop/  
Official download: https://download.mozilla.org/?product=thunderbird-latest&os=win64&lang=en-US  
Thunderbird profile documentation: https://support.mozilla.org/en-US/kb/profiles-where-thunderbird-stores-user-data  
Thunderbird enterprise policy deployment: https://enterprise.thunderbird.net/manage-updates-policies-and-customization/managing-thunderbird-policies/customizing-thunderbird-using-policies-json  

The original Thunderbird application and logo remain Mozilla/MZLA's; they are fetched only during your own GitHub Actions build and are not prepacked in this builder kit. Mozilla code is licensed under its applicable upstream licenses (including MPL 2.0). The additional `ThunderbirdPortable.cs` launcher and scripts are provided under MIT terms (see `LICENSE-BUILDER.txt`). Not affiliated with or endorsed by Mozilla/MZLA or PortableApps.com.
