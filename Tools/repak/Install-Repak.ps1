$ErrorActionPreference = "Stop"
# repak provides the PAK reader/decompressor used by Mesh + Textures.
# Its Oodle loader obtains the compatible native runtime when required.
$version = "0.2.3"
$url = "https://github.com/trumank/repak/releases/download/v$version/repak_cli-x86_64-pc-windows-msvc.zip"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$zip = Join-Path $root "repak.zip"
Invoke-WebRequest -Uri $url -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $root -Force
Remove-Item $zip -Force
$found = Get-ChildItem -Path $root -Filter "repak.exe" -Recurse | Select-Object -First 1
if ($null -eq $found) { throw "repak.exe was not found after extraction." }
Copy-Item $found.FullName (Join-Path $root "repak.exe") -Force
Write-Host "Installed repak.exe to $root"
