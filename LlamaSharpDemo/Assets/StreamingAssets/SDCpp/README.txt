Place stable-diffusion.cpp runtime files here, grouped by platform.

Expected default layout:

  Assets/StreamingAssets/SDCpp/win-x64/
    sd-cli.exe
    stable-diffusion.dll
    cudart64_12.dll
    cublas64_12.dll
    cublasLt64_12.dll
    (and any additional required DLL files from stable-diffusion.cpp release)

Then configure StableDiffusionCppSettings.runtimePackages with:
  platform = WinX64
  streamingAssetsRuntimeFolder = SDCpp/win-x64
  executableFileName = sd-cli.exe

For other platforms, add additional runtime packages and folders:
  SDCpp/linux-x64
  SDCpp/macos-arm64
  etc.
