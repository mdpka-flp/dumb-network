# zapret backend

Bundled from the official [bol-van/zapret](https://github.com/bol-van/zapret) release.
Do not edit these files by hand; refresh them with:

```bash
./scripts/fetch-zapret.sh
```

## Layout

```
backend/zapret/v72.13/
├── VERSION                 # upstream release tag
├── LICENSE.txt             # upstream license
├── linux-x64/nfqws         # static ELF, requires root + nftables/iptables
├── linux-arm64/nfqws
├── windows-x64/
│   ├── winws.exe           # nfqws port based on WinDivert
│   ├── WinDivert.dll
│   ├── WinDivert64.sys
│   └── cygwin1.dll
└── files/fake/*.bin        # fake payloads referenced by the strategies
```

Only these files are needed by the client. The rest of the upstream tarball
(init scripts, blockcheck, sources, other architectures) is intentionally not
shipped.

## Version bump

Set `ZAPRET_VERSION` and rerun the script, for example:

```bash
ZAPRET_VERSION=v72.14 ./scripts/fetch-zapret.sh
```

Then update `backend/zapret/VERSION` consumers (the app discovers the version
from the `VERSION` file, so no code change is normally required).

## Platform notes

- Linux: `nfqws` is static and self-contained. The client installs its own
  nftables table (`inet dumbzapret`) or an iptables chain (`DUMBZAPRET`), then
  queues the selected ports into NFQUEUE 200. Root is mandatory.
- Windows: `winws.exe` needs `WinDivert.dll`, `WinDivert64.sys` and
  `cygwin1.dll` in the same directory. The app manifest requests elevation.
  Windows ARM64 is not supported because upstream has no signed WinDivert
  driver for it.
- Linux ARM 32-bit and other architectures: copy the matching `nfqws` into a
  new `linux-<arch>` directory and extend `BackendLayout` if needed.
