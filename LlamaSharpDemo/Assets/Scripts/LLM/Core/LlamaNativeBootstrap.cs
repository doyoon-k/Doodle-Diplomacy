using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using LLama.Native;
using UnityEngine;

/// <summary>
/// Prepares Windows native DLL search paths so LLamaSharp backends can load consistently in Unity.
/// </summary>
internal static class LlamaNativeBootstrap
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchUserDirs = 0x00000400;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    private static readonly object Sync = new object();
    private static bool _initialized;
    private static string _preferredNativeDirectory;

    public static void EnsureInitialized(bool verboseLogging)
    {
        if (_initialized)
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                TrySetupWindows(verboseLogging);
            }
            else
            {
                Debug.LogWarning($"[LlamaNativeBootstrap] Skip native bootstrap on platform: {Application.platform}");
            }

            _initialized = true;
        }
    }

    private static void TrySetupWindows(bool verboseLogging)
    {
        try
        {
            string packagesRoot = Path.Combine(Application.dataPath, "Packages");
            bool hasPackagesRoot = Directory.Exists(packagesRoot);
            if (!hasPackagesRoot && verboseLogging)
            {
                Debug.Log($"[LlamaNativeBootstrap] Packages folder not found. Using project plugin folder only: {packagesRoot}");
            }

            string cudaDir = hasPackagesRoot
                ? FindBackendNativeDirectory(
                    packagesRoot,
                    backendPrefix: "LLamaSharp.Backend.Cuda12",
                    nativeRelativePath: Path.Combine("LLamaSharpRuntimes", "win-x64", "native", "cuda12"))
                : null;

            string cpuDir = hasPackagesRoot ? FindBestCpuNativeDirectory(packagesRoot) : null;
            string cudaBinDir = FindCudaBinDirectory();
            string pluginsNativeDir = FindProjectNativeDirectory();
            bool hasPluginNativeSet = !string.IsNullOrWhiteSpace(pluginsNativeDir);

            if (hasPluginNativeSet)
            {
                cudaDir = pluginsNativeDir;
                cpuDir = pluginsNativeDir;
            }

            string sourceNativeDirectory = cudaDir ?? cpuDir;
            _preferredNativeDirectory = sourceNativeDirectory;

            // Keep LLamaSharp's optional runtime layout in temp, but run native calls from the
            // folder that actually contains llama.dll/ggml*.dll for reliable DllImport probing.
            EnsureExpectedRuntimeLayout(sourceNativeDirectory, verboseLogging);

            ConfigureNativeLibraryConfig(sourceNativeDirectory, verboseLogging);

            AppendToProcessPath(cudaDir);
            AppendToProcessPath(cpuDir);
            AppendToProcessPath(cudaBinDir);
            SetProcessDllDirectory(cudaDir ?? cpuDir, verboseLogging);

            int okCount = 0;
            int failCount = 0;

            // ggml-cuda.dll depends on CUDA runtime DLLs.
            TryLoadFromEither(cudaDir, cudaBinDir, "cublasLt64_12.dll", verboseLogging, ref okCount, ref failCount);
            TryLoadFromEither(cudaDir, cudaBinDir, "cublas64_12.dll", verboseLogging, ref okCount, ref failCount);
            TryLoadFromEither(cudaDir, cudaBinDir, "cudart64_12.dll", verboseLogging, ref okCount, ref failCount);

            // ggml.dll from CUDA backend still depends on ggml-cpu.dll.
            TryLoad(cpuDir, "ggml-cpu.dll", verboseLogging, ref okCount, ref failCount);
            TryLoad(cudaDir, "ggml-base.dll", verboseLogging, ref okCount, ref failCount);
            TryLoad(cudaDir, "ggml-cuda.dll", verboseLogging, ref okCount, ref failCount);
            TryLoad(cudaDir, "ggml.dll", verboseLogging, ref okCount, ref failCount);
            TryLoad(cudaDir, "llama.dll", verboseLogging, ref okCount, ref failCount);
            TryLoad(cudaDir, "mtmd.dll", verboseLogging, ref okCount, ref failCount);

            // Validate name-based resolution used by DllImport("llama"/"mtmd").
            TryLoadByName("llama.dll", verboseLogging, ref okCount, ref failCount);
            TryLoadByName("mtmd.dll", verboseLogging, ref okCount, ref failCount);
            if (failCount > 0)
            {
                Debug.LogWarning(
                    $"[LlamaNativeBootstrap] Native preload finished with missing/failed libraries. " +
                    $"cudaDir='{cudaDir ?? "(null)"}' cpuDir='{cpuDir ?? "(null)"}' cudaBin='{cudaBinDir ?? "(null)"}' loaded={okCount} failed={failCount}");
            }
            else if (verboseLogging)
            {
                Debug.Log($"[LlamaNativeBootstrap] Native preload complete. loaded={okCount} failed={failCount}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LlamaNativeBootstrap] Native bootstrap failed: {ex.Message}");
        }
    }

    public static T RunWithNativeWorkingDirectory<T>(Func<T> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        string nativeDirectory = _preferredNativeDirectory;
        if (string.IsNullOrWhiteSpace(nativeDirectory) || !Directory.Exists(nativeDirectory))
        {
            return action();
        }

        string previousDirectory = Environment.CurrentDirectory;
        bool changed = !string.Equals(
            previousDirectory.TrimEnd('\\', '/'),
            nativeDirectory.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

        try
        {
            if (changed)
            {
                Environment.CurrentDirectory = nativeDirectory;
            }

            return action();
        }
        finally
        {
            if (changed)
            {
                try
                {
                    Environment.CurrentDirectory = previousDirectory;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LlamaNativeBootstrap] Failed to restore current directory: {ex.Message}");
                }
            }
        }
    }

    private static string FindBestCpuNativeDirectory(string packagesRoot)
    {
        string[] candidates = { "avx2", "avx", "noavx", "avx512" };
        foreach (string variant in candidates)
        {
            string path = FindBackendNativeDirectory(
                packagesRoot,
                backendPrefix: "LLamaSharp.Backend.Cpu",
                nativeRelativePath: Path.Combine("LLamaSharpRuntimes", "win-x64", "native", variant));

            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string FindProjectNativeDirectory()
    {
        string candidate = Path.Combine(Application.dataPath, "Plugins", "x86_64");
        if (!Directory.Exists(candidate))
        {
            return null;
        }

        bool hasLlama = File.Exists(Path.Combine(candidate, "llama.dll")) || File.Exists(Path.Combine(candidate, "libllama.dll"));
        bool hasGgml = File.Exists(Path.Combine(candidate, "ggml.dll")) || File.Exists(Path.Combine(candidate, "libggml.dll"));
        return hasLlama && hasGgml ? candidate : null;
    }

    private static void TryLoadFromEither(
        string primaryDirectory,
        string secondaryDirectory,
        string dllName,
        bool verboseLogging,
        ref int okCount,
        ref int failCount)
    {
        if (!string.IsNullOrWhiteSpace(primaryDirectory) && File.Exists(Path.Combine(primaryDirectory, dllName)))
        {
            TryLoad(primaryDirectory, dllName, verboseLogging, ref okCount, ref failCount);
            return;
        }

        if (!string.IsNullOrWhiteSpace(secondaryDirectory) && File.Exists(Path.Combine(secondaryDirectory, dllName)))
        {
            TryLoad(secondaryDirectory, dllName, verboseLogging, ref okCount, ref failCount);
            return;
        }

        failCount++;
        Debug.LogWarning($"[LlamaNativeBootstrap] Missing file in both locations: {dllName} | primary='{primaryDirectory}' secondary='{secondaryDirectory}'");
    }

    private static string FindCudaBinDirectory()
    {
        string[] envVars = { "CUDA_PATH_V12_8", "CUDA_PATH" };
        foreach (string envVar in envVars)
        {
            string cudaPath = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrWhiteSpace(cudaPath))
            {
                continue;
            }

            string binPath = Path.Combine(cudaPath, "bin");
            if (Directory.Exists(binPath))
            {
                return binPath;
            }
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        string cudaRoot = Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA");
        if (!Directory.Exists(cudaRoot))
        {
            return null;
        }

        string best = Directory
            .GetDirectories(cudaRoot, "v12.*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.Combine(path, "bin"))
            .FirstOrDefault(Directory.Exists);

        return best;
    }

    private static string FindBackendNativeDirectory(string packagesRoot, string backendPrefix, string nativeRelativePath)
    {
        var matches = Directory
            .GetDirectories(packagesRoot, $"{backendPrefix}*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string backendDir in matches)
        {
            string nativeDir = Path.Combine(backendDir, nativeRelativePath);
            if (Directory.Exists(nativeDir))
            {
                return nativeDir;
            }
        }

        return null;
    }

    private static void AppendToProcessPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] parts = currentPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        bool exists = parts.Any(p => string.Equals(
            p.TrimEnd('\\'),
            directory.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            string updated = string.IsNullOrWhiteSpace(currentPath)
                ? directory
                : currentPath + ";" + directory;
            Environment.SetEnvironmentVariable("PATH", updated);
        }
    }

    private static void TryLoad(
        string directory,
        string dllName,
        bool verboseLogging,
        ref int okCount,
        ref int failCount)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string fullPath = Path.Combine(directory, dllName);
        if (!File.Exists(fullPath))
        {
            failCount++;
            Debug.LogWarning($"[LlamaNativeBootstrap] Missing file: {fullPath}");
            return;
        }

        IntPtr handle = LoadLibraryExW(
            fullPath,
            IntPtr.Zero,
            LoadLibrarySearchDllLoadDir | LoadLibrarySearchUserDirs | LoadLibrarySearchDefaultDirs);

        if (handle == IntPtr.Zero)
        {
            handle = LoadLibraryW(fullPath);
        }

        if (handle == IntPtr.Zero)
        {
            failCount++;
            int error = Marshal.GetLastWin32Error();
            Debug.LogWarning($"[LlamaNativeBootstrap] LoadLibrary failed for '{fullPath}' ({error}: {new Win32Exception(error).Message})");
            return;
        }

        okCount++;
        if (verboseLogging)
        {
            Debug.Log($"[LlamaNativeBootstrap] Preloaded native library: {fullPath}");
        }
    }

    private static void SetProcessDllDirectory(string directory, bool verboseLogging)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            bool ok = SetDllDirectoryW(directory);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.LogWarning($"[LlamaNativeBootstrap] SetDllDirectory failed for '{directory}' ({error}: {new Win32Exception(error).Message})");
                return;
            }

            if (verboseLogging)
            {
                Debug.Log($"[LlamaNativeBootstrap] SetDllDirectory: {directory}");
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Unexpected on supported Windows versions, ignore as fallback.
        }
    }

    private static void TryLoadByName(
        string dllName,
        bool verboseLogging,
        ref int okCount,
        ref int failCount)
    {
        if (string.IsNullOrWhiteSpace(dllName))
        {
            return;
        }

        IntPtr handle = LoadLibraryExW(
            dllName,
            IntPtr.Zero,
            LoadLibrarySearchDllLoadDir | LoadLibrarySearchUserDirs | LoadLibrarySearchDefaultDirs);

        if (handle == IntPtr.Zero)
        {
            handle = LoadLibraryW(dllName);
        }

        if (handle == IntPtr.Zero)
        {
            failCount++;
            int error = Marshal.GetLastWin32Error();
            Debug.LogWarning($"[LlamaNativeBootstrap] Name-based LoadLibrary failed for '{dllName}' ({error}: {new Win32Exception(error).Message})");
            return;
        }

        okCount++;
        if (verboseLogging)
        {
            Debug.Log($"[LlamaNativeBootstrap] Name-based native load succeeded: {dllName}");
        }
    }

    private static void ConfigureNativeLibraryConfig(string nativeDirectory, bool verboseLogging)
    {
        if (string.IsNullOrWhiteSpace(nativeDirectory) || !Directory.Exists(nativeDirectory))
        {
            return;
        }

        try
        {
            if (NativeLibraryConfig.LLama.LibraryHasLoaded || NativeLibraryConfig.Mtmd.LibraryHasLoaded)
            {
                if (verboseLogging)
                {
                    Debug.Log("[LlamaNativeBootstrap] Skip NativeLibraryConfig setup because a native library is already loaded.");
                }

                return;
            }

            string llamaPath = ResolvePreferredLibraryPath(nativeDirectory, "llama.dll", "libllama.dll");
            string mtmdPath = ResolvePreferredLibraryPath(nativeDirectory, "mtmd.dll", "libmtmd.dll");

            bool llamaConfigured = TryApplyOptionalConfigMethods(
                NativeLibraryConfig.LLama,
                libraryPath: llamaPath,
                searchDirectory: nativeDirectory);

            bool mtmdConfigured = TryApplyOptionalConfigMethods(
                NativeLibraryConfig.Mtmd,
                libraryPath: mtmdPath,
                searchDirectory: nativeDirectory);

            // Some LLamaSharp variants expose combined config only on container.
            if ((!llamaConfigured || !mtmdConfigured) && NativeLibraryConfig.All != null)
            {
                TryInvokeStringMethod(NativeLibraryConfig.All, "WithSearchDirectory", nativeDirectory);
                TryInvokeStringEnumerableMethod(NativeLibraryConfig.All, "WithSearchDirectories", nativeDirectory);
                TryInvokeDualStringMethod(NativeLibraryConfig.All, "WithLibrary", llamaPath, mtmdPath);
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"[LlamaNativeBootstrap] NativeLibraryConfig pinned. " +
                    $"llama='{llamaPath}' mtmd='{mtmdPath}' searchDir='{nativeDirectory}'");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LlamaNativeBootstrap] Failed to configure NativeLibraryConfig: {ex.Message}");
        }
    }

    private static string ResolvePreferredLibraryPath(string directory, string preferredName, string fallbackName)
    {
        string preferredPath = Path.Combine(directory, preferredName);
        if (File.Exists(preferredPath))
        {
            return preferredPath;
        }

        string fallbackPath = Path.Combine(directory, fallbackName);
        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        return preferredPath;
    }

    private static bool TryApplyOptionalConfigMethods(object config, string libraryPath, string searchDirectory)
    {
        if (config == null)
        {
            return false;
        }

        bool configured = false;
        configured |= TryInvokeStringMethod(config, "WithSearchDirectory", searchDirectory);
        configured |= TryInvokeStringEnumerableMethod(config, "WithSearchDirectories", searchDirectory);
        configured |= TryInvokeStringMethod(config, "WithLibrary", libraryPath);
        return configured;
    }

    private static bool TryInvokeStringMethod(object target, string methodName, string value)
    {
        if (target == null || string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        MethodInfo method = target
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(string);
            });

        if (method == null)
        {
            return false;
        }

        method.Invoke(target, new object[] { value });
        return true;
    }

    private static bool TryInvokeStringEnumerableMethod(object target, string methodName, string value)
    {
        if (target == null || string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        MethodInfo method = target
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = m.GetParameters();
                if (parameters.Length != 1)
                {
                    return false;
                }

                Type parameterType = parameters[0].ParameterType;
                return typeof(System.Collections.Generic.IEnumerable<string>).IsAssignableFrom(parameterType);
            });

        if (method == null)
        {
            return false;
        }

        method.Invoke(target, new object[] { new[] { value } });
        return true;
    }

    private static bool TryInvokeDualStringMethod(object target, string methodName, string valueA, string valueB)
    {
        if (target == null || string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        MethodInfo method = target
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(string) &&
                       parameters[1].ParameterType == typeof(string);
            });

        if (method == null)
        {
            return false;
        }

        method.Invoke(target, new object[] { valueA, valueB });
        return true;
    }

    private static string EnsureExpectedRuntimeLayout(string nativeDirectory, bool verboseLogging)
    {
        if (string.IsNullOrWhiteSpace(nativeDirectory) || !Directory.Exists(nativeDirectory))
        {
            return null;
        }

        try
        {
            RemoveLegacyRuntimeLayoutFromAssets(nativeDirectory, verboseLogging);

            string runtimeRoot = GetRuntimeWorkingRoot();
            string runtimeNativeRoot = Path.Combine(runtimeRoot, "runtimes", "win-x64", "native");
            EnsureDirectory(runtimeNativeRoot);

            // CUDA backend layout expected by LLamaSharp.
            string cuda12Dir = Path.Combine(runtimeNativeRoot, "cuda12");
            EnsureDirectory(cuda12Dir);
            EnsureAlias(nativeDirectory, cuda12Dir, "llama.dll", "llama.dll", "libllama.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "mtmd.dll", "mtmd.dll", "libmtmd.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "ggml.dll", "ggml.dll", "libggml.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "ggml-base.dll", "ggml-base.dll", "libggml-base.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "ggml-cpu.dll", "ggml-cpu.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "ggml-cuda.dll", "ggml-cuda.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "cudart64_12.dll", "cudart64_12.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "cublas64_12.dll", "cublas64_12.dll");
            EnsureAlias(nativeDirectory, cuda12Dir, "cublasLt64_12.dll", "cublasLt64_12.dll");

            // CPU fallback layouts LLamaSharp may probe by AVX level.
            string[] cpuVariants = { "avx2", "avx", "noavx", "avx512", string.Empty };
            foreach (string variant in cpuVariants)
            {
                string variantDir = string.IsNullOrEmpty(variant)
                    ? runtimeNativeRoot
                    : Path.Combine(runtimeNativeRoot, variant);
                EnsureDirectory(variantDir);
                EnsureAlias(nativeDirectory, variantDir, "llama.dll", "llama.dll", "libllama.dll");
                EnsureAlias(nativeDirectory, variantDir, "mtmd.dll", "mtmd.dll", "libmtmd.dll");
                EnsureAlias(nativeDirectory, variantDir, "ggml.dll", "ggml.dll", "libggml.dll");
                EnsureAlias(nativeDirectory, variantDir, "ggml-base.dll", "ggml-base.dll", "libggml-base.dll");
                EnsureAlias(nativeDirectory, variantDir, "ggml-cpu.dll", "ggml-cpu.dll");
            }

            if (verboseLogging)
            {
                Debug.Log($"[LlamaNativeBootstrap] Ensured LLamaSharp runtime layout under: {runtimeNativeRoot}");
            }

            return runtimeRoot;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LlamaNativeBootstrap] Failed to prepare runtime layout: {ex.Message}");
            return null;
        }
    }

    private static string GetRuntimeWorkingRoot()
    {
        string tempRoot = Path.GetTempPath();
        string projectToken = MakeSafeToken(Application.dataPath);
        return Path.Combine(tempRoot, "LLamaSharpRuntime", projectToken);
    }

    private static string MakeSafeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        char[] chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        string token = new string(chars);
        while (token.Contains("__"))
        {
            token = token.Replace("__", "_");
        }

        token = token.Trim('_');
        if (token.Length > 80)
        {
            token = token.Substring(token.Length - 80);
        }

        return string.IsNullOrWhiteSpace(token) ? "default" : token;
    }

    private static void RemoveLegacyRuntimeLayoutFromAssets(string nativeDirectory, bool verboseLogging)
    {
        string legacyRoot = Path.Combine(nativeDirectory, "runtimes");
        string legacyRootMeta = legacyRoot + ".meta";

        try
        {
            if (Directory.Exists(legacyRoot))
            {
                Directory.Delete(legacyRoot, recursive: true);
                if (verboseLogging)
                {
                    Debug.Log($"[LlamaNativeBootstrap] Removed legacy runtime directory from Assets: {legacyRoot}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LlamaNativeBootstrap] Failed to remove legacy runtime directory '{legacyRoot}': {ex.Message}");
        }

        try
        {
            if (File.Exists(legacyRootMeta))
            {
                File.Delete(legacyRootMeta);
                if (verboseLogging)
                {
                    Debug.Log($"[LlamaNativeBootstrap] Removed legacy runtime meta: {legacyRootMeta}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LlamaNativeBootstrap] Failed to remove legacy runtime meta '{legacyRootMeta}': {ex.Message}");
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void EnsureAlias(string sourceDir, string destinationDir, string destinationName, params string[] sourceCandidates)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(destinationDir) || string.IsNullOrWhiteSpace(destinationName))
        {
            return;
        }

        string destinationPath = Path.Combine(destinationDir, destinationName);
        if (File.Exists(destinationPath))
        {
            return;
        }

        if (sourceCandidates == null || sourceCandidates.Length == 0)
        {
            return;
        }

        string sourcePath = sourceCandidates
            .Select(name => Path.Combine(sourceDir, name))
            .FirstOrDefault(File.Exists);

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectoryW(string lpPathName);
}
