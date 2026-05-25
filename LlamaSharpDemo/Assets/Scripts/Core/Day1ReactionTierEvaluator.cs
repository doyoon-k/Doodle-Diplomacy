namespace DoodleDiplomacy.Core
{
    public static class Day1ReactionTierEvaluator
    {
        public static string NormalizeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : label.Trim().ToLowerInvariant();
        }
    }
}
