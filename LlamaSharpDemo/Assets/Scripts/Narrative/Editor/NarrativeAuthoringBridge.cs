using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Localization;
using UnityEditor;
using UnityEngine;
using GameL10n = DoodleDiplomacy.Localization.L10n;

namespace DoodleDiplomacy.Narrative.Editor
{
    [InitializeOnLoad]
    public static class NarrativeAuthoringBridge
    {
        private const string BridgeUri = "ws://127.0.0.1:4317/ws";
        private const double RetrySeconds = 2.0;
        private static readonly ConcurrentQueue<Action> MainThreadActions = new();
        private static readonly SemaphoreSlim SendLock = new(1, 1);
        private static ClientWebSocket _socket;
        private static CancellationTokenSource _cancellation;
        private static double _nextRetryTime;
        private static bool _connecting;

        static NarrativeAuthoringBridge()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            NarrativeTrace.Emitted += OnTraceEmitted;
            UiCopyTrace.Emitted += OnUiCopyTraceEmitted;
            _nextRetryTime = EditorApplication.timeSinceStartup;
        }

        private static void Update()
        {
            while (MainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if ((_socket == null || _socket.State != WebSocketState.Open) &&
                !_connecting &&
                EditorApplication.timeSinceStartup >= _nextRetryTime)
            {
                _nextRetryTime = EditorApplication.timeSinceStartup + RetrySeconds;
                _ = ConnectAsync();
            }
        }

        private static async Task ConnectAsync()
        {
            _connecting = true;
            try
            {
                _cancellation?.Cancel();
                _cancellation?.Dispose();
                _socket?.Dispose();
                _cancellation = new CancellationTokenSource();
                _socket = new ClientWebSocket();
                _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                await _socket.ConnectAsync(new Uri(BridgeUri), _cancellation.Token);
                await SendAsync(JsonUtility.ToJson(new BridgeHello { type = "hello", role = "unity" }));
                _ = ReceiveLoopAsync(_socket, _cancellation.Token);
            }
            catch
            {
                _nextRetryTime = EditorApplication.timeSinceStartup + RetrySeconds;
            }
            finally
            {
                _connecting = false;
            }
        }

        private static async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    string json = Encoding.UTF8.GetString(stream.ToArray());
                    BridgeCommand command = JsonUtility.FromJson<BridgeCommand>(json);
                    if (command != null &&
                        (command.type == "preview_beat" || command.type == "play_checkpoint" || command.type == "set_locale"))
                    {
                        MainThreadActions.Enqueue(() => HandleCommand(command));
                    }
                }
            }
            catch
            {
                _nextRetryTime = EditorApplication.timeSinceStartup + RetrySeconds;
            }
        }

        private static void HandleCommand(BridgeCommand command)
        {
            if (!string.IsNullOrWhiteSpace(command.locale))
            {
                GameL10n.SetLocale(command.locale, persist: false);
            }

            switch (command.type)
            {
                case "set_locale":
                    SendResult(true, "Unity preview locale changed.");
                    break;
                case "preview_beat":
                    PreviewBeat(command.scenarioId, command.beatId);
                    break;
                case "play_checkpoint":
                    PlayCheckpoint(command.scenarioId, command.checkpointId);
                    break;
            }
        }

        private static void PreviewBeat(string scenarioId, string beatId)
        {
            if (!TryFindScenario(scenarioId, out NarrativeScenarioAsset scenario) ||
                !scenario.TryGetBeat(beatId, out NarrativeBeat beat))
            {
                SendResult(false, $"Narrative beat '{beatId}' was not found. Save and let Unity synchronize first.");
                return;
            }

            SubtitleDisplay display = UnityEngine.Object.FindFirstObjectByType<SubtitleDisplay>(
                FindObjectsInactive.Include);
            if (display == null)
            {
                SendResult(false, "No SubtitleDisplay exists in the active scene.");
                return;
            }

            L10nArg[] args = SampleArguments();
            display.Show(beat.ResolveSpeaker(args), beat.ResolveText(args));
            display.SetAdvancePromptVisible(beat.WaitForAdvance);
            EditorGUIUtility.PingObject(display);
            NarrativeTrace.Emit(scenario.ScenarioId, beat.id, "authoring_preview", args);
            SendResult(true, $"Previewing '{beat.id}' in the active scene.");
        }

        private static void PlayCheckpoint(string scenarioId, string checkpointId)
        {
            FirstContactEncounterDirector director =
                UnityEngine.Object.FindFirstObjectByType<FirstContactEncounterDirector>();
            if (Application.isPlaying && director != null &&
                director.PlayNarrativeCheckpointPreview(checkpointId))
            {
                SendResult(true, $"Playing checkpoint '{checkpointId}'.");
                return;
            }

            PreviewBeat(scenarioId, checkpointId);
        }

        private static bool TryFindScenario(string scenarioId, out NarrativeScenarioAsset scenario)
        {
            scenario = null;
            string[] guids = AssetDatabase.FindAssets("t:NarrativeScenarioAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                NarrativeScenarioAsset candidate = AssetDatabase.LoadAssetAtPath<NarrativeScenarioAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (candidate != null && string.Equals(
                        candidate.ScenarioId,
                        scenarioId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    scenario = candidate;
                    return true;
                }
            }

            return false;
        }

        private static L10nArg[] SampleArguments()
        {
            return new[]
            {
                GameL10n.Arg(
                    "category",
                    GameL10n.T("first_contact.terminal.category.danger", "DANGER")),
                GameL10n.Arg("count", 4),
                GameL10n.Arg("required", 3),
                GameL10n.Arg("remaining", 1)
            };
        }

        private static void OnTraceEmitted(NarrativeTraceEvent trace)
        {
            _ = SendAsync(JsonUtility.ToJson(new TraceEnvelope
            {
                type = "narrative_trace",
                trace = trace
            }));
        }

        private static void OnUiCopyTraceEmitted(UiCopyTraceEvent trace)
        {
            _ = SendAsync(JsonUtility.ToJson(new UiCopyTraceEnvelope
            {
                type = "ui_copy_trace",
                trace = trace
            }));
        }

        private static void SendResult(bool ok, string message)
        {
            _ = SendAsync(JsonUtility.ToJson(new CommandResult
            {
                type = "command_result",
                ok = ok,
                message = message
            }));
        }

        private static async Task SendAsync(string text)
        {
            ClientWebSocket socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            await SendLock.WaitAsync();
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        _cancellation?.Token ?? CancellationToken.None);
                }
            }
            catch
            {
                _nextRetryTime = EditorApplication.timeSinceStartup + RetrySeconds;
            }
            finally
            {
                SendLock.Release();
            }
        }

        private static void Shutdown()
        {
            NarrativeTrace.Emitted -= OnTraceEmitted;
            UiCopyTrace.Emitted -= OnUiCopyTraceEmitted;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _socket?.Dispose();
            _socket = null;
        }

        [Serializable]
        private sealed class BridgeHello
        {
            public string type;
            public string role;
        }

        [Serializable]
        private sealed class BridgeCommand
        {
            public string type;
            public string scenarioId;
            public string beatId;
            public string checkpointId;
            public string locale;
        }

        [Serializable]
        private sealed class TraceEnvelope
        {
            public string type;
            public NarrativeTraceEvent trace;
        }

        [Serializable]
        private sealed class UiCopyTraceEnvelope
        {
            public string type;
            public UiCopyTraceEvent trace;
        }

        [Serializable]
        private sealed class CommandResult
        {
            public string type;
            public bool ok;
            public string message;
        }
    }
}
