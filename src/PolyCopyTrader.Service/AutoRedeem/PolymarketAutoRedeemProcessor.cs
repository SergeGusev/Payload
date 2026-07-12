using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.AutoRedeem;

public sealed class PolymarketAutoRedeemProcessor(
    ILogger<PolymarketAutoRedeemProcessor> logger,
    PolymarketAutoRedeemOptions options,
    PolymarketAuthOptions authOptions,
    IPolymarketDataApiClient dataApiClient,
    PolymarketRedeemCalldataBuilder calldataBuilder,
    IPolymarketRelayerClient relayerClient,
    IAppRepository repository) : IPolymarketAutoRedeemProcessor
{
    private static readonly int[] BinaryIndexSets = [1, 2];

    public async Task<PolymarketAutoRedeemCycleResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var wallet = ResolveWalletAddress();
        if (string.IsNullOrWhiteSpace(wallet))
        {
            logger.LogWarning("Polymarket auto redeem skipped because wallet address is not configured.");
            return new PolymarketAutoRedeemCycleResult(0, 0, 0, 1, 0);
        }

        var positions = await FetchCurrentPositionsAsync(wallet, cancellationToken);
        var redeemable = positions
            .Where(position => position.Redeemable == true)
            .Where(position => !string.IsNullOrWhiteSpace(position.ConditionId))
            .Where(position => GetRedeemableValueUsd(position) >= options.MinRedeemableValueUsd)
            .GroupBy(position => NormalizeKey(position.ConditionId), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(GetRedeemableValueUsd).First())
            .Take(options.MaxClaimsPerCycle)
            .ToArray();

        var attemptsRecorded = 0;
        var skipped = 0;
        var submitted = 0;
        var liveSubmissionsStarted = 0;
        var maxLiveSubmissions = Math.Clamp(options.MaxLiveSubmissionsPerCycle, 1, options.MaxClaimsPerCycle);
        foreach (var position in redeemable)
        {
            var conditionId = NormalizeBytes32(position.ConditionId);
            var existing = await repository.GetPolymarketAutoRedeemAttemptAsync(
                NormalizeKey(wallet),
                conditionId,
                cancellationToken);

            if (existing is not null &&
                (string.Equals(existing.Status, PolymarketAutoRedeemStatuses.Submitted, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(existing.Status, PolymarketAutoRedeemStatuses.Confirmed, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            var attempt = CreateAttempt(position, wallet, conditionId);
            if (ShouldSubmit(attempt) && liveSubmissionsStarted < maxLiveSubmissions)
            {
                liveSubmissionsStarted++;
                attempt = await SubmitAttemptAsync(attempt, cancellationToken);
            }

            await repository.UpsertPolymarketAutoRedeemAttemptAsync(attempt, cancellationToken);
            attemptsRecorded++;
            if (string.Equals(attempt.Status, PolymarketAutoRedeemStatuses.SkippedUnsupported, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
            }
            else if (string.Equals(attempt.Status, PolymarketAutoRedeemStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
            {
                submitted++;
            }
        }

        return new PolymarketAutoRedeemCycleResult(
            positions.Count,
            redeemable.Length,
            attemptsRecorded,
            skipped,
            submitted);
    }

    private async Task<IReadOnlyList<PolymarketDataApiPosition>> FetchCurrentPositionsAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var positions = new List<PolymarketDataApiPosition>();
        var limit = Math.Clamp(options.CurrentPositionsLimit, 1, 1_000);
        var maxPages = Math.Clamp(options.MaxPositionPages, 1, 20);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (var page = 0; page < maxPages; page++)
        {
            var pagePositions = await dataApiClient.GetUserCurrentPositionsAsync(
                wallet,
                limit,
                page * limit,
                timestampCacheBuster: timestamp,
                cancellationToken: cancellationToken);

            positions.AddRange(pagePositions);
            if (pagePositions.Count < limit)
            {
                break;
            }
        }

        return positions;
    }

    private PolymarketAutoRedeemAttempt CreateAttempt(
        PolymarketDataApiPosition position,
        string wallet,
        string conditionId)
    {
        var now = DateTimeOffset.UtcNow;
        var status = IsLiveSubmitMode()
            ? PolymarketAutoRedeemStatuses.SubmitPending
            : PolymarketAutoRedeemStatuses.DryRunReady;
        string? lastError = null;
        var calldata = string.Empty;

        if (position.NegativeRisk == true)
        {
            status = PolymarketAutoRedeemStatuses.SkippedUnsupported;
            lastError = "Negative-risk redeem uses the NegRisk adapter path and is not enabled in auto redeem.";
        }
        else if (position.OutcomeIndex is > 1)
        {
            status = PolymarketAutoRedeemStatuses.SkippedUnsupported;
            lastError = "Only binary markets with outcomeIndex 0 or 1 are supported by the current auto redeem path.";
        }
        else if (!IsBytes32(conditionId))
        {
            status = PolymarketAutoRedeemStatuses.SkippedUnsupported;
            lastError = "Condition id is not a 0x-prefixed bytes32 value.";
        }
        else
        {
            calldata = calldataBuilder.BuildRedeemPositions(
                options.CollateralTokenAddress,
                options.ParentCollectionId,
                conditionId,
                BinaryIndexSets);

        }

        return new PolymarketAutoRedeemAttempt(
            Guid.NewGuid(),
            NormalizeKey(wallet),
            ResolveProxyWalletAddress(),
            conditionId,
            position.AssetId,
            position.MarketSlug,
            position.MarketTitle,
            position.Outcome,
            position.OutcomeIndex,
            GetRedeemableValueUsd(position),
            position.Size,
            status,
            options.DryRun || !options.AutoSubmitEnabled,
            options.AutoSubmitEnabled,
            options.ConditionalTokensAddress,
            calldata,
            options.CollateralTokenAddress,
            options.ParentCollectionId,
            BinaryIndexSets,
            null,
            null,
            lastError,
            now,
            now,
            null,
            null,
            now,
            string.IsNullOrWhiteSpace(position.RawJson) ? "{}" : position.RawJson);
    }

    private bool ShouldSubmit(PolymarketAutoRedeemAttempt attempt)
    {
        return options.AutoSubmitEnabled &&
            !options.DryRun &&
            string.Equals(attempt.Status, PolymarketAutoRedeemStatuses.SubmitPending, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PolymarketAutoRedeemAttempt> SubmitAttemptAsync(
        PolymarketAutoRedeemAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!string.Equals(options.WalletType, "WALLET", StringComparison.OrdinalIgnoreCase))
        {
            return attempt with
            {
                Status = PolymarketAutoRedeemStatuses.Failed,
                DryRun = false,
                LastError = "Live auto redeem currently supports WalletType=WALLET only.",
                UpdatedAtUtc = now
            };
        }

        var ownerAddress = authOptions.SigningAddress.Trim();
        if (string.IsNullOrWhiteSpace(ownerAddress))
        {
            return attempt with
            {
                Status = PolymarketAutoRedeemStatuses.Failed,
                DryRun = false,
                LastError = "PolymarketAuth.SigningAddress is required for Deposit Wallet auto redeem.",
                UpdatedAtUtc = now
            };
        }

        try
        {
            var result = await relayerClient.SubmitDepositWalletBatchAsync(
                ownerAddress,
                attempt.Wallet,
                [new PolymarketDepositWalletCall(attempt.TargetContract, "0", attempt.Calldata)],
                BuildMetadata(attempt),
                cancellationToken);

            return attempt with
            {
                Status = PolymarketAutoRedeemStatuses.Submitted,
                DryRun = false,
                RelayerTransactionId = result.TransactionId,
                TransactionHash = result.TransactionHash,
                LastError = null,
                SubmittedAtUtc = now,
                UpdatedAtUtc = now
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Polymarket auto redeem submit failed for condition {ConditionId}.",
                attempt.ConditionId);
            var isTransient = IsTransientRelayerFailure(ex);

            return attempt with
            {
                Status = isTransient
                    ? PolymarketAutoRedeemStatuses.SubmitRetryPending
                    : PolymarketAutoRedeemStatuses.Failed,
                DryRun = false,
                LastError = ex.Message,
                UpdatedAtUtc = now
            };
        }
    }

    private bool IsLiveSubmitMode()
    {
        return options.AutoSubmitEnabled && !options.DryRun;
    }

    private static bool IsTransientRelayerFailure(Exception ex)
    {
        if (ex is not PolymarketApiException apiException)
        {
            return false;
        }

        return apiException.Message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase) ||
            apiException.Message.Contains("HTTP 408", StringComparison.OrdinalIgnoreCase) ||
            apiException.Message.Contains("HTTP 502", StringComparison.OrdinalIgnoreCase) ||
            apiException.Message.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase) ||
            apiException.Message.Contains("HTTP 504", StringComparison.OrdinalIgnoreCase) ||
            apiException.Message.Contains("wallet busy", StringComparison.OrdinalIgnoreCase) ||
            apiException.Message.Contains("nonce", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveWalletAddress()
    {
        if (!string.IsNullOrWhiteSpace(options.WalletAddress))
        {
            return options.WalletAddress.Trim();
        }

        if (!string.IsNullOrWhiteSpace(authOptions.FunderAddress))
        {
            return authOptions.FunderAddress.Trim();
        }

        return authOptions.SigningAddress.Trim();
    }

    private string? ResolveProxyWalletAddress()
    {
        if (!string.IsNullOrWhiteSpace(options.ProxyWalletAddress))
        {
            return NormalizeKey(options.ProxyWalletAddress);
        }

        return string.IsNullOrWhiteSpace(authOptions.FunderAddress)
            ? null
            : NormalizeKey(authOptions.FunderAddress);
    }

    private static string BuildMetadata(PolymarketAutoRedeemAttempt attempt)
    {
        return string.IsNullOrWhiteSpace(attempt.MarketSlug)
            ? $"auto-redeem:{attempt.ConditionId}"
            : $"auto-redeem:{attempt.MarketSlug}:{attempt.ConditionId}";
    }

    private static decimal GetRedeemableValueUsd(PolymarketDataApiPosition position)
    {
        if (position.Redeemable == true && position.Size is > 0m)
        {
            return position.Size.Value;
        }

        return position.CurrentValue ?? position.Size ?? position.TotalBought;
    }

    private static string NormalizeKey(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeBytes32(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? "0x" + trimmed[2..].ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }

    private static bool IsBytes32(string value)
    {
        return value.Length == 66 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
