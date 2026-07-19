using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DoodleDiplomacy.Narrative.Editor
{
    public static class NarrativeDeskLauncher
    {
        public const string DeskUrl = "http://127.0.0.1:4317";
        private const int DeskPort = 4317;
        private static Process _serverProcess;

        [MenuItem("Tools/Narrative Desk/Open Narrative Desk", priority = 0)]
        public static void Open()
        {
            Open(string.Empty, string.Empty);
        }

        public static async void Open(string localizationKey, string beatId = "")
        {
            if (!await IsServerAvailable())
            {
                if (!TryStartServer())
                {
                    EditorUtility.DisplayDialog(
                        "Narrative Desk",
                        "Could not start the local Narrative Desk server. Run npm install in Tools/NarrativeDesk, then try again.",
                        "OK");
                    return;
                }

                for (int i = 0; i < 20 && !await IsServerAvailable(); i++)
                {
                    await Task.Delay(150);
                }
            }

            string query = !string.IsNullOrWhiteSpace(beatId)
                ? "?beat=" + Uri.EscapeDataString(beatId)
                : !string.IsNullOrWhiteSpace(localizationKey)
                    ? "?key=" + Uri.EscapeDataString(localizationKey)
                    : string.Empty;
            Application.OpenURL(DeskUrl + query);
        }

        [MenuItem("Tools/Narrative Desk/Stop Server", priority = 20)]
        private static void StopServer()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Narrative Desk] Could not stop server: {exception.Message}");
            }
        }

        private static bool TryStartServer()
        {
            string unityProjectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            string repositoryRoot = Path.GetDirectoryName(unityProjectRoot) ?? string.Empty;
            string deskRoot = Path.Combine(repositoryRoot, "Tools", "NarrativeDesk");
            string packagePath = Path.Combine(deskRoot, "package.json");
            if (!File.Exists(packagePath))
            {
                Debug.LogError($"[Narrative Desk] package.json was not found at {packagePath}.");
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Application.platform == RuntimePlatform.WindowsEditor ? "npm.cmd" : "npm",
                    Arguments = "start",
                    WorkingDirectory = deskRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                _serverProcess = Process.Start(startInfo);
                if (_serverProcess == null)
                {
                    return false;
                }

                _serverProcess.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        Debug.Log("[Narrative Desk] " + args.Data);
                    }
                };
                _serverProcess.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        Debug.LogWarning("[Narrative Desk] " + args.Data);
                    }
                };
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Narrative Desk] Failed to start server: {exception.Message}");
                return false;
            }
        }

        private static async Task<bool> IsServerAvailable()
        {
            try
            {
                using var client = new TcpClient();
                Task connectTask = client.ConnectAsync("127.0.0.1", DeskPort);
                Task completed = await Task.WhenAny(connectTask, Task.Delay(180));
                return completed == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}
