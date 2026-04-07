<#
.SYNOPSIS
  Build (and optionally push) the hqqq-api Docker image with correct Assembly InformationalVersion.

.RULES
  - -Version "x.y.z"     → InformationalVersion = x.y.z; image tag vX.Y.Z unless -ImageTag is set
  - -BumpPatch            → latest v* semver tag in git, patch +1 (no tags → 0.0.1); image tag vX.Y.Z unless -ImageTag set
  - (default)             → HEAD exactly on tag v*: that version, image tag vX.Y.Z; else dev build 0.0.0+<sha> (image :0.0.0-<sha>)

  Docker receives:
    --build-arg VERSION=<semver base before +>
    --build-arg INFORMATIONAL_VERSION=<full string, e.g. 0.0.0+abc1234>

.EXAMPLE
  .\scripts\build-hqqq-api-docker.ps1 -Version 1.0.3
.EXAMPLE
  .\scripts\build-hqqq-api-docker.ps1 -BumpPatch -Push
.EXAMPLE
  .\scripts\build-hqqq-api-docker.ps1
#>
param(
  [string] $Version,
  [switch] $BumpPatch,
  [string] $ImageTag,
  [string] $Registry = "acrhqqqmvp001.azurecr.io",
  [string] $ImageName = "hqqq-api",
  [switch] $Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$DockerContext = Join-Path $RepoRoot "src\hqqq-api"

function Get-SemverBase([string] $informational) {
  if ($informational -match '^([^+]+)') { return $matches[1].Trim() }
  return $informational.Trim()
}

function Bump-Patch([string] $semver) {
  $s = $semver.Trim().TrimStart("v")
  if ($s -match '^(\d+)\.(\d+)\.(\d+)$') {
    $p = [int]$matches[3] + 1
    return "$($matches[1]).$($matches[2]).$p"
  }
  return "0.0.1"
}

function Get-LatestSemverFromTags {
  $raw = @(git -C $RepoRoot tag -l "v*" 2>$null)
  if ($raw.Count -eq 0) { return $null }
  $list = [System.Collections.Generic.List[string]]::new()
  foreach ($t in $raw) {
    $t = $t.ToString().Trim()
    if (-not $t) { continue }
    $v = $t.TrimStart("v")
    if ($v -match '^\d+\.\d+\.\d+$') { [void]$list.Add($v) }
  }
  if ($list.Count -eq 0) { return $null }
  return ($list | Sort-Object { [version]$_ } -Descending | Select-Object -First 1)
}

function Resolve-InformationalVersion {
  if ($Version) {
    return $Version.Trim().TrimStart("v")
  }
  if ($BumpPatch) {
    $latest = Get-LatestSemverFromTags
    if ($latest) { return Bump-Patch $latest }
    return "0.0.1"
  }
  $exact = git -C $RepoRoot describe --tags --exact-match 2>$null
  if ($? -and $exact) {
    return $exact.Trim().TrimStart("v")
  }
  $sha = git -C $RepoRoot rev-parse --short HEAD
  return "0.0.0+$sha"
}

$informational = Resolve-InformationalVersion
$msbuildVersion = Get-SemverBase $informational

if (-not $ImageTag) {
  if ($informational -match '^(\d+\.\d+\.\d+)$') {
    $ImageTag = "v$($matches[1])"
  } else {
    $ImageTag = $informational -replace '[\+#]', '-' -replace '[^\w\.\-]', '-'
  }
}

$fullImage = "${Registry}/${ImageName}:${ImageTag}"

Write-Host "INFORMATIONAL_VERSION=$informational"
Write-Host "VERSION (MSBuild)   =$msbuildVersion"
Write-Host "Image               =$fullImage"

$buildArgs = @(
  "build",
  "-f", (Join-Path $DockerContext "Dockerfile"),
  "--build-arg", "VERSION=$msbuildVersion",
  "--build-arg", "INFORMATIONAL_VERSION=$informational",
  "-t", $fullImage,
  $DockerContext
)

& docker @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Push) {
  & docker push $fullImage
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Done."
