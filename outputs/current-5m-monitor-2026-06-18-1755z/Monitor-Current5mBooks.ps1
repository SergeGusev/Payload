param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$StartEpochsCsv,

    [int]$IntervalSeconds = 30,

    [int]$StopAfterLastMarketSeconds = 10
)

$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function To-DecimalOrNull($value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return $null
    }

    return [decimal]::Parse([string]$value, $culture)
}

function Get-Levels($book, [string]$side) {
    $levels = $book.$side
    if ($null -eq $levels) {
        return @()
    }

    return @($levels)
}

function Sum-Size($levels) {
    $sum = [decimal]0
    foreach ($level in $levels) {
        $size = To-DecimalOrNull $level.size
        if ($null -ne $size) {
            $sum += $size
        }
    }

    return $sum
}

function TopN-Size($levels, [int]$count, [bool]$descending) {
    $ordered = if ($descending) {
        $levels | Sort-Object { To-DecimalOrNull $_.price } -Descending
    }
    else {
        $levels | Sort-Object { To-DecimalOrNull $_.price }
    }

    return Sum-Size (@($ordered | Select-Object -First $count))
}

function Get-BestPrice($levels, [bool]$maximum) {
    if ($levels.Count -eq 0) {
        return $null
    }

    $prices = @($levels | ForEach-Object { To-DecimalOrNull $_.price } | Where-Object { $null -ne $_ })
    if ($prices.Count -eq 0) {
        return $null
    }

    if ($maximum) {
        return ($prices | Measure-Object -Maximum).Maximum
    }

    return ($prices | Measure-Object -Minimum).Minimum
}

function Get-SizeAtPrice($levels, $price) {
    if ($null -eq $price) {
        return $null
    }

    foreach ($level in $levels) {
        $levelPrice = To-DecimalOrNull $level.price
        if ($null -ne $levelPrice -and $levelPrice -eq $price) {
            return To-DecimalOrNull $level.size
        }
    }

    return $null
}

function Get-BinancePrices {
    $result = @{}
    foreach ($asset in @('BTC', 'ETH', 'SOL')) {
        try {
            $ticker = Invoke-RestMethod -Uri ("https://api.binance.com/api/v3/ticker/price?symbol={0}USDT" -f $asset) -Method Get -TimeoutSec 10
            $result[$asset] = To-DecimalOrNull $ticker.price
        }
        catch {
            $result[$asset] = $null
        }
    }

    return $result
}

