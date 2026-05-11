using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Service.Startup;

public static class ClobMinimumLiveOrderSmokeCommand
{
    private const string SubmitFlag = "--submit";
    private const int PageLimit = 100;
    private const int MaxPages = 5;

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        var submit = args.Contains(SubmitFlag, StringComparer.OrdinalIgnoreCase);
        var secretProvider = PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth);
        var errorSink = new NullPolymarketApiErrorSink();
        var httpLogSink = new NullPolymarketHttpLogSink();
        using var gammaHttpClient = new HttpClient();
        using var clobHttpClient = new HttpClient();
        using var geoHttpClient = new HttpClient();
        using var tradingHttpClient = new HttpClient();

        var gammaClient = new PolymarketGammaClient(
            gammaHttpClient,
            configuration.Polymarket,
            errorSink,
            httpLogSink);
        var clobClient = new PolymarketClobPublicClient(
            clobHttpClient,
            configuration.Polymarket,
            errorSink,
            httpLogSink);
        var geoClient = new PolymarketGeoClient(
            geoHttpClient,
            configuration.Polymarket,
            errorSink,
            httpLogSink);
        var tradingClient = new PolymarketTradingClient(
            tradingHttpClient,
            configuration.Polymarket,
            configuration.PolymarketAuth,
            secretProvider,
            new ClobV2OrderBuilder(new OrderAmountCalculator()),
            new ClobV2OrderSigner(),
            new ClobV2OrderPayloadSerializer(),
            new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner()),
            errorSink,
            httpLogSink);

        return await ExecuteAsync(
            configuration,
            secretProvider,
            gammaClient,
            clobClient,
            geoClient,
            tradingClient,
            output,
            submit,
            cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        IPolymarketGammaClient gammaClient,
        IPolymarketClobPublicClient clobClient,
        IPolymarketGeoClient geoClient,
        IPolymarketTradingClient tradingClient,
        TextWriter output,
        bool submit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(gammaClient);
        ArgumentNullException.ThrowIfNull(clobClient);
        ArgumentNullException.ThrowIfNull(geoClient);
        ArgumentNullException.ThrowIfNull(tradingClient);
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync("CLOB minimal live order smoke: one GTC BUY, postOnly=false, minimum market size; open residue is cancelled immediately.");
        await output.WriteLineAsync($"Submit enabled: {submit}");
        await output.WriteLineAsync($"Bot mode: {configuration.Bot.Mode}");
        await output.WriteLineAsync($"Live trading enabled: {configuration.Bot.EnableLiveTrading}");
        await output.WriteLineAsync($"Signer address: {RedactAddress(configuration.PolymarketAuth.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(ResolveMakerAddress(configuration.PolymarketAuth))}");
        await output.WriteLineAsync($"Signature type: {configuration.PolymarketAuth.SignatureType}");

        var validation = await ValidatePreflightAsync(
            configuration,
            secretProvider,
            geoClient,
            clobClient,
            submit,
            cancellationToken);
        if (validation.Count > 0)
        {
            await output.WriteLineAsync("CLOB minimal live order smoke status: Refused");
            foreach (var message in validation)
            {
                await output.WriteLineAsync($"Reason: {message}");
            }

            return 1;
        }

        var candidate = await FindCandidateAsync(
            configuration,
            gammaClient,
            clobClient,
            cancellationToken);
        if (candidate is null)
        {
            await output.WriteLineAsync("CLOB minimal live order smoke status: NoCandidate");
            await output.WriteLineAsync("Reason: no active accepting market with an ask at or below the configured live notional cap was found.");
            return 1;
        }

        await output.WriteLineAsync($"Market: {candidate.MarketSlug}");
        await output.WriteLineAsync($"Outcome: {candidate.Outcome}");
        await output.WriteLineAsync($"Token: {RedactLong(candidate.TokenId)}");
        await output.WriteLineAsync($"Limit price: {FormatDecimal(candidate.Price)}");
        await output.WriteLineAsync($"Requested size: {FormatDecimal(candidate.SizeShares)}");
        await output.WriteLineAsync($"Estimated notional: {FormatDecimal(candidate.NotionalUsd)}");
        await output.WriteLineAsync($"Tick size: {FormatNullableDecimal(candidate.OrderBook.TickSize)}");
        await output.WriteLineAsync($"Min order size: {FormatNullableDecimal(candidate.OrderBook.MinOrderSize)}");
        await output.WriteLineAsync($"Negative risk: {candidate.NegativeRisk}");

        var request = new ClobV2OrderRequest(
            candidate.TokenId,
            TradeSide.Buy,
            candidate.Price,
            candidate.SizeShares,
            candidate.OrderBook.TickSize ?? candidate.MarketTickSize ?? 0.01m,
            candidate.OrderBook.MinOrderSize ?? candidate.MarketMinOrderSize ?? 1m,
            ResolveMakerAddress(configuration.PolymarketAuth),
            configuration.PolymarketAuth.SigningAddress,
            ParseSignatureType(configuration.PolymarketAuth.SignatureType),
            ClobV2OrderType.GTC,
            DateTimeOffset.UtcNow,
            NegativeRisk: candidate.NegativeRisk,
            PostOnly: false);

        LiveOrderPlacementResult placement;
        try
        {
            placement = await tradingClient.PlaceLiveOrderAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("CLOB minimal live order smoke status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"Submit success: {placement.Success}");
        await output.WriteLineAsync($"Submit status: {placement.ResponseStatus}");
        await output.WriteLineAsync($"Order id: {RedactLong(placement.OrderId)}");
        if (!string.IsNullOrWhiteSpace(placement.ErrorMessage))
        {
            await output.WriteLineAsync($"Submit error: {placement.ErrorMessage}");
        }
        if (!placement.Success && !string.IsNullOrWhiteSpace(placement.RawResponseJson))
        {
            await output.WriteLineAsync($"Submit response: {placement.RawResponseJson}");
        }

        var cancelOk = true;
        if (placement.Success &&
            !string.IsNullOrWhiteSpace(placement.OrderId) &&
            !string.Equals(placement.ResponseStatus, "matched", StringComparison.OrdinalIgnoreCase))
        {
            var cancel = await tradingClient.CancelOrderAsync(placement.OrderId, cancellationToken);
            cancelOk = cancel.Success;
            await output.WriteLineAsync($"Cancel open residue: {cancel.Success}");
            await output.WriteLineAsync($"Canceled count: {cancel.CanceledOrderIds.Count}");
            await output.WriteLineAsync($"Not canceled count: {cancel.NotCanceled.Count}");
        }
        else if (placement.Success &&
            string.IsNullOrWhiteSpace(placement.OrderId))
        {
            var cancel = await tradingClient.CancelAllOrdersAsync(cancellationToken);
            cancelOk = cancel.Success;
            await output.WriteLineAsync("Order id was missing after a successful submit; cancel-all was attempted.");
            await output.WriteLineAsync($"Cancel-all success: {cancel.Success}");
            await output.WriteLineAsync($"Canceled count: {cancel.CanceledOrderIds.Count}");
            await output.WriteLineAsync($"Not canceled count: {cancel.NotCanceled.Count}");
        }

        var ok = placement.Success && cancelOk;
        await output.WriteLineAsync($"CLOB minimal live order smoke status: {(ok ? "OK" : "Rejected")}");
        return ok ? 0 : 1;
    }

    private static async Task<IReadOnlyList<string>> ValidatePreflightAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        IPolymarketGeoClient geoClient,
        IPolymarketClobPublicClient clobClient,
        bool submit,
        CancellationToken cancellationToken)
    {
        var validation = new List<string>();
        if (!submit)
        {
            validation.Add($"The live submit flag is required: {SubmitFlag}.");
        }

        if (configuration.Bot.Mode != BotMode.Live)
        {
            validation.Add("Bot mode is not Live.");
        }

        if (!configuration.Bot.EnableLiveTrading)
        {
            validation.Add("Live trading is not explicitly enabled.");
        }

        if (!configuration.PolymarketAuth.Enabled)
        {
            validation.Add("Polymarket auth is disabled.");
        }

        if (string.IsNullOrWhiteSpace(configuration.PolymarketAuth.SigningAddress))
        {
            validation.Add("Signing address is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ResolveMakerAddress(configuration.PolymarketAuth)))
        {
            validation.Add("Funder/maker address is not configured.");
        }

        if (configuration.LiveTrading.MaxOrderNotionalUsd <= 0m)
        {
            validation.Add("LiveTrading.MaxOrderNotionalUsd must be greater than zero.");
        }

        var authReadiness = await new PolymarketAuthReadinessService(
            configuration.PolymarketAuth,
            secretProvider,
            new PolymarketL2HmacSigner()).GetReadinessAsync(cancellationToken);
        if (!authReadiness.CanAuthenticate)
        {
            validation.Add("Polymarket auth is not ready: " + string.Join(", ", authReadiness.MissingRequirements));
        }

        try
        {
            var geoblock = await geoClient.GetGeoblockStatusAsync(cancellationToken);
            if (geoblock.Blocked)
            {
                validation.Add($"Geoblock is active for VPS IP {geoblock.Ip ?? "unknown"}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("Geoblock check failed: " + ex.Message);
        }

        try
        {
            var serverTime = await clobClient.GetServerTimeAsync(cancellationToken);
            var clockCheckUtc = DateTimeOffset.UtcNow;
            if (Math.Abs((serverTime - clockCheckUtc).TotalSeconds) > configuration.LiveTrading.MaxClockDriftSeconds)
            {
                validation.Add("CLOB server time drift exceeds configured limit.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validation.Add("CLOB server time check failed: " + ex.Message);
        }

        return validation;
    }

    private static async Task<SmokeCandidate?> FindCandidateAsync(
        AppConfiguration configuration,
        IPolymarketGammaClient gammaClient,
        IPolymarketClobPublicClient clobClient,
        CancellationToken cancellationToken)
    {
        var markets = new List<PolymarketGammaMarket>();
        for (var page = 0; page < MaxPages; page++)
        {
            var fetched = await gammaClient.GetActiveMarketsAsync(PageLimit, page * PageLimit, cancellationToken);
            if (fetched.Count == 0)
            {
                break;
            }

            markets.AddRange(fetched);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var orderedMarkets = markets
            .Where(IsTradeableMarket)
            .Where(market => IsCurrentMarketWindow(market, nowUtc))
            .OrderByDescending(BtcUpDown5mMarketAnalyzer.IsCandidate)
            .ThenBy(market => BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market) ?? DateTimeOffset.MaxValue)
            .ThenBy(market => market.EndDateUtc ?? DateTimeOffset.MaxValue)
            .ToArray();
        foreach (var market in orderedMarkets)
        {
            for (var index = 0; index < market.ClobTokenIds.Count; index++)
            {
                var tokenId = market.ClobTokenIds[index];
                if (string.IsNullOrWhiteSpace(tokenId))
                {
                    continue;
                }

                OrderBookSnapshot? orderBook;
                try
                {
                    orderBook = await clobClient.GetOrderBookAsync(tokenId, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue;
                }

                if (orderBook?.BestAsk is not { } bestAsk)
                {
                    continue;
                }

                var tickSize = orderBook.TickSize ?? market.OrderPriceMinTickSize ?? 0.01m;
                if (bestAsk < tickSize || bestAsk > 1m - tickSize)
                {
                    continue;
                }

                var minOrderSize = orderBook.MinOrderSize ?? market.OrderMinSize ?? 1m;
                if (minOrderSize <= 0m)
                {
                    continue;
                }

                var sizeShares = RoundUp(minOrderSize, 2);
                var notional = bestAsk * sizeShares;
                if (notional <= 0m || notional > configuration.LiveTrading.MaxOrderNotionalUsd)
                {
                    continue;
                }

                var outcome = index < market.Outcomes.Count ? market.Outcomes[index] : string.Empty;
                return new SmokeCandidate(
                    market.Slug,
                    tokenId,
                    outcome,
                    orderBook,
                    bestAsk,
                    sizeShares,
                    notional,
                    market.NegativeRisk || orderBook.NegativeRisk,
                    market.OrderMinSize,
                    market.OrderPriceMinTickSize);
            }
        }

        return null;
    }

    private static bool IsTradeableMarket(PolymarketGammaMarket market)
    {
        return market.Active &&
            !market.Closed &&
            !market.Archived &&
            market.AcceptingOrders &&
            market.EnableOrderBook &&
            market.ClobTokenIds.Count > 0;
    }

    private static bool IsCurrentMarketWindow(PolymarketGammaMarket market, DateTimeOffset nowUtc)
    {
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        return marketStartUtc is { } startUtc &&
            startUtc <= nowUtc &&
            market.EndDateUtc is { } endUtc &&
            endUtc > nowUtc;
    }

    private static string ResolveMakerAddress(PolymarketAuthOptions authOptions)
    {
        return string.IsNullOrWhiteSpace(authOptions.FunderAddress)
            ? authOptions.SigningAddress
            : authOptions.FunderAddress;
    }

    private static ClobV2SignatureType ParseSignatureType(string value)
    {
        return Enum.TryParse<ClobV2SignatureType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ClobV2SignatureType.EOA;
    }

    private static decimal RoundUp(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Ceiling(value * factor) / factor;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static string FormatNullableDecimal(decimal? value)
    {
        return value is { } actual ? FormatDecimal(actual) : "unknown";
    }

    private static string RedactAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "not configured";
        }

        return value.Length <= 12
            ? value
            : string.Concat(value.AsSpan(0, 6), "...", value.AsSpan(value.Length - 4, 4));
    }

    private static string RedactLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Length <= 18
            ? value
            : string.Concat(value.AsSpan(0, 10), "...", value.AsSpan(value.Length - 6, 6));
    }

    private sealed record SmokeCandidate(
        string MarketSlug,
        string TokenId,
        string Outcome,
        OrderBookSnapshot OrderBook,
        decimal Price,
        decimal SizeShares,
        decimal NotionalUsd,
        bool NegativeRisk,
        decimal? MarketMinOrderSize,
        decimal? MarketTickSize);
}
