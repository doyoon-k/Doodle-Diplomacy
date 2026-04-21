using System.Security.Cryptography;
using System.Text;

namespace DoodleDiplomacy.AI
{
    public static class PreGeneratedObjectImageKeyUtility
    {
        public static string NormalizePromptForKey(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            string trimmed = prompt.Trim().ToLowerInvariant();
            var builder = new StringBuilder(trimmed.Length);
            bool pendingSpace = false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char ch = trimmed[i];
                if (char.IsWhiteSpace(ch))
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(ch);
            }

            return builder.ToString();
        }

        public static string ComputePromptKey(string prompt)
        {
            string normalized = NormalizePromptForKey(prompt);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            byte[] inputBytes = Encoding.UTF8.GetBytes(normalized);
            byte[] hashBytes;
            using (SHA256 sha = SHA256.Create())
            {
                hashBytes = sha.ComputeHash(inputBytes);
            }

            var builder = new StringBuilder(hashBytes.Length * 2);
            for (int i = 0; i < hashBytes.Length; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
