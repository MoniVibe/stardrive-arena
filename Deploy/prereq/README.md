# Deploy/prereq/

Holds prerequisite installers that the BlackBox NSIS installer bundles + runs
before installing the game itself. Currently:

| File | Source | Purpose |
|---|---|---|
| `windowsdesktop-runtime-8.0.x-win-x64.exe` | Microsoft .NET releases | .NET 8 Desktop Runtime (x64) — required for `net8.0-windows` apps; Mars 1.51 ran on .NET Framework 4.8 (built into Windows) so this is new for the Jupiter line. |

The `.exe` files are **not tracked in git** (`.gitignore` excludes
`Deploy/prereq/*.exe`) — they're ~56 MB binaries that don't belong in the
repo. The maintainer fetches them locally before a Deploy|x64 build.

## How to refresh the runtime installer

Whenever a new .NET 8 LTS patch ships (`8.0.x` increments), refresh the local
copy. The latest version is recorded in Microsoft's release-metadata JSON:

```bash
curl -sL "https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json" \
  | py -3 -c "import json,sys; d=json.load(sys.stdin); r=d['releases'][0]; \
              print(r['release-version']); \
              [print(f['url']) for f in r['windowsdesktop']['files'] \
               if f['rid']=='win-x64' and f['name'].endswith('.exe')]"
```

Then download:

```bash
cd Deploy/prereq
rm -f windowsdesktop-runtime-8.0.*.exe
curl -sL -o windowsdesktop-runtime-8.0.<NEW_VERSION>-win-x64.exe \
  "<URL_FROM_METADATA>"
```

Update the filename reference in [BBInstaller.nsi](../BBInstaller.nsi) — the
NSIS `File` directive embeds the EXE by exact filename, so the script must
point at the file on disk.

## Why bundle vs. expect-user-to-install

.NET's apphost shows a "must install .NET Desktop Runtime" dialog when the
runtime is missing. As of .NET 8.0.26 + .NET 9 SDK build tooling, that
dialog renders with a **broken Download link** (the URL ends up as just
`&gui=true` with no base) and the "Download it now" button does nothing —
likely an SDK-side apphost-patching bug we don't control. Bundling the
runtime installer in NSIS sidesteps the broken dialog entirely: the user
gets a single guided install flow that handles the runtime as a prereq.

If/when the SDK bug is fixed in a future build, we may revisit and remove
the bundle to shrink the installer back down (~57 MB savings). The bundle
is otherwise harmless — Microsoft's runtime installer is idempotent and
exits quickly when the runtime is already installed.
