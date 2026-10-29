$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../src/Clenzy.Windows/Windows11/Maintenance.ps1"
$script:passed = 0
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Test($name, [scriptblock]$body) {
    $script:packages = @([pscustomobject]@{ Name='Microsoft.BingNews'; PackageFullName='News_1'; InstallLocation='C:\Apps\News'; NonRemovable=$false; IsFramework=$false; IsResourcePackage=$false; Dependencies=@() })
    $script:processes = @(); $script:removed = @(); $script:points = @(); $script:recoveryFails = $false
    & $body
    $script:passed++; Write-Output "PASS $name"
}
function Get-AppxPackage { $script:packages }
function Get-Process { $script:processes }
function Remove-AppxPackage($Package) { $script:removed += $Package; $script:packages = @($script:packages | Where-Object PackageFullName -ne $Package) }
function Checkpoint-Computer($Description, $RestorePointType) { if ($script:recoveryFails) { throw 'Recovery unavailable' }; $script:points += [pscustomobject]@{ Description=$Description } }
function Get-ComputerRestorePoint { $script:points }
function Package { Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Package';Id='Microsoft.BingNews'}) }
function Remove { Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Remove';Id='Microsoft.BingNews';FullName='News_1'}) }
function MustThrow([scriptblock]$body) { $threw=$false; try { & $body | Out-Null } catch { $threw=$true }; Assert $threw 'Expected operation to reject request' }
Test 'Installed app is detected' { $s=Package; Assert ($s.Exists -and -not $s.Blocked -and $s.FullName -eq 'News_1') 'Incorrect package state' }
Test 'Missing app is not offered' { $script:packages=@(); Assert (-not (Package).Exists) 'Missing app reported installed' }
Test 'Unknown package is rejected' { MustThrow { Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Remove';Id='Microsoft.WindowsStore';FullName='Store_1'}) }; Assert ($script:removed.Count -eq 0) 'Protected app was removed' }
Test 'Duplicate registrations are rejected' { $script:packages += $script:packages[0]; MustThrow { Package } }
foreach ($flag in @('NonRemovable','IsFramework','IsResourcePackage')) {
    Test "Preserve $flag" { $script:packages[0].$flag=$true; Assert (Package).Blocked 'Protected package offered'; MustThrow { Remove }; Assert ($script:removed.Count -eq 0) 'Protected package removed' }
}
Test 'Object dependency blocks removal' { $script:packages += [pscustomobject]@{Name='Dependent';PackageFullName='Dependent_1';Dependencies=@([pscustomobject]@{PackageFullName='News_1'})}; Assert (Package).Blocked 'Dependency ignored'; MustThrow { Remove } }
Test 'String dependency blocks removal' { $script:packages += [pscustomobject]@{Name='Dependent';PackageFullName='Dependent_1';Dependencies=@('News_1')}; Assert (Package).Blocked 'Dependency ignored'; MustThrow { Remove } }
Test 'Running app blocks removal' { $script:processes=@([pscustomobject]@{Path='c:\apps\news\News.exe'}); Assert (Package).Blocked 'Running app ignored'; MustThrow { Remove } }
Test 'Similar path does not block unrelated app' { $script:processes=@([pscustomobject]@{Path='C:\Apps\NewsOther\Other.exe'}); Assert (-not (Package).Blocked) 'Path boundary incorrect' }
Test 'Changed identity blocks uninstall' { $script:packages[0].PackageFullName='News_2'; MustThrow { Remove }; Assert ($script:removed.Count -eq 0) 'Stale package removed' }
Test 'Allowed uninstall uses exact package identity' { $r=Remove; Assert ($r.Removed -and $script:removed.Count -eq 1 -and $script:removed[0] -ceq 'News_1') 'Wrong uninstall'; Assert (-not (Package).Exists) 'Package remains' }
Test 'Already absent removal is a no-op' { $script:packages=@(); Assert (-not (Remove).Removed) 'False removal'; Assert ($script:removed.Count -eq 0) 'Uninstall called unnecessarily' }
Test 'New restore point is verified' { Assert (Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Recovery'})).Verified 'Recovery not verified' }
Test 'Failed checkpoint does not accept old restore point' { $script:points=@([pscustomobject]@{Description='Old point'}); $script:recoveryFails=$true; Assert (-not (Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Recovery'})).Verified) 'False recovery success' }
Test 'Unknown operation is rejected' { MustThrow { Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Execute';Command='anything'}) } }
Write-Output "$script:passed production-module contract tests passed. OS commands were replaced with controlled test doubles; no installed apps or recovery settings changed."
