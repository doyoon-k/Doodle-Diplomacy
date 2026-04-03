#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || STABLE_DIFFUSION_CPP_SIDECAR_WORKER_WIN
#define STABLE_DIFFUSION_CPP_WIN
#endif

using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class StableDiffusionCppNativeBridge
{
    private const string LibraryName = "stable-diffusion";

    private static readonly object LibraryLock = new object();
    private static IntPtr _libraryHandle = IntPtr.Zero;
    private static string _loadedLibraryPath;
    private static string _loadedRuntimeDirectory;

    internal enum SampleMethod
    {
        Euler = 0,
        EulerA = 1,
        Heun = 2,
        Dpm2 = 3,
        Dpmpp2SA = 4,
        Dpmpp2M = 5,
        Dpmpp2Mv2 = 6,
        Ipndm = 7,
        IpndmV = 8,
        Lcm = 9,
        DdimTrailing = 10,
        Tcd = 11,
        ResMultistep = 12,
        Res2S = 13,
        Count = 14
    }

    internal enum Scheduler
    {
        Discrete = 0,
        Karras = 1,
        Exponential = 2,
        Ays = 3,
        Gits = 4,
        SgmUniform = 5,
        Simple = 6,
        SmoothStep = 7,
        KlOptimal = 8,
        Lcm = 9,
        BongTangent = 10,
        Count = 11
    }

    internal enum Prediction
    {
        Eps = 0,
        V = 1,
        EdmV = 2,
        Flow = 3,
        FluxFlow = 4,
        Flux2Flow = 5,
        Count = 6
    }

    internal enum SdType
    {
        F32 = 0,
        F16 = 1,
        Q4_0 = 2,
        Q4_1 = 3,
        Q5_0 = 6,
        Q5_1 = 7,
        Q8_0 = 8,
        Q8_1 = 9,
        Q2K = 10,
        Q3K = 11,
        Q4K = 12,
        Q5K = 13,
        Q6K = 14,
        Q8K = 15,
        IQ2Xxs = 16,
        IQ2Xs = 17,
        IQ3Xxs = 18,
        IQ1S = 19,
        IQ4Nl = 20,
        IQ3S = 21,
        IQ2S = 22,
        IQ4Xs = 23,
        I8 = 24,
        I16 = 25,
        I32 = 26,
        I64 = 27,
        F64 = 28,
        IQ1M = 29,
        Bf16 = 30,
        Tq1_0 = 34,
        Tq2_0 = 35,
        Mxfp4 = 39,
        Nvfp4 = 40,
        Count = 41
    }

    internal enum RngType
    {
        Default = 0,
        Cuda = 1,
        Cpu = 2,
        Count = 3
    }

    internal enum LoraApplyMode
    {
        Auto = 0,
        Immediately = 1,
        AtRuntime = 2,
        Count = 3
    }

    internal enum CacheMode
    {
        Disabled = 0,
        EasyCache = 1,
        UCache = 2,
        DBCache = 3,
        TaylorSeer = 4,
        CacheDit = 5,
        Spectrum = 6
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdTilingParams
    {
        public byte enabled;
        public int tile_size_x;
        public int tile_size_y;
        public float target_overlap;
        public float rel_size_x;
        public float rel_size_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdEmbedding
    {
        public IntPtr name;
        public IntPtr path;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdCtxParams
    {
        public IntPtr model_path;
        public IntPtr clip_l_path;
        public IntPtr clip_g_path;
        public IntPtr clip_vision_path;
        public IntPtr t5xxl_path;
        public IntPtr llm_path;
        public IntPtr llm_vision_path;
        public IntPtr diffusion_model_path;
        public IntPtr high_noise_diffusion_model_path;
        public IntPtr vae_path;
        public IntPtr taesd_path;
        public IntPtr control_net_path;
        public IntPtr embeddings;
        public uint embedding_count;
        public IntPtr photo_maker_path;
        public IntPtr tensor_type_rules;
        public byte vae_decode_only;
        public byte free_params_immediately;
        public int n_threads;
        public SdType wtype;
        public RngType rng_type;
        public RngType sampler_rng_type;
        public Prediction prediction;
        public LoraApplyMode lora_apply_mode;
        public byte offload_params_to_cpu;
        public byte enable_mmap;
        public byte keep_clip_on_cpu;
        public byte keep_control_net_on_cpu;
        public byte keep_vae_on_cpu;
        public byte flash_attn;
        public byte diffusion_flash_attn;
        public byte tae_preview_only;
        public byte diffusion_conv_direct;
        public byte vae_conv_direct;
        public byte circular_x;
        public byte circular_y;
        public byte force_sdxl_vae_conv_scale;
        public byte chroma_use_dit_mask;
        public byte chroma_use_t5_mask;
        public int chroma_t5_mask_pad;
        public byte qwen_image_zero_cond_t;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdImage
    {
        public uint width;
        public uint height;
        public uint channel;
        public IntPtr data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdSlgParams
    {
        public IntPtr layers;
        public UIntPtr layer_count;
        public float layer_start;
        public float layer_end;
        public float scale;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdGuidanceParams
    {
        public float txt_cfg;
        public float img_cfg;
        public float distilled_guidance;
        public SdSlgParams slg;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdSampleParams
    {
        public SdGuidanceParams guidance;
        public Scheduler scheduler;
        public SampleMethod sample_method;
        public int sample_steps;
        public float eta;
        public int shifted_timestep;
        public IntPtr custom_sigmas;
        public int custom_sigmas_count;
        public float flow_shift;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdPmParams
    {
        public IntPtr id_images;
        public int id_images_count;
        public IntPtr id_embed_path;
        public float style_strength;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdCacheParams
    {
        public CacheMode mode;
        public float reuse_threshold;
        public float start_percent;
        public float end_percent;
        public float error_decay_rate;
        public byte use_relative_threshold;
        public byte reset_error_on_compute;
        public int Fn_compute_blocks;
        public int Bn_compute_blocks;
        public float residual_diff_threshold;
        public int max_warmup_steps;
        public int max_cached_steps;
        public int max_continuous_cached_steps;
        public int taylorseer_n_derivatives;
        public int taylorseer_skip_interval;
        public IntPtr scm_mask;
        public byte scm_policy_dynamic;
        public float spectrum_w;
        public int spectrum_m;
        public float spectrum_lam;
        public int spectrum_window_size;
        public float spectrum_flex_window;
        public int spectrum_warmup_steps;
        public float spectrum_stop_percent;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdLora
    {
        public byte is_high_noise;
        public float multiplier;
        public IntPtr path;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdImgGenParams
    {
        public IntPtr loras;
        public uint lora_count;
        public IntPtr prompt;
        public IntPtr negative_prompt;
        public int clip_skip;
        public SdImage init_image;
        public IntPtr ref_images;
        public int ref_images_count;
        public byte auto_resize_ref_image;
        public byte increase_ref_index;
        public SdImage mask_image;
        public int width;
        public int height;
        public SdSampleParams sample_params;
        public float strength;
        public long seed;
        public int batch_count;
        public SdImage control_image;
        public float control_strength;
        public SdPmParams pm_params;
        public SdTilingParams vae_tiling_params;
        public SdCacheParams cache;
    }

    internal static bool CanUseLibraryPath(string nativeLibraryPath)
    {
        if (string.IsNullOrWhiteSpace(nativeLibraryPath))
        {
            return false;
        }

        lock (LibraryLock)
        {
            return _libraryHandle == IntPtr.Zero ||
                   string.Equals(_loadedLibraryPath, nativeLibraryPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static bool TryEnsureLoaded(
        string runtimeDirectory,
        string nativeLibraryPath,
        out string error)
    {
        error = null;

#if STABLE_DIFFUSION_CPP_WIN
        if (string.IsNullOrWhiteSpace(nativeLibraryPath))
        {
            error = "Native library path is empty.";
            return false;
        }

        string libraryName = Path.GetFileNameWithoutExtension(nativeLibraryPath);
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Persistent worker expects native library '{LibraryName}', but package points to '{Path.GetFileName(nativeLibraryPath)}'.";
            return false;
        }

        lock (LibraryLock)
        {
            if (_libraryHandle != IntPtr.Zero)
            {
                if (string.Equals(_loadedLibraryPath, nativeLibraryPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                error = $"Native library is already loaded from '{_loadedLibraryPath}'. Restart Unity/player to switch to '{nativeLibraryPath}'.";
                return false;
            }

            if (!File.Exists(nativeLibraryPath))
            {
                error = $"Native library file not found: {nativeLibraryPath}";
                return false;
            }

            string normalizedRuntimeDirectory = string.IsNullOrWhiteSpace(runtimeDirectory)
                ? Path.GetDirectoryName(nativeLibraryPath)
                : runtimeDirectory;
            if (!string.IsNullOrWhiteSpace(normalizedRuntimeDirectory) && !SetDllDirectory(normalizedRuntimeDirectory))
            {
                error = $"SetDllDirectory failed for '{normalizedRuntimeDirectory}' (Win32Error={Marshal.GetLastWin32Error()}).";
                return false;
            }

            IntPtr handle = LoadLibrary(nativeLibraryPath);
            if (handle == IntPtr.Zero)
            {
                error = $"LoadLibrary failed for '{nativeLibraryPath}' (Win32Error={Marshal.GetLastWin32Error()}).";
                return false;
            }

            _libraryHandle = handle;
            _loadedLibraryPath = nativeLibraryPath;
            _loadedRuntimeDirectory = normalizedRuntimeDirectory;
            return true;
        }
#else
        error = "Persistent Stable Diffusion native worker is currently supported only on Windows builds.";
        return false;
#endif
    }

    internal static string GetLoadedRuntimeDirectory()
    {
        lock (LibraryLock)
        {
            return _loadedRuntimeDirectory ?? string.Empty;
        }
    }

    internal static IntPtr StringToNative(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? IntPtr.Zero
            : Marshal.StringToHGlobalAnsi(value);
    }

    internal static IntPtr BytesToNative(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return IntPtr.Zero;
        }

        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    internal static void FreeHGlobal(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    internal static void FreeNativeMemory(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

#if STABLE_DIFFUSION_CPP_WIN
        try
        {
            UcrtFree(ptr);
            return;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        MsvcrtFree(ptr);
#else
        Marshal.FreeHGlobal(ptr);
#endif
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_ctx_params_init")]
    internal static extern void SdCtxParamsInit(ref SdCtxParams sdCtxParams);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "new_sd_ctx")]
    internal static extern IntPtr NewSdCtx(ref SdCtxParams sdCtxParams);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "free_sd_ctx")]
    internal static extern void FreeSdCtx(IntPtr sdCtx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_sample_params_init")]
    internal static extern void SdSampleParamsInit(ref SdSampleParams sampleParams);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_img_gen_params_init")]
    internal static extern void SdImgGenParamsInit(ref SdImgGenParams imageGenParams);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_cache_params_init")]
    internal static extern void SdCacheParamsInit(ref SdCacheParams cacheParams);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_get_default_sample_method")]
    internal static extern SampleMethod GetDefaultSampleMethod(IntPtr sdCtx);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_get_default_scheduler")]
    internal static extern Scheduler GetDefaultScheduler(IntPtr sdCtx, SampleMethod sampleMethod);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "str_to_sample_method")]
    internal static extern SampleMethod StrToSampleMethod([MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "str_to_scheduler")]
    internal static extern Scheduler StrToScheduler([MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "generate_image")]
    internal static extern IntPtr GenerateImage(IntPtr sdCtx, ref SdImgGenParams imageGenParams);

    internal static string SdGetSystemInfo()
    {
        IntPtr ptr = SdGetSystemInfoNative();
        return ptr == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_version")]
    internal static extern IntPtr SdVersion();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_get_system_info")]
    private static extern IntPtr SdGetSystemInfoNative();

#if STABLE_DIFFUSION_CPP_WIN
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "free")]
    private static extern void UcrtFree(IntPtr ptr);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "free")]
    private static extern void MsvcrtFree(IntPtr ptr);
#endif
}
