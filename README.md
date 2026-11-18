<p align="center">
  <img src="docs/images/trag.png" width="112" alt="Trag logo">
</p>

<h1 align="center">Trag</h1>

<p align="center">
  <b>A fast, native Git client for Windows and Linux.</b><br>
  Built with Avalonia and compiled ahead of time, so it starts quickly and stays light on memory.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white" alt="Windows 10+">
  <img src="https://img.shields.io/badge/Linux-x64-FCC624?logo=linux&logoColor=black" alt="Linux x64">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/Avalonia-12-8B44AC" alt="Avalonia 12">
  <img src="https://img.shields.io/badge/NativeAOT-native%20binary-2EA44F" alt="NativeAOT">
  <img src="https://img.shields.io/badge/license-source--available-E0662B" alt="Source-available license">
</p>

<p align="center">
  <a href="https://trag.sh">Website</a> |
  <a href="https://downloads.finitysoftware.io/trag">Download</a> |
  <a href="docs/manual/README.md">User manual</a> |
  <a href="#-building-from-source">Build from source</a>
</p>

---

Trag brings your repositories, their history and your local changes together in one window, so everyday Git work
such as committing, branching, merging and syncing takes less effort. It is developed by Finity Software.

## ✨ Why Trag

- ⚡ **Native and lightweight.** Trag ships as a single NativeAOT executable. There is no Electron, no bundled browser
  and no .NET runtime to install, so it opens fast and keeps its memory footprint small.
- 🧩 **Your Git, your setup.** Trag works on top of the Git installation already on your machine. Your configuration,
  hooks and existing repositories behave exactly as they do on the command line.
- 🔐 **Credentials stay safe.** Account tokens live in the Windows Credential Manager or the Linux keyring, never in a
  plain file.
- 🖥️ **Built for the desktop.** Full keyboard support, a responsive UI on large repositories and several light and
  dark themes.

## 🚀 Features

| Area | What you get |
|---|---|
| **Repositories** | One list for all your local repositories, with cloning from your GitHub and GitLab accounts |
| **History** | The full history with a branch graph, search and line-by-line blame |
| **Changes** | Stage whole files, individual hunks or selected lines, then commit or amend |
| **Branches** | Branches and tags, merge, rebase, cherry-pick, revert and reset |
| **Conflicts** | Resolve merge conflicts block by block |
| **Stash** | Put work in progress aside and bring it back later |
| **Sync** | Fetch, pull and push, or publish a new repository to GitHub or GitLab in one step |
| **Advanced** | Submodules, Git LFS and signed commits |
| **Terminal** | A terminal in the repository folder, one click away |

## 📖 User manual

The [user manual](docs/manual/README.md) walks through every part of Trag with screenshots: adding repositories,
committing, history, branches, merging and rebasing, resolving conflicts, stashes, syncing, and accounts and settings.

## 📥 Download

Every release is available at **https://downloads.finitysoftware.io/trag** and on the
[Releases](https://github.com/finitysoftware/trag/releases) page.

**Windows.** The installer sets Trag up for the current user and does not require administrator rights. The portable ZIP
runs without installing. Settings, the repository list and account details are kept in `%LOCALAPPDATA%\Trag`.

**Linux.** The AppImage is a single file: make it executable with `chmod +x` and run it. The `tar.gz` holds the same
build to unpack anywhere and start with `./Trag`. Settings are kept in `~/.local/share/Trag`.

`SHA256SUMS.txt` next to the downloads lets you check them.

## ✅ Requirements

- Windows 10 (1809) or newer, 64-bit, or a 64-bit Linux desktop
- Git 2.43 or newer
- On Linux, the usual X11 and font libraries, and `libsecret` with a keyring service such as GNOME Keyring for account
  tokens

If Git is not found on startup, Trag reports it and lets you select `git.exe` under Settings.

## 🛠️ Building from source

Building requires the .NET SDK version pinned in `global.json` (currently 10.0.403).

```
dotnet build Trag.sln
dotnet run --project src/Trag.Desktop
```

Release builds use NativeAOT and produce a single native executable that does not require the .NET runtime on the
target machine.

**Windows.** The NativeAOT build requires the Visual Studio C++ Build Tools:

```
powershell -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

The output is written to `artifacts/publish/win-x64`. To build the installer as well, install Inno Setup 6 and run
`scripts/Build-Installer.ps1` instead. The setup file is written to `dist`.

**Linux.** The NativeAOT build requires `clang` and `zlib1g-dev`:

```
dotnet publish src/Trag.Desktop -c Release -r linux-x64 -o artifacts/publish/linux-x64
```

At runtime, Linux needs the standard X11 and font libraries, as well as `libsecret` and a keyring service such as GNOME
Keyring, which Trag uses to store account tokens.

## 🧪 Running the tests

The unit tests run quickly and have no additional requirements:

```
dotnet test Trag.sln --filter "FullyQualifiedName!~Trag.IntegrationTests"
```

The integration tests run real Git against real repositories and local servers, and take noticeably longer:

```
dotnet test tests/Trag.IntegrationTests
```

## 🗂️ Project structure

| Folder | What it holds |
|---|---|
| `src/Trag.Domain` | Rules and values, with no dependencies |
| `src/Trag.Application` | Models that hold the state of an open repository |
| `src/Trag.Infrastructure` | Integration with the outside world: Git, the file system, accounts and the network |
| `src/Trag.ViewModels` | Presentation logic, independent of any UI framework |
| `src/Trag.Desktop` | The Avalonia user interface and application startup |
| `tests` | A test project for each layer, plus the integration tests |
| `installer` | The Inno Setup script for the Windows installer |

`tests/Trag.ArchitectureTests` verifies that the layers depend on each other only in this order.

## 🤝 Contributing

Bug reports and pull requests are welcome. For larger changes, please open an issue first so the approach can be agreed
on before any work starts.

To contribute, fork this repository on GitHub, make your changes in the fork and open a pull request. Submitting a
contribution means you accept the terms in section 5 of the license.

## 📄 License

Trag is free to use, both personally and commercially. The source code may be read, built and modified for your own
use or for contributing back, but it may not be redistributed, sold or published under a different name. The full
terms are in [LICENSE](LICENSE).

Trag includes open-source components that are covered by their own licenses, listed in
[THIRD-PARTY-NOTICES.txt](docs/THIRD-PARTY-NOTICES.txt).

<p align="center">
  Made by <a href="https://finitysoftware.io">Finity Software</a>
</p>
