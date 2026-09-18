# Distribution notice inventory

This file is the maintained inventory for generated local distribution notices. `tools/collect-notices.sh <new-directory>` reads the pinned Godot engine's own complete license texts and copyright inventory and copies the pinned redistributed .NET runtime packs' supplied license and third-party notices. `tools/release-package.sh` includes those files beside the application archive, with their source versions and hashes. This collection does not grant project distribution rights or certify a public release.

| Component | Where its exact version is pinned | Remaining distribution action |
| --- | --- | --- |
| Godot 4.6.2 Mono engine and export templates | `tools/bootstrap.sh` and pinned bootstrap manifests | Collector requires exact engine source revision `71f334935c000924d403448e698df4441130df18` and retains engine-provided license texts and third-party copyright information. |
| .NET 8.0.31 runtime | `global.json`, project/runtime lockfiles, `tools/runtime-packs.json` | Collector includes the supplied `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` for both macOS architectures or Linux x86_64, with version/file hashes. |
| Godot C# runtime assemblies | Client project and package lockfile | Covered by the engine's bundled source/license inventory; review the final exported dependency list when changing engine or managed packages. |
| Project-authored procedural visuals/audio | `assets/credits.json` | Owner declares distribution terms and final contributor credits. |

Test-only NuGet packages and the Go developer CLI are not automatically runtime dependencies of a packaged game. The current client uses the .NET runtime and Godot assemblies; a new runtime dependency requires updating the collector and auditing the actual export. Preserve the generated notice files in the final distribution. Owner credit/rights decisions, platform requirements, and any additional dependencies remain separate acceptance checks.
