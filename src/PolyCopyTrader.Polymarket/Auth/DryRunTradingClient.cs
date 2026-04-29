using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.Auth;

public sealed class DryRunTradingClient(
    PolymarketAuthOptions options,
    ISecretProvider secretProvider,
    ClobV2OrderBuilder orderBuilder,
    ClobV2OrderSigner orderSigner,
    ClobV2OrderPayloadSerializer payloadSerializer) : IPolymarketTradingClient
{
    public async Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(
        ClobV2OrderRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationMessages = orderBuilder.Validate(request).ToList();
        if (validationMessages.Count > 0)
        {
            var rejectedOrder = BuildRejectedOrder(request);
            var rejectedPayload = payloadSerializer.SerializeRedacted(rejectedOrder, null);
            return new ClobV2DryRunOrderResult(
                DryRunOrderStatus.DryRunRejected,
                rejectedOrder,
                null,
                rejectedPayload,
                rejectedPayload,
                validationMessages);
        }

        ClobV2Order order;
        try
        {
            order = orderBuilder.Build(request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            validationMessages.Add($"Dry-run order build failed: {ex.GetType().Name}.");
            var rejectedOrder = BuildRejectedOrder(request);
            var rejectedPayload = payloadSerializer.SerializeRedacted(rejectedOrder, null);
            return new ClobV2DryRunOrderResult(
                DryRunOrderStatus.DryRunRejected,
                rejectedOrder,
                null,
                rejectedPayload,
                rejectedPayload,
                validationMessages);
        }

        string? signature = null;
        if (options.DryRunSigningEnabled)
        {
            var privateKey = await secretProvider.GetSecretAsync(options.DryRunPrivateKeyName, ct);
            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                try
                {
                    var keyAddress = orderSigner.GetAddress(privateKey);
                    if (!string.Equals(keyAddress, request.SignerAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        validationMessages.Add("Dry-run private key does not match the request signer address.");
                    }
                    else
                    {
                        signature = orderSigner.Sign(order, privateKey, options.ChainId);
                        if (!orderSigner.Verify(order, signature, request.SignerAddress, options.ChainId))
                        {
                            validationMessages.Add("Dry-run signature verification failed.");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    validationMessages.Add($"Dry-run signing failed: {ex.GetType().Name}.");
                }
            }
        }

        if (validationMessages.Count > 0)
        {
            signature = null;
        }

        var status = validationMessages.Count > 0
            ? DryRunOrderStatus.DryRunRejected
            : string.IsNullOrWhiteSpace(signature)
                ? DryRunOrderStatus.DryRunUnsigned
                : DryRunOrderStatus.DryRunSigned;

        var payload = payloadSerializer.Serialize(order, status == DryRunOrderStatus.DryRunSigned ? signature : null);
        var redactedPayload = payloadSerializer.SerializeRedacted(order, status == DryRunOrderStatus.DryRunSigned ? signature : null);
        return new ClobV2DryRunOrderResult(status, order, signature, payload, redactedPayload, validationMessages);
    }

    private static ClobV2Order BuildRejectedOrder(ClobV2OrderRequest request)
    {
        return new ClobV2Order(
            request.Salt ?? "0",
            request.MakerAddress,
            request.SignerAddress,
            request.TokenId,
            "0",
            "0",
            request.Side,
            request.SignatureType,
            request.CreatedAtUtc.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ClobV2OrderBuilder.NormalizeBytes32(request.Metadata),
            ClobV2OrderBuilder.NormalizeBytes32(request.Builder),
            "0",
            request.OrderType,
            request.PostOnly,
            request.DeferExec,
            request.NegativeRisk);
    }
}
