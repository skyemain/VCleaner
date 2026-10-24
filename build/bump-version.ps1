<#
.SYNOPSIS
    Sets the next version in VERSION and rolls the "Unreleased" changelog section.

.EXAMPLE
    pwsh build\bump-version.ps1 patch          # 1.1.0 -> 1.1.1
    pwsh build\bump-version.ps1 1.4.0 -Tag     # explicit version, commit and tag v1.4.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Bump,
    [switch]$Tag
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$versionFile = Join-Path $root 'VERSION'
$changelog = Join-Path $root 'CHANGELOG.md'

$current = (Get-Content $versionFile -Raw).Trim()
$parts = $current.Split('.') | ForEach-Object { [int]$_ }
$next = switch -Regex ($Bump) {
    '^major$' { "$($parts[0] + 1).0.0" }
    '^minor$' { "$($parts[0]).$($parts[1] + 1).0" }
    '^patch$' { "$($parts[0]).$($parts[1]).$($parts[2] + 1)" }
    '^\d+\.\d+\.\d+$' { $Bump }
    default { throw "Not a valid version or bump kind: $Bump" }
}

Set-Content $versionFile $next -Encoding ascii
if (Test-Path $changelog) {
    $text = Get-Content $changelog -Raw
    $heading = "## [Unreleased]`n`n## [$next] - $(Get-Date -Format 'yyyy-MM-dd')"
    Set-Content $changelog ($text -replace '## \[Unreleased\]', $heading) -NoNewline -Encoding utf8
}
Write-Host "$current -> $next"

if ($Tag) {
    Push-Location $root
    try {
        git add VERSION CHANGELOG.md
        git commit -m "Release $next"
        git tag -a "v$next" -m "CleanSlate $next"
        Write-Host "Created tag v$next (push with: git push origin main --tags)"
    }
    finally { Pop-Location }
}
