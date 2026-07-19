using System;
using System.Collections.Generic;
using System.Diagnostics;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Narrative
{
    [Serializable]
    public sealed class NarrativeTraceEvent
    {
        public string scenarioId = string.Empty;
        public string beatId = string.Empty;
        public string phase = string.Empty;
        public string timestampUtc = string.Empty;
        public List<NarrativeTraceVariable> variables = new();
    }

    [Serializable]
    public sealed class NarrativeTraceVariable
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    /// <summary>
    /// Runtime call sites can report narrative state without taking a dependency on
    /// authoring transport. Calls and the event body are removed from player builds.
    /// </summary>
    public static class NarrativeTrace
    {
#if UNITY_EDITOR
        public static event Action<NarrativeTraceEvent> Emitted;
#endif

        [Conditional("UNITY_EDITOR")]
        public static void Emit(
            string scenarioId,
            string beatId,
            string phase,
            IReadOnlyList<L10nArg> args = null)
        {
#if UNITY_EDITOR
            var trace = new NarrativeTraceEvent
            {
                scenarioId = scenarioId ?? string.Empty,
                beatId = beatId ?? string.Empty,
                phase = phase ?? string.Empty,
                timestampUtc = DateTime.UtcNow.ToString("O")
            };

            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    trace.variables.Add(new NarrativeTraceVariable
                    {
                        key = args[i].Key,
                        value = args[i].Value
                    });
                }
            }

            Emitted?.Invoke(trace);
#endif
        }
    }
}
