using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class ChainlinkBtcUsdCorrelationWorkerTests
{
    [Fact]
    public void ParseChainlinkNodes_ReadsBenchmarkNodes()
    {
        const string body = """
{
  "data": {
    "allStreamValuesGenerics": {
      "nodes": [
        {
          "attributeName": "benchmark",
          "valueNumeric": "80108.829825",
          "validAfterTs": "2026-05-08T19:53:19.123679+00:00"
        },
        {
          "attributeName": "other",
          "valueNumeric": "1.0",
          "validAfterTs": "2026-05-08T19:53:19.123679+00:00"
        },
        {
          "attributeName": "benchmark",
          "valueNumeric": "bad",
          "validAfterTs": "2026-05-08T19:53:19.123679+00:00"
        }
      ]
    }
  }
}
""";

        var nodes = ChainlinkBtcUsdCorrelationWorker.ParseChainlinkNodes(body);

        Assert.Single(nodes);
        Assert.Equal(80108.829825m, nodes[0].PriceUsd);
        Assert.Equal(DateTimeOffset.Parse("2026-05-08T19:53:19.123679+00:00"), nodes[0].ValidAfterUtc);
        Assert.Contains("\"attributeName\"", nodes[0].RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseChainlinkNodes_ReturnsEmptyWhenShapeIsUnexpected()
    {
        var nodes = ChainlinkBtcUsdCorrelationWorker.ParseChainlinkNodes("""{"data":{}}""");

        Assert.Empty(nodes);
    }
}
