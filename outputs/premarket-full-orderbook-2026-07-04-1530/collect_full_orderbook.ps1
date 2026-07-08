$ErrorActionPreference = 'Continue'

$OutputDir = 'D:\My\Business\PolyMarket\outputs\premarket-full-orderbook-2026-07-04-1530'
$JsonlPath = Join-Path $OutputDir 'full_orderbook_captures.jsonl'
$SummaryCsvPath = Join-Path $OutputDir 'full_orderbook_summary.csv'
$LevelsCsvPath = Join-Path $OutputDir 'full_orderbook_levels.csv'
$ScheduleCsvPath = Join-Path $OutputDir 'capture_schedule.csv'
$LogPath = Join-Path $OutputDir 'collector.log'

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Remove-Item -LiteralPath $JsonlPath, $SummaryCsvPath, $LevelsCsvPath, $ScheduleCsvPath, $LogPath -Force -ErrorAction SilentlyContinue

function Write-Log([string]$message) {
  Add-Content -LiteralPath $LogPath -Value ("{0} {1}" -f (Get-Date).ToString('o'), $message) -Encoding UTF8
}

function Get-NextMarketStartUtc {
  $now = [DateTimeOffset]::UtcNow
  $base = [DateTimeOffset]::new($now.Year, $now.Month, $now.Day, $now.Hour, 0, 0, [TimeSpan]::Zero)
  $minute = [Math]::Ceiling($now.Minute / 5.0) * 5
  if ($minute -ge 60) {
    $base = $base.AddHours(1)
    $minute = 0
  }

  $start = $base.AddMinutes($minute)
  if ($start.AddSeconds(-30) -le $now.AddSeconds(5)) {
    $start = $start.AddMinutes(5)
  }

  return $start
}

function To-DecimalOrNull($value) {
  if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
    return $null
  }

  return [decimal]::Parse([string]$value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-GammaMarket([string]$asset, [long]$epoch) {
  $slug = "$asset-updown-5m-$epoch"
  $url = "https://gamma-api.polymarket.com/markets?slug=$slug"
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
      $response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 10
      $items = @($response)
      if ($items.Count -lt 1) {
        throw "Gamma returned no market for $slug"
      }

      return $items[0]
    } catch {
      Write-Log "Gamma attempt $attempt failed for ${slug}: $($_.Exception.Message)"
      Start-Sleep -Milliseconds (300 * $attempt)
    }
  }

  throw "Unable to load Gamma market $slug"
}

function Parse-JsonArray($value) {
  if ($null -eq $value) {
    return @()
  }

  if ($value -is [string]) {
    return @($value | ConvertFrom-Json)
  }

  return @($value)
}

function Get-Book([string]$token) {
  $url = 'https://clob.polymarket.com/book?token_id=' + [uri]::EscapeDataString($token)
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
      return Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 10
    } catch {
      Write-Log "Book attempt $attempt failed for token ${token}: $($_.Exception.Message)"
      Start-Sleep -Milliseconds (250 * $attempt)
    }
  }

  throw "Unable to load CLOB book for token $token"
}

function Convert-BookLevels($items, [string]$sortDirection) {
  $levels = @(
    $items | ForEach-Object {
      $price = To-DecimalOrNull $_.price
      $size = To-DecimalOrNull $_.size
      if ($null -ne $price -and $null -ne $size) {
        [pscustomobject]@{
          price = $price
          size = $size
          notional_usd = $price * $size
        }
      }
    }
  )

  if ($sortDirection -eq 'desc') {
    return @($levels | Sort-Object price -Descending)
  }

  return @($levels | Sort-Object price)
}

function Sum-Decimal($items, [string]$propertyName) {
  $sum = [decimal]0
  foreach ($item in $items) {
    $sum += [decimal]$item.$propertyName
  }
  return $sum
}

function Add-CsvRows([string]$path, $rows) {
  $items = @($rows | ForEach-Object { $_ })
  if ($items.Count -eq 0) {
    return
  }

  $csv = $items | ConvertTo-Csv -NoTypeInformation
  if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 0) {
    $csv = $csv | Select-Object -Skip 1
  }
  Add-Content -LiteralPath $path -Value $csv -Encoding UTF8
}

