using System;

/// <summary>
/// Central registration point for the active provider-agnostic LLM service.
/// </summary>
public static class LlmServiceLocator
{
    public static ILlmService Current { get; private set; }

    public static void Register(ILlmService service)
    {
        Current = service;
    }

    public static void Unregister(ILlmService service)
    {
        if (ReferenceEquals(Current, service))
        {
            Current = null;
        }
    }

    public static ILlmService Require()
    {
        if (Current == null)
        {
            throw new InvalidOperationException("No ILlmService is registered. Ensure a runtime adapter or editor service is initialized before running pipelines.");
        }

        return Current;
    }
}