function Get-MarketAsset([string]$slug) {
    if ($slug.StartsWith('btc-', [StringComparison]::OrdinalIgnoreCase)) { return 'BTC' }
    if ($slug.StartsWith('eth-', [StringComparison]::OrdinalIgnoreCase)) { return 'ETH' }
    if ($slug.StartsWith('sol-', [StringComparison]::OrdinalIgnoreCase)) { return 'SOL' }
    return ''
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$samplesPath = Join-Path $OutputDir 'samples.csv'
$rawPath = Join-Path $OutputDir 'raw_books.jsonl'
$marketsPath = Join-Path $OutputDir 'markets.csv'
$statusPath = Join-Path $OutputDir 'status.json'
$errorsPath = Join-Path $OutputDir 'errors.log'

$startEpochs = @()
foreach ($rawStartEpoch in $StartEpochsCsv.Split(',')) {
    $normalizedStartEpoch = $rawStartEpoch.Trim()
    if ($normalizedStartEpoch.Length -gt 0) {
        $startEpochs += [int64]::Parse($normalizedStartEpoch, $culture)
    }
}

$assets = @('btc', 'eth', 'sol')
$slugs = @()
foreach ($startEpoch in $startEpochs) {
    foreach ($asset in $assets) {
        $slugs += ("{0}-updown-5m-{1}" -f $asset, $startEpoch)
    }
}

$gammaUri = 'https://gamma-api.polymarket.com/markets?limit=' + $slugs.Count + '&active=true&closed=false'
foreach ($slug in $slugs) {
    $gammaUri += '&slug=' + [Uri]::EscapeDataString($slug)
}

$markets = @(Invoke-RestMethod -Uri $gammaUri -Method Get -TimeoutSec 20)
if ($markets.Count -eq 1 -and $markets[0] -is [System.Array]) {
    $markets = @($markets[0])
}
$marketRows = @()
$tokens = @()
foreach ($market in $markets) {
    $outcomes = if ($market.outcomes -is [string]) { $market.outcomes | ConvertFrom-Json } else { $market.outcomes }
    $clobTokenIds = if ($market.clobTokenIds -is [string]) { $market.clobTokenIds | ConvertFrom-Json } else { $market.clobTokenIds }
    $assetSymbol = Get-MarketAsset $market.slug
    $startEpoch = [int64]($market.slug -replace '^.*-', '')
    $startUtc = [DateTimeOffset]::FromUnixTimeSeconds($startEpoch)
    $endUtc = $startUtc.AddMinutes(5)

    $marketRows += [pscustomobject]@{
        asset = $assetSymbol
        market_id = $market.id
        slug = $market.slug
        question = $market.question
        active = $market.active
        closed = $market.closed
        accepting_orders = $market.acceptingOrders
        enable_order_book = $market.enableOrderBook
        market_start_utc = $startUtc.ToString('o')
        market_end_utc = $endUtc.ToString('o')
        gamma_end_date = $market.endDate
        outcomes = ($outcomes | ConvertTo-Json -Compress)
        clob_token_ids = ($clobTokenIds | ConvertTo-Json -Compress)
    }

    for ($i = 0; $i -lt $clobTokenIds.Count; $i++) {
        $tokens += [pscustomobject]@{
            asset = $assetSymbol
            slug = $market.slug
            market_id = $market.id
            market_start_utc = $startUtc
            market_end_utc = $endUtc
            outcome = [string]$outcomes[$i]
            token_id = [string]$clobTokenIds[$i]
        }
    }
}

$marketRows | Sort-Object asset, market_start_utc | Export-Csv -LiteralPath $marketsPath -NoTypeInformation -Encoding UTF8

$stopAtUtc = ([DateTimeOffset]::FromUnixTimeSeconds(($startEpochs | Measure-Object -Maximum).Maximum)).AddMinutes(5).AddSeconds($StopAfterLastMarketSeconds)
$sampleNumber = 0
$startReferencePrices = @{}

while ([DateTimeOffset]::UtcNow -le $stopAtUtc) {
    $sampleUtc = [DateTimeOffset]::UtcNow
    $sampleNumber++
    $binance = Get-BinancePrices
    $rows = @()

    foreach ($token in $tokens) {
        $assetPrice = $binance[$token.asset]
        $marketKey = $token.asset + '|' + $token.slug
        if ($sampleUtc -ge $token.market_start_utc -and -not $startReferencePrices.ContainsKey($marketKey) -and $null -ne $assetPrice) {
            $startReferencePrices[$marketKey] = $assetPrice
        }

        $startReference = if ($startReferencePrices.ContainsKey($marketKey)) { $startReferencePrices[$marketKey] } else { $null }
        $moveBps = if ($null -ne $assetPrice -and $null -ne $startReference -and $startReference -ne 0) {
            (($assetPrice - $startReference) / $startReference) * 10000
        }
        else {
            $null
        }

        try {
            $bookUri = 'https://clob.polymarket.com/book?token_id=' + [Uri]::EscapeDataString($token.token_id)
            $book = Invoke-RestMethod -Uri $bookUri -Method Get -TimeoutSec 15
            $bids = Get-Levels $book 'bids'
            $asks = Get-Levels $book 'asks'
            $bestBid = Get-BestPrice $bids $true
            $bestAsk = Get-BestPrice $asks $false
            $bidSize = Get-SizeAtPrice $bids $bestBid
            $askSize = Get-SizeAtPrice $asks $bestAsk
            $spread = if ($null -ne $bestBid -and $null -ne $bestAsk) { $bestAsk - $bestBid } else { $null }
            $secondsFromStart = ($sampleUtc - $token.market_start_utc).TotalSeconds
            $secondsToStart = ($token.market_start_utc - $sampleUtc).TotalSeconds
            $secondsToEnd = ($token.market_end_utc - $sampleUtc).TotalSeconds

            $rows += [pscustomobject]@{
                sample_number = $sampleNumber
                sample_utc = $sampleUtc.ToString('o')
                asset = $token.asset
                market_slug = $token.slug
                market_id = $token.market_id
                market_start_utc = $token.market_start_utc.ToString('o')
                market_end_utc = $token.market_end_utc.ToString('o')
                seconds_to_start = [Math]::Round($secondsToStart, 3)
                seconds_from_start = [Math]::Round($secondsFromStart, 3)
                seconds_to_end = [Math]::Round($secondsToEnd, 3)
                outcome = $token.outcome
                token_id = $token.token_id
                best_bid = $bestBid
                best_bid_size = $bidSize
                best_ask = $bestAsk
                best_ask_size = $askSize
                spread = $spread
                bid_levels = $bids.Count
                ask_levels = $asks.Count
                top5_bid_size = TopN-Size $bids 5 $true
                top5_ask_size = TopN-Size $asks 5 $false
                total_bid_size = Sum-Size $bids
                total_ask_size = Sum-Size $asks
                book_timestamp = $book.timestamp
                book_market = $book.market
                binance_price = $assetPrice
                start_reference_price = $startReference
                move_from_start_bps = $moveBps
                error = ''
            }

            $rawRecord = [pscustomobject]@{
                sample_number = $sampleNumber
                sample_utc = $sampleUtc.ToString('o')
                asset = $token.asset
                market_slug = $token.slug
                outcome = $token.outcome
                token_id = $token.token_id
                book = $book
            }
            $rawRecord | ConvertTo-Json -Depth 100 -Compress | Add-Content -LiteralPath $rawPath -Encoding UTF8
        }
        catch {
            $message = $_.Exception.Message
            $rows += [pscustomobject]@{
                sample_number = $sampleNumber
                sample_utc = $sampleUtc.ToString('o')
                asset = $token.asset
                market_slug = $token.slug
                market_id = $token.market_id
                market_start_utc = $token.market_start_utc.ToString('o')
                market_end_utc = $token.market_end_utc.ToString('o')
                seconds_to_start = [Math]::Round(($token.market_start_utc - $sampleUtc).TotalSeconds, 3)
                seconds_from_start = [Math]::Round(($sampleUtc - $token.market_start_utc).TotalSeconds, 3)
                seconds_to_end = [Math]::Round(($token.market_end_utc - $sampleUtc).TotalSeconds, 3)
                outcome = $token.outcome
                token_id = $token.token_id
                best_bid = $null
                best_bid_size = $null
                best_ask = $null
                best_ask_size = $null
                spread = $null
                bid_levels = 0
                ask_levels = 0
                top5_bid_size = $null
                top5_ask_size = $null
                total_bid_size = $null
                total_ask_size = $null
                book_timestamp = ''
                book_market = ''
                binance_price = $assetPrice
                start_reference_price = $startReference
                move_from_start_bps = $moveBps
                error = $message
            }

            ("{0} {1} {2} {3}" -f $sampleUtc.ToString('o'), $token.slug, $token.outcome, $message) |
                Add-Content -LiteralPath $errorsPath -Encoding UTF8
        }
    }

    if ($rows.Count -gt 0) {
        if (Test-Path -LiteralPath $samplesPath) {
            $rows | Export-Csv -LiteralPath $samplesPath -NoTypeInformation -Append -Encoding UTF8
        }
        else {
            $rows | Export-Csv -LiteralPath $samplesPath -NoTypeInformation -Encoding UTF8
        }
    }

    $status = [pscustomobject]@{
        status = 'running'
        sample_number = $sampleNumber
        sample_utc = $sampleUtc.ToString('o')
        stop_at_utc = $stopAtUtc.ToString('o')
        rows_written = if (Test-Path -LiteralPath $samplesPath) { (Import-Csv -LiteralPath $samplesPath).Count } else { 0 }
        markets = $markets.Count
        tokens = $tokens.Count
    }
    $status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statusPath -Encoding UTF8

    $remaining = ($stopAtUtc - [DateTimeOffset]::UtcNow).TotalSeconds
    if ($remaining -le 0) {
        break
    }

    Start-Sleep -Seconds ([Math]::Min($IntervalSeconds, [Math]::Max(1, [int][Math]::Ceiling($remaining))))
}

([pscustomobject]@{
    status = 'completed'
    completed_utc = ([DateTimeOffset]::UtcNow).ToString('o')
    stop_at_utc = $stopAtUtc.ToString('o')
    sample_number = $sampleNumber
    rows_written = if (Test-Path -LiteralPath $samplesPath) { (Import-Csv -LiteralPath $samplesPath).Count } else { 0 }
    markets = $markets.Count
    tokens = $tokens.Count
}) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statusPath -Encoding UTF8
