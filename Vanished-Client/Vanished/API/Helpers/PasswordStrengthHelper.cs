using System.Text.RegularExpressions;

namespace Vanished.API.Helpers
{
    public class PasswordStrengthResult
    {
        public int Score { get; set; }
        public string Label { get; set; } = "Muito fraca";
        public bool HasMinLength { get; set; }
        public bool HasMixedCase { get; set; }
        public bool HasDigit { get; set; }
        public bool HasSymbol { get; set; }
        public bool HasNoSpaces { get; set; }
        public bool IsAcceptable => HasMinLength && HasMixedCase && HasDigit && HasSymbol && HasNoSpaces && Score >= 65;
    }

    public static class PasswordStrengthHelper
    {
        public static PasswordStrengthResult Evaluate(string? password)
        {
            password ??= string.Empty;

            bool hasLower = Regex.IsMatch(password, "[a-z]");
            bool hasUpper = Regex.IsMatch(password, "[A-Z]");
            bool hasDigit = Regex.IsMatch(password, @"\d");
            bool hasSymbol = Regex.IsMatch(password, "[^A-Za-z0-9]");
            bool hasNoSpaces = !Regex.IsMatch(password, @"\s");
            bool hasMinLength = password.Length >= 8;
            int uniqueChars = password.Distinct().Count();

            int score = 0;
            if (password.Length >= 8) score += 25;
            if (password.Length >= 12) score += 10;
            if (hasLower && hasUpper) score += 20;
            if (hasDigit) score += 15;
            if (hasSymbol) score += 20;
            if (uniqueChars >= 6) score += 10;
            if (hasNoSpaces) score += 5;
            score = Math.Min(100, score);

            string label = score switch
            {
                < 30 => "Fraca",
                < 55 => "Razoável",
                < 75 => "Forte",
                _ => "Muito forte"
            };

            return new PasswordStrengthResult
            {
                Score = score,
                Label = label,
                HasMinLength = hasMinLength,
                HasMixedCase = hasLower && hasUpper,
                HasDigit = hasDigit,
                HasSymbol = hasSymbol,
                HasNoSpaces = hasNoSpaces,
            };
        }
    }
}
