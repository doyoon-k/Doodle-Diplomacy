# Third-Party Notices (Template)

This package uses third-party software and runtimes.

Important:
- This document is a maintainer template, not legal advice.
- Review and update all entries before each public release.

## Included Components

1. LLamaSharp
   - Source/Package: NuGet `LLamaSharp`
   - Purpose: .NET binding and runtime integration for llama.cpp
   - Action for release owner: include exact version and license text

2. llama.cpp native backends (via LLamaSharp backend packages)
   - Source/Package: `LLamaSharp.Backend.*`
   - Purpose: native inference backend (CPU/CUDA)
   - Action for release owner: include exact version and license text

3. CUDA runtime dependencies (if redistributed)
   - Source: NVIDIA redistributable runtime files
   - Purpose: CUDA backend execution on end-user systems
   - Action for release owner: verify redistribution terms and include required notices

4. Hugging Face model downloads (user-provided models)
   - Purpose: model acquisition workflow from Hugging Face repositories
   - Action for release owner: document that model license is per-model and must be respected by end user

5. Additional managed dependencies (NuGet)
   - Examples in this project: `System.*`, `Microsoft.*`, `CommunityToolkit.HighPerformance`
   - Action for release owner: include upstream notices where required

## What To Ship With Asset Release

1. A `Third-Party Notices` document (this file, completed).
2. Any required license files for bundled binaries.
3. A statement clarifying:
   - Models are not bundled unless explicitly included.
   - Model usage is subject to the selected model's license.
   - GPU acceleration may require compatible hardware and runtime dependencies.

## Maintainer Release Checklist

1. Confirm final dependency versions.
2. Recheck each dependency license obligations.
3. Update this file with exact package names and versions.
4. Include missing notice/license texts in release payload.
5. Validate the package on a clean Unity project.

