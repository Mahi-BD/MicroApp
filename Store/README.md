# Microsoft Store packaging

MicroApp is listed on the Microsoft Store as an **MSIX** package (Store ID `9MXDQG6Q35D0`).
The Store signs the package, so no code-signing certificate is needed here.

`AppxManifest.xml` carries the identity Partner Center assigned to the product. Those three
values are not decorative — a package whose identity does not match is rejected on upload:

| Element | Value |
|---|---|
| `Package/Identity/Name` | `RampsBD.MicroApp` |
| `Package/Identity/Publisher` | `CN=8C8AE894-B66F-4CE1-9805-33D3F83BC5CF` |
| `Package/Properties/PublisherDisplayName` | `RampsBD` |

`Assets/` holds the tile and Store logos, generated from `Resources/AppIcon.ico`.

## Building the package

Needs `makeappx.exe` from the Windows SDK (10.0.22621.0 or newer). From a layout folder
containing the Release binaries plus this manifest and `Assets/`:

    makeappx pack /d <layout> /p MicroApp-<version>.msix /nv

`/nv` skips signature validation, which is what you want for a Store-signed package.
Bump `Version` in the manifest to match `AssemblyFileVersion` before each submission — the
Store rejects a version it has already seen, and the fourth part must stay `0`.
