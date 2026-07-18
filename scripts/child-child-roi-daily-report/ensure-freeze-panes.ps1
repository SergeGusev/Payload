[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkbookPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedWorkbook = (Resolve-Path -LiteralPath $WorkbookPath).Path
$directory = Split-Path -Parent $resolvedWorkbook
$temporaryPath = Join-Path $directory (([IO.Path]::GetFileName($resolvedWorkbook)) + '.freeze.tmp')
if (Test-Path -LiteralPath $temporaryPath) {
    Remove-Item -LiteralPath $temporaryPath -Force
}
Copy-Item -LiteralPath $resolvedWorkbook -Destination $temporaryPath

try {
    $archive = [IO.Compression.ZipFile]::Open($temporaryPath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry('xl/worksheets/sheet1.xml')
        if ($null -eq $entry) {
            throw 'xl/worksheets/sheet1.xml was not found.'
        }

        $reader = [IO.StreamReader]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try {
            $xmlText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $document = [Xml.XmlDocument]::new()
        $document.PreserveWhitespace = $true
        $document.LoadXml($xmlText)
        $namespaceUri = $document.DocumentElement.NamespaceURI
        $namespaceManager = [Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespaceManager.AddNamespace('m', $namespaceUri)
        $sheetView = $document.SelectSingleNode('/m:worksheet/m:sheetViews/m:sheetView', $namespaceManager)
        if ($null -eq $sheetView) {
            throw 'The worksheet does not contain sheetViews/sheetView.'
        }

        @($sheetView.SelectNodes('m:pane | m:selection', $namespaceManager)) | ForEach-Object {
            [void]$sheetView.RemoveChild($_)
        }

        $pane = $document.CreateElement('pane', $namespaceUri)
        $pane.SetAttribute('xSplit', '1')
        $pane.SetAttribute('ySplit', '1')
        $pane.SetAttribute('topLeftCell', 'B2')
        $pane.SetAttribute('activePane', 'bottomRight')
        $pane.SetAttribute('state', 'frozen')
        [void]$sheetView.AppendChild($pane)

        foreach ($selectionDefinition in @(
            @{ Pane = 'topRight'; Cell = 'B1' },
            @{ Pane = 'bottomLeft'; Cell = 'A2' },
            @{ Pane = 'bottomRight'; Cell = 'B2' }
        )) {
            $selection = $document.CreateElement('selection', $namespaceUri)
            $selection.SetAttribute('pane', $selectionDefinition.Pane)
            $selection.SetAttribute('activeCell', $selectionDefinition.Cell)
            $selection.SetAttribute('sqref', $selectionDefinition.Cell)
            [void]$sheetView.AppendChild($selection)
        }

        $memory = [IO.MemoryStream]::new()
        $settings = [Xml.XmlWriterSettings]::new()
        $settings.Encoding = [Text.UTF8Encoding]::new($false)
        $settings.Indent = $false
        $settings.OmitXmlDeclaration = $false
        $writer = [Xml.XmlWriter]::Create($memory, $settings)
        try {
            $document.Save($writer)
            $writer.Flush()
            $bytes = $memory.ToArray()
        }
        finally {
            $writer.Dispose()
            $memory.Dispose()
        }

        $entry.Delete()
        $newEntry = $archive.CreateEntry(
            'xl/worksheets/sheet1.xml',
            [IO.Compression.CompressionLevel]::Optimal)
        $stream = $newEntry.Open()
        try {
            $stream.Write($bytes, 0, $bytes.Length)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $verifyArchive = [IO.Compression.ZipFile]::OpenRead($temporaryPath)
    try {
        $verifyEntry = $verifyArchive.GetEntry('xl/worksheets/sheet1.xml')
        $verifyReader = [IO.StreamReader]::new($verifyEntry.Open(), [Text.UTF8Encoding]::new($false))
        try {
            $verifyText = $verifyReader.ReadToEnd()
        }
        finally {
            $verifyReader.Dispose()
        }
    }
    finally {
        $verifyArchive.Dispose()
    }

    if ($verifyText -notmatch '<pane[^>]*xSplit="1"[^>]*ySplit="1"[^>]*topLeftCell="B2"[^>]*state="frozen"') {
        throw 'Frozen pane verification failed after OpenXML update.'
    }

    Move-Item -LiteralPath $temporaryPath -Destination $resolvedWorkbook -Force
    Write-Output 'Frozen pane: xSplit=1; ySplit=1; topLeftCell=B2; state=frozen'
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
