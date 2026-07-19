using System;
using System.Collections.Generic;

namespace DoodleDiplomacy.Localization
{
    [Serializable]
    public sealed class UiCopyCatalogDocument
    {
        public int schemaVersion = 1;
        public string catalogId = "game_ui";
        public string title = string.Empty;
        public string sourceLocale = "en-US";
        public List<string> locales = new();
        public List<UiCopyScreen> screens = new();
        public List<UiCopyTerm> terms = new();
        public List<UiCopyEntry> entries = new();
    }

    [Serializable]
    public sealed class UiCopyScreen
    {
        public string id = string.Empty;
        public string title = string.Empty;
        public string surface = string.Empty;
        public string description = string.Empty;
    }

    [Serializable]
    public sealed class UiCopyTerm
    {
        public string id = string.Empty;
        public string sourceTerm = string.Empty;
        public string targetTerm = string.Empty;
        public string definition = string.Empty;
        public string notes = string.Empty;
        public string status = "draft";
    }

    [Serializable]
    public sealed class UiCopyEntry
    {
        public string key = string.Empty;
        public string sourceText = string.Empty;
        public string domain = string.Empty;
        public string surface = string.Empty;
        public string screenId = string.Empty;
        public string context = string.Empty;
        public string status = "draft";
        public string audience = "player";
        public List<string> tags = new();
        public List<UiCopyLocalizedText> localizedTexts = new();
    }

    [Serializable]
    public sealed class UiCopyLocalizedText
    {
        public string locale = string.Empty;
        public string text = string.Empty;
        public string status = "draft";
    }
}
