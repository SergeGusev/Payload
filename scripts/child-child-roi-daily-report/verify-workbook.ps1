[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkbookPath,

    [Parameter(Mandatory = $true)]
    [string]$ReportDataPath,

    [Parameter(Mandatory = $true)]
    [string]$LayoutPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$resolvedWorkbook = (Resolve-Path -LiteralPath $WorkbookPath).Path
$report = Get-Content -Raw -LiteralPath $ReportDataPath | ConvertFrom-Json
$layout = Get-Content -Raw -LiteralPath $LayoutPath | ConvertFrom-Json
$redOle = [Drawing.ColorTranslator]::ToOle([Drawing.Color]::FromArgb(192, 0, 0))
$whiteOle = [Drawing.ColorTranslator]::ToOle([Drawing.Color]::White)
$tolerance = 0.000001
$expectedNumberFormat = '$#,##0.00;[Red]-$#,##0.00;$0.00'

$excel = $null
$workbook = $null
$worksheet = $null
$window = $null
$numericCellCount = 0
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AskToUpdateLinks = $false
    $workbook = $excel.Workbooks.Open($resolvedWorkbook, 0, $false)
    if ($workbook.Worksheets.Count -ne 1) {
        throw "Expected one worksheet, found $($workbook.Worksheets.Count)."
    }
    $worksheet = $workbook.Worksheets.Item('Daily PnL')
    $worksheet.Activate() | Out-Null
    $window = $workbook.Windows.Item(1)
    $window.Activate() | Out-Null

    $excel.CalculateFullRebuild()

    if (-not $window.FreezePanes -or $window.SplitRow -ne 1 -or $window.SplitColumn -ne 1) {
        throw "Freeze panes mismatch: FreezePanes=$($window.FreezePanes), SplitRow=$($window.SplitRow), SplitColumn=$($window.SplitColumn)."
    }
    if ($worksheet.UsedRange.Rows.Count -ne $layout.totalRow -or $worksheet.UsedRange.Columns.Count -ne 8) {
        throw "Used range mismatch: $($worksheet.UsedRange.Rows.Count)x$($worksheet.UsedRange.Columns.Count)."
    }

    $expectedHeaders = @('Date (UTC)') + @($report.strategies | ForEach-Object { $_.name }) + @('Daily Total')
    for ($column = 1; $column -le 8; $column++) {
        $actualHeader = [string]$worksheet.Cells.Item(1, $column).Value2
        if ($actualHeader -ne $expectedHeaders[$column - 1]) {
            throw "Header mismatch in column ${column}: '$actualHeader' != '$($expectedHeaders[$column - 1])'."
        }
    }

    $negativeCellCount = 0
    for ($dateIndex = 0; $dateIndex -lt $report.dates.Count; $dateIndex++) {
        $row = 2 + $dateIndex
        $actualDate = [DateTime]::FromOADate([double]$worksheet.Cells.Item($row, 1).Value2).ToString('yyyy-MM-dd')
        if ($actualDate -ne $report.dates[$dateIndex]) {
            throw "Date mismatch in row ${row}: $actualDate != $($report.dates[$dateIndex])."
        }

        $expectedDailyTotal = 0.0
        for ($strategyIndex = 0; $strategyIndex -lt 6; $strategyIndex++) {
            $column = 2 + $strategyIndex
            $expected = [double]$report.strategies[$strategyIndex].dailyPnlUsd[$dateIndex]
            $cell = $worksheet.Cells.Item($row, $column)
            $actual = [double]$cell.Value2
            if ([Math]::Abs($actual - $expected) -gt $tolerance) {
                throw "Daily PnL mismatch at row $row, column ${column}: $actual != $expected."
            }
            $address = $cell.Address($false, $false)
            if (-not [bool]$excel.Evaluate("ISNUMBER('$($worksheet.Name)'!$address)")) {
                throw "Numeric type check failed at $address."
            }
            $numericCellCount++
            if ([string]$cell.NumberFormat -ne $expectedNumberFormat) {
                throw "Number format mismatch at ${address}: '$($cell.NumberFormat)'."
            }
            $expectedDailyTotal += $expected
            if ($actual -lt 0) {
                $negativeCellCount++
                if ([int]$cell.DisplayFormat.Font.Color -ne $redOle -or
                    [int]$cell.DisplayFormat.Interior.Color -ne $whiteOle) {
                    throw "Negative style mismatch at row $row, column $column."
                }
                if ([string]$cell.Text -notmatch '^-\$') {
                    throw "Negative sign display mismatch at ${address}: '$($cell.Text)'."
                }
            }
        }

        $expectedFormula = "=SUM(B${row}:G${row})+0*A${row}"
        $actualFormula = [string]$worksheet.Cells.Item($row, 8).Formula
        if ($actualFormula -ne $expectedFormula) {
            throw "Daily total formula mismatch in H${row}: '$actualFormula'."
        }
        $dailyTotalCell = $worksheet.Cells.Item($row, 8)
        $actualDailyTotal = [double]$dailyTotalCell.Value2
        if ([Math]::Abs($actualDailyTotal - $expectedDailyTotal) -gt $tolerance) {
            throw "Daily total value mismatch in H${row}: $actualDailyTotal != $expectedDailyTotal."
        }
        if (-not [bool]$excel.Evaluate("ISNUMBER('$($worksheet.Name)'!H${row})") -or
            [string]$dailyTotalCell.NumberFormat -ne $expectedNumberFormat) {
            throw "Daily total numeric type/format check failed in H${row}."
        }
        $numericCellCount++
        if ($actualDailyTotal -lt 0) {
            $negativeCellCount++
            if ([int]$dailyTotalCell.DisplayFormat.Font.Color -ne $redOle -or
                [int]$dailyTotalCell.DisplayFormat.Interior.Color -ne $whiteOle) {
                throw "Negative style mismatch in H${row}."
            }
            if ([string]$dailyTotalCell.Text -notmatch '^-\$') {
                throw "Negative sign display mismatch in H${row}: '$($dailyTotalCell.Text)'."
            }
        }
    }

    if ([string]$worksheet.Cells.Item($layout.totalRow, 1).Value2 -ne 'Category Total') {
        throw 'Total row label mismatch.'
    }
    for ($strategyIndex = 0; $strategyIndex -lt 6; $strategyIndex++) {
        $column = 2 + $strategyIndex
        $letter = [char](64 + $column)
        $expectedFormula = "=SUM(${letter}2:${letter}$($layout.lastDataRow))"
        $actualFormula = [string]$worksheet.Cells.Item($layout.totalRow, $column).Formula
        if ($actualFormula -ne $expectedFormula) {
            throw "Strategy total formula mismatch at total row, column ${column}: '$actualFormula'."
        }
        $expected = [double]$report.strategies[$strategyIndex].totalPnlUsd
        $totalCell = $worksheet.Cells.Item($layout.totalRow, $column)
        $actual = [double]$totalCell.Value2
        if ([Math]::Abs($actual - $expected) -gt $tolerance) {
            throw "Strategy total mismatch at total row, column ${column}: $actual != $expected."
        }
        $address = $totalCell.Address($false, $false)
        if (-not [bool]$excel.Evaluate("ISNUMBER('$($worksheet.Name)'!$address)") -or
            [string]$totalCell.NumberFormat -ne $expectedNumberFormat) {
            throw "Strategy total numeric type/format check failed at $address."
        }
        $numericCellCount++
    }

    $expectedGrandFormula = "=SUM(H2:H$($layout.lastDataRow))"
    $actualGrandFormula = [string]$worksheet.Cells.Item($layout.totalRow, 8).Formula
    if ($actualGrandFormula -ne $expectedGrandFormula) {
        throw "Grand total formula mismatch: '$actualGrandFormula'."
    }
    $grandTotalCell = $worksheet.Cells.Item($layout.totalRow, 8)
    $actualGrandTotal = [double]$grandTotalCell.Value2
    $expectedGrandTotal = [double]$report.grandTotalPnlUsd
    if ([Math]::Abs($actualGrandTotal - $expectedGrandTotal) -gt $tolerance) {
        throw "Grand total mismatch: $actualGrandTotal != $expectedGrandTotal."
    }
    if (-not [bool]$excel.Evaluate("ISNUMBER('$($worksheet.Name)'!H$($layout.totalRow))") -or
        [string]$grandTotalCell.NumberFormat -ne $expectedNumberFormat) {
        throw 'Grand total numeric type/format check failed.'
    }
    $numericCellCount++
    $expectedNumericCellCount = ($report.dates.Count * 7) + 7
    if ($numericCellCount -ne $expectedNumericCellCount) {
        throw "Expected $expectedNumericCellCount numeric financial cells, verified $numericCellCount."
    }

    $formulaErrorCount = 0
    try {
        $errorCells = $worksheet.UsedRange.SpecialCells(-4123, 16)
        $formulaErrorCount = $errorCells.Count
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($errorCells)
    }
    catch [Runtime.InteropServices.COMException] {
        $formulaErrorCount = 0
    }
    if ($formulaErrorCount -ne 0) {
        throw "Workbook contains $formulaErrorCount formula error cells."
    }
    if ($negativeCellCount -eq 0) {
        throw 'No negative values were found for conditional-format verification.'
    }

    $workbook.Save()

    $result = [ordered]@{
        workbook = $resolvedWorkbook
        worksheets = $workbook.Worksheets.Count
        sheet = $worksheet.Name
        usedRows = $worksheet.UsedRange.Rows.Count
        usedColumns = $worksheet.UsedRange.Columns.Count
        freezePanes = [bool]$window.FreezePanes
        splitRow = [int]$window.SplitRow
        splitColumn = [int]$window.SplitColumn
        dates = $report.dates.Count
        strategies = $report.strategies.Count
        formulaErrors = $formulaErrorCount
        verifiedNegativeCells = $negativeCellCount
        verifiedNumericCells = $numericCellCount
        grandTotalPnlUsd = $actualGrandTotal
        expectedGrandTotalPnlUsd = $expectedGrandTotal
        status = 'OK'
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    $result | Format-List
}
finally {
    if ($null -ne $workbook) {
        $workbook.Close($true)
    }
    if ($null -ne $excel) {
        $excel.Quit()
    }
    foreach ($comObject in @($window, $worksheet, $workbook, $excel)) {
        if ($null -ne $comObject) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject)
        }
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