$assets = @('btc', 'eth', 'sol')
$firstStart = Get-NextMarketStartUtc
$schedule = New-Object System.Collections.Generic.List[object]

for ($seq = 1; $seq -le 6; $seq++) {
  $marketStart = $firstStart.AddMinutes(5 * ($seq - 1))
  $marketEnd = $marketStart.AddMinutes(5)
  $due = $marketStart.AddSeconds(-30)
  $epoch = $marketStart.ToUnixTimeSeconds()

  foreach ($asset in $assets) {
    try {
      $market = Get-GammaMarket $asset $epoch
      $tokens = Parse-JsonArray $market.clobTokenIds
      $outcomes = Parse-JsonArray $market.outcomes
      if ($tokens.Count -lt 2) {
        throw "Market $($market.slug) has fewer than 2 token ids"
      }

      for ($i = 0; $i -lt 2; $i++) {
        $outcome = if ($outcomes.Count -gt $i) { [string]$outcomes[$i] } elseif ($i -eq 0) { 'Up' } else { 'Down' }
        $schedule.Add([pscustomobject]@{
          seq = $seq
          asset = $asset.ToUpperInvariant()
          outcome = $outcome
          slug = [string]$market.slug
          token = [string]$tokens[$i]
          market_start_utc = $marketStart.ToString('o')
          market_end_utc = $marketEnd.ToString('o')
          due_utc = $due.ToString('o')
          market_start_local = $marketStart.LocalDateTime.ToString('yyyy-MM-dd HH:mm:ss')
          market_end_local = $marketEnd.LocalDateTime.ToString('yyyy-MM-dd HH:mm:ss')
          due_local = $due.LocalDateTime.ToString('yyyy-MM-dd HH:mm:ss')
          question = [string]$market.question
        })
      }
    } catch {
      Write-Log "Schedule failed for $asset epoch ${epoch}: $($_.Exception.Message)"
    }
  }
}

Add-CsvRows $ScheduleCsvPath $schedule
Write-Log "Schedule rows: $($schedule.Count); first due local: $($firstStart.AddSeconds(-30).LocalDateTime.ToString('yyyy-MM-dd HH:mm:ss'))"

