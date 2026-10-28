# AGENTS.md

Avalonia 12 / .NET 10 desktop client for zapret (DPI bypass). UI text is Russian.
Targets Linux and Windows only; `nfqws` on Linux, `winws` + WinDivert on Windows.

## Commands

- `dotnet build DumbNetwork.slnx` — solution is **`.slnx`**, there is no `.sln`.
- `dotnet test DumbNetwork.slnx` — includes a real `nfqws --dry-run` for every
  Linux strategy; it needs the bundled backend, so run it from the repo.
- Opt-in orchestration test (installs real nft rules, uses a stand-in backend):
  `unshare -Urn sh -c 'ip link set lo up; DUMBZAPRET_INTEGRATION=1 dotnet test tests/DumbNetwork.Tests/DumbNetwork.Tests.csproj --filter LinuxIntegrationTests'`.
  The helper protocol itself can be exercised the same way with a fake `sudo`
  script in `PATH`, `DUMBZAPRET_BACKEND` pointing at a stand-in backend, and a
  harness whose apphost dispatches `--privileged-helper` to `PrivilegedHelper`.
- `dotnet run --project src/DumbNetwork` — normal way to launch on Linux. The GUI
  runs unprivileged and prompts for the admin password in `PasswordDialog` on the
  first enable. If the process is already root, no prompt is shown.
- `./scripts/fetch-zapret.sh` — re-download the backend (default v72.13, override
  with `ZAPRET_VERSION=vX.Y`). Do not edit `backend/` by hand.

## Layout / ownership

- `src/DumbNetwork/Strategies/StrategyCatalog.cs` — the only place strategies are
  defined. Arguments are shared between nfqws and winws; `{fake:file.bin}` expands
  to `backend/.../files/fake/file.bin`. Validate changes by running `dotnet test`
  (dry-run test) — not by launching the GUI.
- `src/DumbNetwork/Services/BackendLayout.cs` — backend discovery. Keep paths
  version-agnostic: it reads `backend/zapret/<version>/VERSION` and resolves
  `linux-x64`, `linux-arm64` or `windows-x64`. Env override:
  `DUMBZAPRET_BACKEND`.
- `src/DumbNetwork/Services/Firewall/` — Linux-only nftables (preferred) and
  iptables fallback. Windows uses `NoopFirewall` because winws/WinDivert filters
  packets itself.
- `src/DumbNetwork/Services/PrivilegedHelperClient.cs` + `PrivilegedHelper.cs` —
  Linux privilege separation. The unprivileged GUI launches the same apphost with
  `--privileged-helper` via `sudo -k -S`; they speak JSON lines over stdin/stdout.
  `HelperProtocol.cs` defines the wire format, `SudoDiagnostics.cs` maps sudo stderr.
- `src/DumbNetwork/Services/ZapretControllerFactory.cs` chooses the helper client
  on Linux non-root, otherwise the local `ZapretRunner` (Windows or root Linux).
- `src/DumbNetwork/ViewModels/MainWindowViewModel.cs` + `Views/MainWindow.axaml`
  — MVVM with CommunityToolkit. There is no ViewLocator; `App` wires the VM and
  sets `PasswordPrompt` to `MainWindow.PromptForPasswordAsync`.
- `src/DumbNetwork/Views/PasswordDialog.axaml` — in-app sudo password dialog.

## Backend contract

- `backend/zapret/<version>/{linux-x64,linux-arm64,windows-x64}` plus
  `files/fake/`; `scripts/fetch-zapret.sh` verifies upstream SHA-256.
- The csproj copies `backend/**` into the output under `backend/...`
  (`Content` + `Link`). New backend files require a rebuild to appear.
- Windows needs `winws.exe`, `WinDivert.dll`, `WinDivert64.sys` and `cygwin1.dll`
  together in one directory; `app.manifest` requests administrator rights.
- Linux uses NFQUEUE **200** and fwmark **0x40000000**; firewall table is
  `dumbzapret`, iptables chain is `DUMBZAPRET`. Keep these in sync with
  `ZapretConstants`.

## Gotchas

- Start order: nfqws first, then firewall; stop order: firewall first, then process.
  Breaking this can drop/loop packets. This order lives in `ZapretRunner` and is
  reused by the helper.
- The helper must accept only strategy IDs from `StrategyCatalog` plus
  `start`/`stop`/`shutdown`. Never accept arbitrary commands or nfqws arguments
  over the pipe: the helper is root, and a sudoers rule may permit only this app.
- The password must only travel to `sudo` via stdin. Do not log it, put it in
  command-line arguments, or keep it on the controller after authentication.
  `sudo` is launched with `LANG=C`/`LC_ALL=C` so `SudoDiagnostics` can match stable
  English messages; the prompt marker is `__DUMBNET_SUDO_PASSWORD__`.
- `DUMBZAPRET_BACKEND` is read by the GUI/local runner but is not preserved through
  sudo for the helper. The helper locates the bundled backend from `AppContext`
  and the current directory; keep test backends reachable from there or run the
  local root flow.
- The app must clean up firewall rules on close — `MainWindow.OnClosing` awaits
  `ShutdownAsync`; do not remove that path. The helper additionally cleans up on
  stdin EOF, which covers the GUI exiting. `PrivilegedHelperClient.DisposeAsync`
  sends `shutdown`, then closes stdin, then kills as a last resort.
- `nfqws` tricks (`md5sig`, `badseq`) are marked Linux-only in the catalog; do not
  silently expose Linux-only args on Windows.
- `PrivilegedHelperClient` intentionally forces `sudo -k`; each app session asks
  for the password even if the user has a fresh sudo timestamp.
- Hardware flow offload bypasses NFQUEUE; this is documented in README, not fixable
  from the app.
- `nfqws` drops root with `setgroups` at startup, which fails inside unprivileged
  user namespaces. For namespace-based testing use the fake-backend integration
  test; real nfqws needs a real root shell.
- UI is intentionally one strategy at a time; no hostlists/autohostlist yet.
