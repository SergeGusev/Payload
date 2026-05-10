namespace PolyCopyTrader.Polymarket;

public sealed class PolymarketApiException : Exception
{
    public PolymarketApiException(string component, string operation, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Component = component;
        Operation = operation;
    }

    public string Component { get; }

    public string Operation { get; }
}
