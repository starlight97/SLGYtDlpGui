<#
.SYNOPSIS  Build a Velopack release of YtDlpGui into <repo>\Releases.
.DESCRIPTION
  Version = <Version> in src/YtDlpGui/YtDlpGui.csproj (single source of truth).
  Publishes self-contained, NON-single-file win-x64, then `vpk pack`.
  -Delta first downloads the latest GitHub full package so vpk pack builds a delta automatically.
  -Upload is opt-in, creates a DRAFT unless -Publish, needs $env:GITHUB_TOKEN.
  Never run -Upload from automation: publishing is the repository owner's decision.
.EXAMPLE  powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -AllowDirty
.EXAMPLE  powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Delta -ReleaseNotes .\notes.md
.EXAMPLE  $env:GITHUB_TOKEN='...'; powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Delta -Upload
#>
[CmdletBinding()]
param([switch]$Delta, [string]$ReleaseNotes = '', [switch]$Upload, [switch]$Publish, [switch]$AllowDirty)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackId = 'SLGYtDlpGui'   # PERMANENT - changing it breaks updates for installed users
$PackTitle = 'YtDlpGui'; $Authors = 'SLGarden'; $MainExe = 'YtDlpGui.exe'; $Rid = 'win-x64'
$RepoUrl = 'https://github.com/starlight97/SLGYtDlpGui'   # keep in sync with AppUpdateService.GithubRepoUrl

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = [IO.Path]::Combine($RepoRoot, 'src', 'YtDlpGui', 'YtDlpGui.csproj')
$WorkDir    = [IO.Path]::Combine($RepoRoot, 'artifacts', 'velopack')   # git-ignored
$BuildDir   = [IO.Path]::Combine($WorkDir, 'build')                    # --artifacts-path: obj/bin away from src\
$PublishDir = [IO.Path]::Combine($WorkDir, 'publish')
$ReleaseDir = [IO.Path]::Combine($RepoRoot, 'Releases')                # git-ignored
$IconPath   = [IO.Path]::Combine($RepoRoot, 'src', 'YtDlpGui', 'Assets', 'app.ico')
$Manifest   = [IO.Path]::Combine($PSScriptRoot, 'dotnet-tools.json')

function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    Write-Host ('> {0} {1}' -f $Exe, ($Arguments -join ' ')) -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('{0} exited with code {1}' -f $Exe, $LASTEXITCODE) }
}
function Invoke-Vpk([string[]]$Arguments) {
    Push-Location $PSScriptRoot    # local tool manifest (build\dotnet-tools.json) resolves from here
    try { Invoke-Checked 'dotnet' (@('tool', 'run', 'vpk') + $Arguments) } finally { Pop-Location }
}

