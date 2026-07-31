param(
    [Parameter(Mandatory=$true)][string]$ContentDirectory,
    [string]$Version = "MyCustomClient",
    [Parameter(Mandatory=$true)][string]$MainClass,
    [string]$Output = "manifest.json"
)
$resolved = (Resolve-Path -LiteralPath $ContentDirectory).Path
$outputPath = [IO.Path]::GetFullPath((Join-Path $resolved $Output))
$files = Get-ChildItem -LiteralPath $resolved -File -Recurse | Where-Object { $_.FullName -ne $outputPath } | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($resolved, $_.FullName).Replace('\','/')
    [ordered]@{ path=$relative; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); size=$_.Length }
}
[ordered]@{version=$Version;mainClass=$MainClass;files=@($files)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Host "Manifest generated: $outputPath ($(@($files).Count) files)"

