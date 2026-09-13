$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$hubRoot = Split-Path $PSScriptRoot -Parent
$hubAssetDir = Join-Path $hubRoot 'HubTool/Assets'
New-Item -ItemType Directory -Path $hubAssetDir -Force | Out-Null
$hubFrames = @()
foreach ($hubSize in @(16,32,48,64,128,256)) {
    $hubBitmap = [System.Drawing.Bitmap]::new($hubSize,$hubSize)
    $hubGraphics = [System.Drawing.Graphics]::FromImage($hubBitmap)
    $hubGraphics.SmoothingMode = 'AntiAlias'
    $hubGraphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#0B121E'))
    $hubBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#68E3C6'))
    $hubGraphics.FillRectangle($hubBrush,[single]($hubSize*.24),[single]($hubSize*.22),[single]($hubSize*.16),[single]($hubSize*.56))
    $hubGraphics.FillRectangle($hubBrush,[single]($hubSize*.60),[single]($hubSize*.22),[single]($hubSize*.16),[single]($hubSize*.56))
    $hubGraphics.FillRectangle($hubBrush,[single]($hubSize*.40),[single]($hubSize*.42),[single]($hubSize*.20),[single]($hubSize*.16))
    $hubStream = [System.IO.MemoryStream]::new()
    $hubBitmap.Save($hubStream,[System.Drawing.Imaging.ImageFormat]::Png)
    $hubFrames += ,@{ Size=$hubSize; Bytes=$hubStream.ToArray() }
    $hubStream.Dispose(); $hubBrush.Dispose(); $hubGraphics.Dispose(); $hubBitmap.Dispose()
}
$hubFile = [System.IO.File]::Create((Join-Path $hubAssetDir 'HubTool.ico'))
$hubWriter = [System.IO.BinaryWriter]::new($hubFile)
$hubWriter.Write([uint16]0); $hubWriter.Write([uint16]1); $hubWriter.Write([uint16]$hubFrames.Count)
$hubOffset = 6 + 16*$hubFrames.Count
foreach ($hubFrame in $hubFrames) {
    $hubDimension = if ($hubFrame.Size -eq 256) { 0 } else { $hubFrame.Size }
    $hubWriter.Write([byte]$hubDimension); $hubWriter.Write([byte]$hubDimension)
    $hubWriter.Write([byte]0); $hubWriter.Write([byte]0)
    $hubWriter.Write([uint16]1); $hubWriter.Write([uint16]32)
    $hubWriter.Write([uint32]$hubFrame.Bytes.Length); $hubWriter.Write([uint32]$hubOffset)
    $hubOffset += $hubFrame.Bytes.Length
}
foreach ($hubFrame in $hubFrames) { $hubWriter.Write([byte[]]$hubFrame.Bytes) }
$hubWriter.Dispose(); $hubFile.Dispose()