# 1. Version (3-part SemVer2; vpk rejects 4-part)
$Version = (& dotnet msbuild $Project -nologo -getProperty:Version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Bad <Version> '$Version' in $Project" }

# 2. Velopack NuGet == vpk (vpk itself only warns on a mismatch)
$ref = Select-Xml -Path $Project -XPath "//PackageReference[@Include='Velopack']" | Select-Object -First 1
if ($null -eq $ref) { throw 'Velopack PackageReference not found.' }
$nugetVersion = $ref.Node.GetAttribute('Version')
$toolVersion = (Get-Content $Manifest -Raw | ConvertFrom-Json).tools.vpk.version
if ($nugetVersion -ne $toolVersion) { throw "Velopack $nugetVersion != vpk $toolVersion. Bump both together." }

# 3. Hygiene
$dirty = (& git -C $RepoRoot status --porcelain | Out-String).Trim()
if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes. Commit first, or -AllowDirty for a local test build.' }

# 4. Tools + publish (self-contained, folder, NOT single-file, no --framework)
Push-Location $PSScriptRoot; try { Invoke-Checked 'dotnet' @('tool', 'restore') } finally { Pop-Location }
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
Invoke-Checked 'dotnet' @('publish', $Project, '-c', 'Release', '-r', $Rid, '--self-contained', 'true',
    '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false', '--artifacts-path', $BuildDir, '-o', $PublishDir)
foreach ($f in @($MainExe, 'YtDlpGui.dll', 'Velopack.dll')) {
    if (-not (Test-Path ([IO.Path]::Combine($PublishDir, $f)))) { throw "Publish output missing $f" }
}

# 5. Optional: latest GitHub full package for a delta (0 releases -> vpk logs and returns normally)
if ($Delta) {
    $hasToken = [bool]$env:GITHUB_TOKEN
    if ($hasToken) { $env:VPK_TOKEN = $env:GITHUB_TOKEN }   # never on the command line (echo/process list)
    try { Invoke-Vpk @('download', 'github', '--repoUrl', $RepoUrl, '--outputDir', $ReleaseDir) }
    catch { Write-Warning "No previous release downloaded: $($_.Exception.Message). Full package only." }
    finally { if ($hasToken) { Remove-Item Env:VPK_TOKEN -ErrorAction SilentlyContinue } }
}

# 6. Refuse a version that isn't newer than full packages already in Releases\ (earlier local packs,
#    plus the latest GitHub full package when -Delta ran - vpk download writes only that .nupkg,
#    never releases.win.json). vpk pack has the same guard but may prompt; failing here keeps the
#    script non-interactive. Tags already on GitHub are also refused by `vpk upload github` (no --merge).
$core = [version](($Version -split '-')[0])
$pattern = '^' + [regex]::Escape($PackId) + '-(\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?-full\.nupkg$'
foreach ($pkg in @(Get-ChildItem -Path $ReleaseDir -Filter '*-full.nupkg' -File -ErrorAction SilentlyContinue)) {
    if ($pkg.Name -eq ('{0}-{1}-full.nupkg' -f $PackId, $Version)) { throw "$($pkg.Name) already exists in $ReleaseDir. Bump <Version>." }
    if ($pkg.Name -match $pattern -and [version]$Matches[1] -gt $core) { throw "$($pkg.Name) is newer than $Version. Bump <Version> or clear test packages from $ReleaseDir." }
}

# 7. Pack (deltas are generated automatically when a previous full nupkg is in --outputDir)
$packArgs = @('pack', '--packId', $PackId, '--packVersion', $Version, '--packDir', $PublishDir, '--mainExe', $MainExe,
    '--packTitle', $PackTitle, '--packAuthors', $Authors, '--runtime', $Rid, '--outputDir', $ReleaseDir)
if (Test-Path $IconPath) { $packArgs += @('--icon', $IconPath) }
if ($ReleaseNotes) { $packArgs += @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) }
Invoke-Vpk $packArgs

# 8. Optional, manual-only upload (uploads the assets listed in assets.win.json = this build only)
if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw 'Set $env:GITHUB_TOKEN (fine-grained PAT, Contents: read/write on this repo).' }
    if ($dirty) { throw 'Refusing to upload a build from a dirty tree.' }
    $head = (& git -C $RepoRoot rev-parse HEAD | Out-String).Trim()
    if (-not (& git -C $RepoRoot branch -r --contains $head | Out-String).Trim()) { throw "HEAD $head is not pushed; push first." }
    $up = @('upload', 'github', '--repoUrl', $RepoUrl, '--outputDir', $ReleaseDir, '--tag', "v$Version",
        '--releaseName', "YtDlpGui $Version", '--targetCommitish', $head)
    if ($Publish) { $up += '--publish' }
    $env:VPK_TOKEN = $env:GITHUB_TOKEN
    try { Invoke-Vpk $up } finally { Remove-Item Env:VPK_TOKEN -ErrorAction SilentlyContinue }
}
Write-Host "Done: $PackId $Version -> $ReleaseDir" -ForegroundColor Green
Get-ChildItem $ReleaseDir | Sort-Object LastWriteTime -Descending | Select-Object -First 8 Name, Length | Format-Table -AutoSize
