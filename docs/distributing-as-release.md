# Distributing a plugin as a GitHub Release ZIP (closed source)

OriathHub plugins are normally installed from source: the Plugins Marketplace clones your repo and runs `dotnet build` on the user's machine. That requires you to publish your **source code** and requires the user to have the **.NET 10 SDK** installed.

If you don't want to publish source, you can instead ship the **already-compiled** plugin as a `.zip` attached to a GitHub **Release**. The Marketplace detects this automatically: when the repo you paste has a latest release carrying a `.zip` asset, it downloads and extracts that release straight into the host's `Plugins/` folder — no clone, no build, **no SDK needed by the user**.

This guide covers everything needed to make that work.

---

## How a user installs it

In the Marketplace's custom-install box (the URL field at the top, next to **Download**), the user pastes your repository URL:

```
https://github.com/<owner>/<repo>
```

The Marketplace checks `https://api.github.com/repos/<owner>/<repo>/releases/latest`:

- **Latest release has a `.zip` asset** → installs from that release (this flow).
- **No release / no `.zip` asset** → falls back to clone + build (the source flow).

So the *same box* handles both; you don't publish to the catalog, and there is no separate button — shipping a release zip is what opts your repo into this flow.

> Only **public** repositories are supported. Release assets are downloaded anonymously; private repos are not supported.

---

## 1. Name the repository to match your plugin DLL

The host loads a plugin from `Plugins/<FolderName>/<FolderName>*.dll`. For a release install the **install folder name is your repository name**, so:

> The repository name must be a prefix of your plugin DLL's file name.

Example: repo **`RitualPlugin`** shipping **`RitualPlugin.dll`** → installs to `Plugins/RitualPlugin/RitualPlugin.dll` ✓.

The repo doesn't need any source, a `.csproj`, or the SDK `nupkg` — none of the "marketplace readiness" checks for the source flow apply here. It only needs Releases.

## 2. Build your plugin

Build in **Release** and gather the output your plugin needs at runtime:

```powershell
dotnet build PATH\TO\MyPlugin.csproj -c Release
```

The build produces (and the `CopyToHostPluginsDir`/asset targets stage) a self-contained plugin folder: your plugin DLL, any extra dependency DLLs you bundle, and asset subfolders such as `textures/`. **That folder's contents are what you zip.**

Do **not** include the assemblies the host already ships (`OriathHub`, `GameOffsets`, ImGui, Coroutine, Newtonsoft.Json, ImageSharp) — see [Gotchas](gotchas.md).

## 3. Build the ZIP

Zip the **contents of your built `Plugins/<Name>/` folder**. Two layouts are accepted:

```
# A) files at the root of the zip            # B) a single top-level folder
MyPlugin.zip                                  MyPlugin.zip
 ├─ MyPlugin.dll                               └─ MyPlugin/
 ├─ SomeDependency.dll                              ├─ MyPlugin.dll
 └─ textures/                                       ├─ SomeDependency.dll
      └─ icon.png                                   └─ textures/
                                                         └─ icon.png
```

Layout **B** is flattened automatically on install (a lone top-level folder is unwrapped).

Rules the Marketplace enforces on extract:

| Requirement | Why |
|---|---|
| A `<RepoName>*.dll` must sit at the top level (after flattening B) | The host loader resolves `<folder>*.dll`; without it the plugin won't load. |
| Multiple files/folders are fine | Everything is copied recursively into `Plugins/<RepoName>/`. |
| Do **not** ship a top-level `config/` folder | A top-level `config/` in the zip is **skipped** on install, so the user's saved settings survive updates. |

What does **not** work: the plugin DLL nested in a subfolder *while other entries sit beside it* (e.g. `bin/MyPlugin.dll` next to a `README.md`). That isn't a single-folder zip, so it isn't flattened, and no top-level `<RepoName>*.dll` is found — the install fails with a clear message. Zip the folder *contents*, not your repo root.

## 4. Create the GitHub Release

1. Go to your repo → **Releases** → **Draft a new release**.
2. **Choose a tag** (e.g. `1.0.0` or `v1.0.0`). The tag is shown to users as the installed version **and drives update detection** — publishing a release with a newer tag is how you push an update.
3. **Attach your `.zip`** as a release asset (drag it into the "Attach binaries" area). The Marketplace picks the **first asset whose name ends in `.zip`** of the **latest** release.
4. **Publish** the release (a draft/pre-release is not returned by `releases/latest`; it must be a published, non-pre-release).

## 5. Updating

To ship an update, publish a **new release with a newer tag** and attach the new `.zip`. The Marketplace's update check (on startup, on the periodic auto-check, or via the card's **Check for update** button) compares the latest release id against the installed one. When they differ, the card shows an `[<tag>]` badge and an **Update** button that re-downloads and reinstalls — preserving the user's `config/`.

---

## Checklist

- [ ] Public GitHub repository.
- [ ] Repo name is a prefix of the plugin DLL name (`RitualPlugin` → `RitualPlugin.dll`).
- [ ] Plugin built in Release; runtime DLLs + assets gathered.
- [ ] `.zip` contains a `<RepoName>*.dll` at the root (or in a single top-level folder), plus all deps/assets, and **no** top-level `config/`.
- [ ] Published (non-draft, non-pre-release) GitHub Release with a version tag and the `.zip` attached.
- [ ] Users install by pasting `https://github.com/<owner>/<repo>` into the Marketplace.
