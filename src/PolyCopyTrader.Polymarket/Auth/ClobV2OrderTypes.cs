using System.Numerics;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket.Auth;

public enum ClobV2SignatureType
{
    EOA = 0,
    POLY_PROXY = 1,
    POLY_GNOSIS_SAFE = 2,
    POLY_1271 = 3
}

public enum ClobV2OrderType
{
    GTC,
    FOK,
    GTD,
    FAK
}

public sealed record ClobV2OrderRequest(
    string TokenId,
    TradeSide Side,
    decimal Price,
    decimal SizeShares,
    decimal TickSize,
    decimal MinOrderSize,
    string MakerAddress,
    string SignerAddress,
    ClobV2SignatureType SignatureType,
    ClobV2OrderType OrderType,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? GtdExpirationUtc = null,
    bool NegativeRisk = false,
    string? Salt = null,
    string? Metadata = null,
    string? Builder = null,
    bool PostOnly = true,
    bool DeferExec = false);

public sealed record ClobV2OrderAmounts(
    TradeSide Side,
    BigInteger MakerAmount,
    BigInteger TakerAmount);

public sealed record ClobV2Order(
    string Salt,
    string Maker,
    string Signer,
    string TokenId,
    string MakerAmount,
    string TakerAmount,
    TradeSide Side,
    ClobV2SignatureType SignatureType,
    string Timestamp,
    string Metadata,
    string Builder,
    string Expiration,
    ClobV2OrderType OrderType,
    bool PostOnly,
    bool DeferExec,
    bool NegativeRisk);

public sealed record ClobV2SignedOrder(
    ClobV2Order Order,
    string Signature);

public sealed record ClobV2DryRunOrderResult(
    DryRunOrderStatus Status,
    ClobV2Order Order,
    string? Signature,
    string PayloadJson,
    string RedactedPayloadJson,
    IReadOnlyList<string> ValidationMessages);

public sealed record LiveOrderPlacementResult(
    bool Success,
    string? OrderId,
    string ResponseStatus,
    string? ErrorMessage,
    string? MakingAmount,
    string? TakingAmount,
    string RawResponseJson,
    string RedactedRequestJson);

public sealed record LiveOrderCancellationResult(
    bool Success,
    IReadOnlyList<string> CanceledOrderIds,
    IReadOnlyDictionary<string, string> NotCanceled,
    string RawResponseJson,
    string ErrorMessage = "");

public sealed record LiveOrderStatusResult(
    string OrderId,
    string Status,
    string OriginalSize,
    string SizeMatched,
    string Price,
    string RawResponseJson);
