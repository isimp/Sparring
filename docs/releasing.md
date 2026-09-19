# Releasing

Pushing builds the mod. Releasing is a manual workflow that publishes to GitHub and to [Hexium](https://valheim.hexium.gg/).

## One-time setup

The icon lives at `packaging/icon.png`: a 256x256 PNG, committed to the repository. The release workflow stops if it is missing or the wrong size.

Create an API token in the Hexium team settings (the settings icon next to your username, then the bottom of the page) and store it in this repository as the secret `HEXIUM_TOKEN`. The release workflow checks for it before building.

## Cutting a release

1. Set the new version in `Sparring.csproj` (`<Version>`), `src/Plugin.cs` (the `BepInPlugin` attribute) and `packaging/manifest.json` (`version_number`). The workflow checks that all three agree.
2. Add a CHANGELOG entry. It ships in the package and appears on the mod page.
3. Push, and wait for the Build workflow to pass.
4. Run the Release workflow from the Actions tab with the version (without `v`), optional release notes, the draft flag, the Hexium team and the category slugs.

The workflow then runs these steps in order:

| Step | |
|---|---|
| Version check | All three version sources match the input. |
| Icon check | `packaging/icon.png` exists. |
| Token check | `HEXIUM_TOKEN` is set. |
| Build | Against `lib/`, so no game install is needed. |
| Package | `Sparring-X.Y.Z.zip` with the manifest, icon, README, CHANGELOG, LICENSE and the DLL. |
| Package check | Hexium's rules: required files at the root, icon exactly 256x256, name of letters, digits and underscores, description of at most 256 characters. |
| GitHub release | Tags `vX.Y.Z` and attaches the zip and the DLL. |
| Hexium | Uploads the package and submits the version. |

If the Hexium step fails, fix the cause and run the workflow again with the same version. The GitHub release is updated rather than duplicated. Hexium refuses a version that already exists.

## Install location and compatibility

Sparring is client-side. Players near a duel need it as well for creatures to ignore the fighters, which is worth stating on the mod page.

`Link.ProtocolSince` in `src/Link.cs` is the oldest version a server running Sparring admits. Raise it, together with `Link.Protocol`, only when the messages between clients change.

## How the publish works

`tools/publish-hexium.py` uses the standard library only:

1. `POST /api/experimental/usermedia/initiate-upload/` returns an upload UUID and a presigned URL per part.
2. `PUT` each part, keeping the returned `ETag`.
3. `POST /api/experimental/usermedia/{uuid}/finish-upload/` with those ETags.
4. `POST /api/experimental/submission/submit/` with `author_name` set to the team and `communities` set to `["valheim"]`.

The endpoints match Thunderstore's API. For authorization the script tries `Bearer` first and falls back to `Token`, printing which one the server accepted.

It can also be run by hand:

```bash
HEXIUM_TOKEN=... python3 tools/publish-hexium.py dist/Sparring-0.1.0.zip --team isimp --categories "PvP,Combat,Mechanics,Open Source,Valheim 1.0"
python3 tools/publish-hexium.py dist/Sparring-0.1.0.zip --check-only   # validate only
```

Valid category slugs are listed at `https://hexium.gg/api/experimental/community/valheim/category/`.

## After a game update

Regenerate the reference stubs, then rebuild and commit `lib/`:

```powershell
pwsh tools/strip-references.ps1
```

The script reads the installed Valheim and BepInEx, replaces every method body with `throw null` and writes the result to `lib/`. The stubs can be compiled against but not run. If you add a reference to `Sparring.csproj`, add it to the list in the script as well.
