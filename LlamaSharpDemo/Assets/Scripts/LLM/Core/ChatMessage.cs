using System;

/// <summary>
/// Chat message payload used by chat-style completion requests.
/// </summary>
[Serializable]
public struct ChatMessage
{
    /// <summary>
    /// Role label such as "system", "user", or "assistant".
    /// </summary>
    public string role;

    /// <summary>
    /// Message content.
    /// </summary>
    public string content;
}