$groups = $schedule | Group-Object seq | Sort-Object { [int]$_.Name }
foreach ($group in $groups) {
  $dueText = ($group.Group | Select-Object -First 1).due_utc
  $dueAt = [DateTimeOffset]::Parse($dueText, [Globalization.CultureInfo]::InvariantCulture)
  while ([DateTimeOffset]::UtcNow -lt $dueAt) {
    $remainingMs = [int][Math]::Max(250, [Math]::Min(10000, ($dueAt - [DateTimeOffset]::UtcNow).TotalMilliseconds))
    Start-Sleep -Milliseconds $remainingMs
  }

  foreach ($row in ($group.Group | Sort-Object asset, outcome)) {
    $capturedAt = [DateTimeOffset]::Now
    try {
      $book = Get-Book $row.token
      $asks = Convert-BookLevels $book.asks 'asc'
      $bids = Convert-BookLevels $book.bids 'desc'

      $askRows = New-Object System.Collections.Generic.List[object]
      $cumSize = [decimal]0
      $cumNotional = [decimal]0
      $level = 0
      foreach ($ask in $asks) {
        $level++
        $cumSize += $ask.size
        $cumNotional += $ask.notional_usd
        $askRows.Add([pscustomobject]@{
          seq = $row.seq; market = "$($row.market_start_local.Substring(11,5))-$($row.market_end_local.Substring(11,5))"; asset = $row.asset; outcome = $row.outcome; book_side = 'Ask';
          level = $level; price = [double]$ask.price; size = [double]$ask.size; notional_usd = [math]::Round([double]$ask.notional_usd, 8);
          cumulative_size = [math]::Round([double]$cumSize, 8); cumulative_notional_usd = [math]::Round([double]$cumNotional, 8);
          captured_at = $capturedAt.ToString('o'); due_local = $row.due_local; slug = $row.slug; token = $row.token
        })
      }

      $bidRows = New-Object System.Collections.Generic.List[object]
      $cumSize = [decimal]0
      $cumNotional = [decimal]0
      $level = 0
      foreach ($bid in $bids) {
        $level++
        $cumSize += $bid.size
        $cumNotional += $bid.notional_usd
        $bidRows.Add([pscustomobject]@{
          seq = $row.seq; market = "$($row.market_start_local.Substring(11,5))-$($row.market_end_local.Substring(11,5))"; asset = $row.asset; outcome = $row.outcome; book_side = 'Bid';
          level = $level; price = [double]$bid.price; size = [double]$bid.size; notional_usd = [math]::Round([double]$bid.notional_usd, 8);
          cumulative_size = [math]::Round([double]$cumSize, 8); cumulative_notional_usd = [math]::Round([double]$cumNotional, 8);
          captured_at = $capturedAt.ToString('o'); due_local = $row.due_local; slug = $row.slug; token = $row.token
        })
      }

      Add-CsvRows $LevelsCsvPath @($askRows + $bidRows)

      $summary = [pscustomobject]@{
        seq = $row.seq
        market = "$($row.market_start_local.Substring(11,5))-$($row.market_end_local.Substring(11,5))"
        asset = $row.asset
        outcome = $row.outcome
        captured_at = $capturedAt.ToString('o')
        due_local = $row.due_local
        slug = $row.slug
        token = $row.token
        best_ask = if ($asks.Count -gt 0) { [double]$asks[0].price } else { $null }
        best_ask_size = if ($asks.Count -gt 0) { [double]$asks[0].size } else { $null }
        best_ask_notional_usd = if ($asks.Count -gt 0) { [math]::Round([double]$asks[0].notional_usd, 8) } else { $null }
        ask_levels = $asks.Count
        total_ask_size = [math]::Round([double](Sum-Decimal $asks 'size'), 8)
        total_ask_notional_usd = [math]::Round([double](Sum-Decimal $asks 'notional_usd'), 8)
        best_bid = if ($bids.Count -gt 0) { [double]$bids[0].price } else { $null }
        best_bid_size = if ($bids.Count -gt 0) { [double]$bids[0].size } else { $null }
        best_bid_notional_usd = if ($bids.Count -gt 0) { [math]::Round([double]$bids[0].notional_usd, 8) } else { $null }
        bid_levels = $bids.Count
        total_bid_size = [math]::Round([double](Sum-Decimal $bids 'size'), 8)
        total_bid_notional_usd = [math]::Round([double](Sum-Decimal $bids 'notional_usd'), 8)
        error = ''
      }
      Add-CsvRows $SummaryCsvPath $summary

      $json = [pscustomobject]@{
        summary = $summary
        asks = @($askRows | Select-Object level, price, size, notional_usd, cumulative_size, cumulative_notional_usd)
        bids = @($bidRows | Select-Object level, price, size, notional_usd, cumulative_size, cumulative_notional_usd)
      }
      Add-Content -LiteralPath $JsonlPath -Value ($json | ConvertTo-Json -Compress -Depth 8) -Encoding UTF8
      Write-Log "Captured seq=$($row.seq) $($row.asset) $($row.outcome) asks=$($asks.Count) bids=$($bids.Count)"
    } catch {
      $summary = [pscustomobject]@{
        seq = $row.seq
        market = "$($row.market_start_local.Substring(11,5))-$($row.market_end_local.Substring(11,5))"
        asset = $row.asset
        outcome = $row.outcome
        captured_at = $capturedAt.ToString('o')
        due_local = $row.due_local
        slug = $row.slug
        token = $row.token
        best_ask = $null; best_ask_size = $null; best_ask_notional_usd = $null; ask_levels = 0; total_ask_size = 0; total_ask_notional_usd = 0
        best_bid = $null; best_bid_size = $null; best_bid_notional_usd = $null; bid_levels = 0; total_bid_size = 0; total_bid_notional_usd = 0
        error = $_.Exception.Message
      }
      Add-CsvRows $SummaryCsvPath $summary
      Add-Content -LiteralPath $JsonlPath -Value ([pscustomobject]@{ summary = $summary; asks = @(); bids = @() } | ConvertTo-Json -Compress -Depth 8) -Encoding UTF8
      Write-Log "Capture failed seq=$($row.seq) $($row.asset) $($row.outcome): $($_.Exception.Message)"
    }

    Start-Sleep -Milliseconds 80
  }
}

Write-Log 'Collector finished.'
