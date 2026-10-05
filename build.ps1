param(
    [string]$PlayniteDir = "",
    [switch]$NoPack
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $PlayniteDir) {
    $candidates = @(
        "$env:APPDATA\Playnite",
        "$env:LOCALAPPDATA\Playnite",
        "C:\Program Files\Playnite",
        "C:\Program Files (x86)\Playnite",
        "D:\Progamas\Biblioteca\Playnite"
    )
    $PlayniteDir = $candidates |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Playnite.SDK.dll') } |
        Select-Object -First 1
}

if (-not $PlayniteDir) {
    $lnk = Get-ChildItem `
        "$env:APPDATA\Microsoft\Windows\Start Menu\Programs", "C:\ProgramData\Microsoft\Windows\Start Menu\Programs", "$env:USERPROFILE\Desktop", "C:\Users\Public\Desktop" `
        -Filter '*.lnk' -Recurse -Depth 2 -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*playnite*' } |
        Select-Object -First 1
    if ($lnk) {
        $shell = New-Object -ComObject WScript.Shell
        $target = $shell.CreateShortcut($lnk.FullName).TargetPath
        if ($target) { $PlayniteDir = Split-Path $target -Parent }
    }
}

if (-not $PlayniteDir -or -not (Test-Path -LiteralPath (Join-Path $PlayniteDir 'Playnite.SDK.dll'))) {
    throw "Pasta do Playnite com Playnite.SDK.dll nao encontrada. Passe -PlayniteDir."
}

$toolbox = Join-Path $PlayniteDir 'Toolbox.exe'
if (-not (Test-Path -LiteralPath $toolbox)) {
    throw "Toolbox.exe nao encontrado em: $toolbox"
}

Write-Host "Playnite dir: $PlayniteDir"

dotnet build (Join-Path $scriptDir 'src\AniListWatchTime.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($NoPack) { return }

$manifest = Get-Content (Join-Path $scriptDir 'extension.yaml') -Raw
$id = (($manifest -split "`n" | Where-Object { $_ -match '^Id:' } | Select-Object -First 1) -split ':', 2)[1].Trim()
$name = (($manifest -split "`n" | Where-Object { $_ -match '^Name:' } | Select-Object -First 1) -split ':', 2)[1].Trim()
$version = (($manifest -split "`n" | Where-Object { $_ -match '^Version:' } | Select-Object -First 1) -split ':', 2)[1].Trim()

$dist = Join-Path $scriptDir 'dist'
$staging = Join-Path $dist "staging\$id"

New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $dist -Force | Out-Null

Copy-Item (Join-Path $scriptDir 'src\bin\Release\net462\AniListWatchTime.dll') (Join-Path $staging 'AniListWatchTime.dll') -Force
Copy-Item (Join-Path $scriptDir 'extension.yaml') (Join-Path $staging 'extension.yaml') -Force
Copy-Item (Join-Path $scriptDir 'icon.png') (Join-Path $staging 'icon.png') -Force
New-Item -ItemType Directory -Path (Join-Path $staging 'Localization') -Force | Out-Null
Copy-Item (Join-Path $scriptDir 'src\Localization\*.xaml') (Join-Path $staging 'Localization') -Force

& $toolbox pack $staging $dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Remove-Item -LiteralPath (Join-Path $dist 'staging') -Recurse -Force

$pext = Get-ChildItem -Path $dist -Filter "$id*.pext" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

# O Toolbox.exe nao aceita nome de saida: ele sempre gera "<Id>_<major>_<minor>.pext",
# com o GUID no meio do arquivo. Aqui o pacote e renomeado para "<Nome>_<versao>.pext",
# com espaco virando "_" e ponto virando "-", que e o nome que vai para a release.
$finalName = ($name -replace '\s+', '_') + '_' + ($version -replace '\.', '-') + '.pext'
$finalPath = Join-Path $dist $finalName
if ($pext -and $pext.FullName -ne $finalPath)
{
    Move-Item -LiteralPath $pext.FullName -Destination $finalPath -Force
}

Write-Host ""
Write-Host "Pacote gerado: $finalPath"
Write-Host "Para instalar: arraste o .pext para dentro de uma janela do Playnite aberta (modo Desktop) ou de duplo clique no arquivo."
