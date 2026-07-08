namespace PolyCopyTrader.Domain.Configuration;

public static class LiveOrderRejectionClassifier
{
    public const string InsufficientBalanceOrAllowanceStatus = "InsufficientBalanceOrAllowance";

    public static bool IsInsufficientBalanceOrAllowance(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Replace("\\u003e", ">", StringComparison.OrdinalIgnoreCase);
        return normalized.Contains("not enough balance / allowance", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("balance is not enough", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("not enough allowance", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatInsufficientBalanceOrAllowance(string? detail)
    {
        const string summary = "Polymarket rejected order: insufficient balance or allowance.";
        return string.IsNullOrWhiteSpace(detail)
            ? summary
            : summary + " " + detail;
    }
}
