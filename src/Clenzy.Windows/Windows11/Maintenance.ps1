# PowerShell 5.1. Fixed operations only; request data is never evaluated as code.
function Invoke-ClenzyWindows11 {
    param([Parameter(Mandatory=$true)]$Request)
    $ErrorActionPreference = 'Stop'
    $allowed = @($script:ClenzyCatalog | Where-Object Kind -eq 'Package' | ForEach-Object Id)
    switch ($Request.Operation) {
        'Capabilities' { return @{ PowerShell=$PSVersionTable.PSVersion.ToString(); CatalogCount=@($script:ClenzyCatalog).Count } }
        'Packages' {
            $script:PackageSnapshot = @(Get-AppxPackage)
            $script:ProcessSnapshot = @(Get-Process)
            $results = @{}
            try { foreach ($id in $allowed) { try { $results[$id] = Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Package';Id=$id}) } catch { $results[$id] = @{Exists=$false;FullName=$null;Blocked=$true;Unknown=$true;Reason=('Could not inspect: ' + $_.Exception.Message)} } } }
            finally { $script:PackageSnapshot = $null; $script:ProcessSnapshot = $null }
            return $results
        }
        'Audit' {
            $errors = New-Object System.Collections.Generic.List[string]
            function Read-Section([string]$Name, [scriptblock]$Body) {
                try { & $Body } catch { $errors.Add($Name + ': ' + $_.Exception.Message); return $null }
            }
            $os = Get-CimInstance Win32_OperatingSystem
            $packages = @(Get-AppxPackage | Select-Object Name,PackageFullName,InstallLocation,NonRemovable,IsFramework,IsResourcePackage,Dependencies)
            $audit = [ordered]@{
                DomainJoined = [bool](Get-CimInstance Win32_ComputerSystem).PartOfDomain
                OS = $os | Select-Object Caption,Version,BuildNumber,OSArchitecture
                CPU = @(Read-Section 'CPU' { Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores })
                RAMBytes = [long]$os.TotalVisibleMemorySize * 1024
                GPU = @(Read-Section 'GPU' { Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion })
                Disks = @(Read-Section 'Disks' { Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object DeviceID,Size,FreeSpace })
                TopMemoryProcesses = @(Read-Section 'Memory processes' { Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 10 ProcessName,Id,WorkingSet64 })
                PowerShell = $PSVersionTable.PSVersion.ToString()
                Administrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
                Packages = $packages
                Provisioned = @(Read-Section 'Provisioned packages (administrator required)' { Get-AppxProvisionedPackage -Online | Select-Object DisplayName,PackageName })
                InstalledPrograms = @(Read-Section 'Installed programs' { Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object DisplayName | Select-Object DisplayName,DisplayVersion,Publisher })
                Startup = @(Read-Section 'Startup' { Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location,User })
                Services = @(Read-Section 'Services' { Get-CimInstance Win32_Service | Select-Object Name,DisplayName,StartMode,State,PathName })
                Tasks = @(Read-Section 'Tasks' { Get-ScheduledTask | Select-Object TaskName,TaskPath,State })
                Defender = Read-Section 'Defender' { Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated }
                Firewall = @(Read-Section 'Firewall' { Get-NetFirewallProfile | Select-Object Name,Enabled })
                BitLocker = @(Read-Section 'BitLocker (administrator required)' { Get-BitLockerVolume | Select-Object MountPoint,ProtectionStatus,VolumeStatus })
                Update = Read-Section 'Windows Update' { Get-Service wuauserv | Select-Object Name,Status,StartType }
                Errors = $errors
            }
            return $audit
        }
        'Package' {
            if ($allowed -cnotcontains [string]$Request.Id) { throw 'Package is not allowlisted.' }
            $all = if ($null -ne $script:PackageSnapshot) { @($script:PackageSnapshot) } else { @(Get-AppxPackage) }
            $p = @($all | Where-Object { $_.Name -ceq $Request.Id })
            if ($p.Count -gt 1) { throw 'Multiple package registrations require manual review.' }
            if ($p.Count -eq 0) { return @{ Exists=$false; FullName=$null; Blocked=$false; Reason='Not installed' } }
            $p = $p[0]
            $dependents = @($all | Where-Object { @($_.Dependencies | Where-Object { ($_.PackageFullName -eq $p.PackageFullName) -or ([string]$_ -ceq $p.PackageFullName) }).Count -gt 0 })
            $processes = if ($null -ne $script:ProcessSnapshot) { @($script:ProcessSnapshot) } else { @(Get-Process) }
            $running = @($processes | Where-Object { try { $_.Path -and $p.InstallLocation -and $_.Path.StartsWith($p.InstallLocation + '\', [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
            $blocked = $p.NonRemovable -or $p.IsFramework -or $p.IsResourcePackage -or $dependents.Count -gt 0 -or $running.Count -gt 0
            return @{ Exists=$true; FullName=$p.PackageFullName; Blocked=[bool]$blocked; Reason= $(if ($blocked) { 'Required, non-removable, depended on, or currently running; preserved.' } else { 'Installed for this user; usage history is unknown. Review before uninstalling.' }) }
        }
        'Remove' {
            $state = Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Package';Id=$Request.Id})
            if (-not $state.Exists) { return @{ Removed=$false } }
            if ($state.Blocked -or $state.FullName -cne $Request.FullName) { throw 'Package changed, is running, or is protected; rescan.' }
            Remove-AppxPackage -Package $state.FullName -ErrorAction Stop
            return @{ Removed=$true }
        }
        'Service' {
            $entry = @($script:ClenzyCatalog | Where-Object { $_.Kind -ceq 'Service' -and $_.Id -ceq $Request.Id })
            if ($entry.Count -ne 1) { throw 'Service is not allowlisted.' }
            $service = Get-Service -Name $entry[0].Name -ErrorAction SilentlyContinue
            if ($null -eq $service) { return @{Exists=$false; Mode=$null; Available=$false; Reason='Service is not installed.'} }
            $delayed = Get-ItemPropertyValue -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $entry[0].Name) -Name DelayedAutoStart -ErrorAction SilentlyContinue
            $blocked = $service.Status -ne 'Stopped' -or @($service.DependentServices).Count -gt 0 -or $delayed -eq 1
            $mode = $service.StartType.ToString()
            return @{Exists=$true; Mode=$mode; Available=(-not $blocked -and $mode -eq 'Automatic'); Reason=$(if ($blocked) {'Running, depended on or delayed-start service; preserved.'} else {'Only automatic services can be changed to manual.'})}
        }
        'SetService' {
            if (@('Manual','Automatic') -cnotcontains [string]$Request.Mode) { throw 'Unsupported service start mode.' }
            $state = Invoke-ClenzyWindows11 ([pscustomobject]@{Operation='Service';Id=$Request.Id})
            if (-not $state.Exists -or ($Request.Mode -eq 'Manual' -and -not $state.Available)) { throw 'Service changed or cannot be modified; rescan.' }
            $entry = $script:ClenzyCatalog | Where-Object { $_.Kind -ceq 'Service' -and $_.Id -ceq $Request.Id }
            Set-Service -Name $entry.Name -StartupType $Request.Mode -ErrorAction Stop
            return @{Changed=$true}
        }
        'Recovery' {
            $description = 'Clenzy Windows 11 ' + [Guid]::NewGuid().ToString('N')
            try {
                Checkpoint-Computer -Description $description -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
                $verified = @(Get-ComputerRestorePoint -ErrorAction Stop | Where-Object { $_.Description -ceq $description })
                return @{ Verified=($verified.Count -eq 1) }
            } catch { return @{ Verified=$false; Reason=$_.Exception.Message } }
        }
        default { throw 'Unsupported operation.' }
    }
}
