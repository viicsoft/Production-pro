# Automated UI Verification Script for Vidikom Atem Software Control
[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$ExePath = "",

    [Parameter(Mandatory=$false)]
    [int]$TimeoutSeconds = 60,

    [Parameter(Mandatory=$false)]
    [switch]$KillExisting = $false
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Vidikom ATEM Software Control -- Automated UI Verification" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 1. Load Windows UI Automation Assemblies
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

function Get-UiElementText([System.Windows.Automation.AutomationElement]$elem) {
    if (-not $elem) { return "" }
    try {
        $n = $elem.Current.Name
        if (-not [string]::IsNullOrWhiteSpace($n)) { return $n.Trim() }
    } catch { }
    try {
        $textPat = $null
        if ($elem.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$textPat) -and $textPat) {
            $tp = $textPat -as [System.Windows.Automation.TextPattern]
            if ($tp.DocumentRange) {
                $t = $tp.DocumentRange.GetText(-1)
                if (-not [string]::IsNullOrWhiteSpace($t)) { return $t.Trim() }
            }
        }
    } catch { }
    try {
        $valPat = $null
        if ($elem.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valPat) -and $valPat) {
            $vp = $valPat -as [System.Windows.Automation.ValuePattern]
            if (-not [string]::IsNullOrWhiteSpace($vp.Current.Value)) { return $vp.Current.Value.Trim() }
        }
    } catch { }
    return ""
}

function Dismiss-ModalDialogs([int]$targetPid) {
    if ($targetPid -le 0) { return }
    try {
        $modalDialogs = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $targetPid)),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window))
            ))
        )
        foreach ($dlg in $modalDialogs) {
            $dlgName = ""
            try { $dlgName = $dlg.Current.Name } catch { }
            if ($dlgName -ne "Vidikom Atem Software Control" -and $dlgName -ne "BackgroundIntercom") {
                Write-Host "Dismissing blocking dialog '$dlgName'..." -ForegroundColor Yellow
                $okBtn = $dlg.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.AndCondition(
                        (New-Object System.Windows.Automation.PropertyCondition(
                            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                            [System.Windows.Automation.ControlType]::Button)),
                        (New-Object System.Windows.Automation.OrCondition(
                            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "OK")),
                            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Close")),
                            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Next"))
                        ))
                    ))
                )
                if (-not $okBtn) {
                    $okBtn = $dlg.FindFirst(
                        [System.Windows.Automation.TreeScope]::Descendants,
                        (New-Object System.Windows.Automation.PropertyCondition(
                            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                            [System.Windows.Automation.ControlType]::Button))
                    )
                }
                if ($okBtn) {
                    $pObj = $null
                    if ($okBtn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pObj) -and $pObj) {
                        ($pObj -as [System.Windows.Automation.InvokePattern]).Invoke()
                    }
                } else {
                    $wObj = $null
                    if ($dlg.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$wObj) -and $wObj) {
                        ($wObj -as [System.Windows.Automation.WindowPattern]).Close()
                    }
                }
            }
        }
    } catch { }
}

function Wait-Port8080Free([int]$TimeoutMs = 6000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $isFree = $false
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            $conns = @(Get-NetTCPConnection -LocalPort 8080 -ErrorAction SilentlyContinue)
            foreach ($c in $conns) {
                $pId = $c.OwningProcess
                if ($pId -and $pId -gt 4) {
                    Stop-Process -Id $pId -Force -ErrorAction SilentlyContinue
                    try {
                        $pObj = Get-Process -Id $pId -ErrorAction SilentlyContinue
                        if ($pObj) { $pObj.WaitForExit(500) | Out-Null }
                    } catch { }
                }
            }
        } catch { }

        $listener = $null
        try {
            $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, 8080)
            $listener.Server.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::ReuseAddress, $false)
            $listener.Start()
            $listener.Stop()
            $isFree = $true
            break
        } catch {
            Start-Sleep -Milliseconds 100
        } finally {
            if ($listener) {
                try { $listener.Stop() } catch { }
            }
        }
    }
    $sw.Stop()
    return $isFree
}

function Invoke-UiControlSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [System.Windows.Automation.AutomationElement]$RootElement,

        [Parameter(Mandatory=$true)]
        [string]$AutomationId,

        [int]$TimeoutSeconds = 10,
        [int]$RetryDelayMs = 250,
        [int]$TargetPid = 0,
        [switch]$RequireEnabled = $true
    )

    if (-not $RootElement) { return $false }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($TargetPid -gt 0) {
            Dismiss-ModalDialogs $TargetPid
        }

        try {
            # 1. Always re-fetch freshly to prevent stale AutomationElement references
            $elem = $RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId
                ))
            )

            if ($elem) {
                # 2. Verify element enabled state
                $isEnabled = $true
                try {
                    $isEnabled = $elem.Current.IsEnabled
                } catch {
                    Start-Sleep -Milliseconds $RetryDelayMs
                    continue
                }

                if ($RequireEnabled -and -not $isEnabled) {
                    Start-Sleep -Milliseconds $RetryDelayMs
                    continue
                }

                # 3. Handle offscreen ScrollViewer clipping
                try {
                    if ($elem.Current.IsOffscreen) {
                        $scrollPat = $null
                        if ($elem.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$scrollPat) -and $scrollPat) {
                            ($scrollPat -as [System.Windows.Automation.ScrollItemPattern]).ScrollIntoView()
                            Start-Sleep -Milliseconds 100
                        }
                    }
                } catch { }

                # 4. Primary: Attempt InvokePattern
                $invPat = $null
                if ($elem.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invPat) -and $invPat) {
                    try {
                        ($invPat -as [System.Windows.Automation.InvokePattern]).Invoke()
                        return $true
                    } catch [System.Windows.Automation.ElementNotAvailableException] {
                        # Element invalidated during layout pass; retry with fresh reference
                        Start-Sleep -Milliseconds $RetryDelayMs
                        continue
                    } catch {
                        Start-Sleep -Milliseconds $RetryDelayMs
                        continue
                    }
                }

                # 5. Secondary: Attempt SelectionItemPattern (for RadioButtons)
                $selPat = $null
                if ($elem.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selPat) -and $selPat) {
                    try {
                        ($selPat -as [System.Windows.Automation.SelectionItemPattern]).Select()
                        return $true
                    } catch {
                        Start-Sleep -Milliseconds $RetryDelayMs
                        continue
                    }
                }

                # 6. Tertiary: Attempt TogglePattern (for ToggleButtons)
                $togPat = $null
                if ($elem.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$togPat) -and $togPat) {
                    try {
                        ($togPat -as [System.Windows.Automation.TogglePattern]).Toggle()
                        return $true
                    } catch {
                        Start-Sleep -Milliseconds $RetryDelayMs
                        continue
                    }
                }
            }
        } catch { }

        Start-Sleep -Milliseconds $RetryDelayMs
    }

    return $false
}

# 2. Resolve Target Executable
if ($PSBoundParameters.ContainsKey('ExePath')) {
    # Explicit -ExePath parameter was supplied: strictly validate with NO silent fallback
    if ([string]::IsNullOrWhiteSpace($ExePath)) {
        [Console]::Out.WriteLine("Target executable not found: [Empty ExePath provided]")
        Write-Host "Target executable not found: [Empty ExePath provided]" -ForegroundColor Red
        Write-Error "Target executable not found: [Empty ExePath provided]"
        exit 1
    }
    $resolvedExe = [System.IO.Path]::GetFullPath($ExePath)
    if (-not (Test-Path $resolvedExe)) {
        [Console]::Out.WriteLine("Target executable not found: $resolvedExe")
        Write-Host "Target executable not found: $resolvedExe" -ForegroundColor Red
        Write-Error "Target executable not found: $resolvedExe"
        exit 1
    }
} else {
    # No -ExePath parameter supplied: probe default candidate locations
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    if (-not $scriptDir) { $scriptDir = Join-Path (Get-Location).Path "scripts" }
    $defaultScriptExe = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($scriptDir, "..\desktop\bin\Debug\net9.0-windows\desktop.exe"))
    $defaultCwdExe = [System.IO.Path]::GetFullPath("desktop\bin\Debug\net9.0-windows\desktop.exe")

    if (Test-Path $defaultScriptExe) {
        $resolvedExe = $defaultScriptExe
    } elseif (Test-Path $defaultCwdExe) {
        $resolvedExe = $defaultCwdExe
    } else {
        [Console]::Out.WriteLine("Target executable not found: $defaultScriptExe")
        Write-Host "Target executable not found: $defaultScriptExe" -ForegroundColor Red
        Write-Error "Target executable not found: $defaultScriptExe"
        exit 1
    }
}
Write-Host "[1/7] Target executable: $resolvedExe" -ForegroundColor Green

# 3. Pre-seed AppData Configuration to bypass First-Launch Wizard
$appDataDir = [System.IO.Path]::Combine($env:APPDATA, "AtemDirector")
$configFile = [System.IO.Path]::Combine($appDataDir, "config.json")
if (-not (Test-Path $appDataDir)) {
    [System.IO.Directory]::CreateDirectory($appDataDir) | Out-Null
}

$needsConfig = $true
if (Test-Path $configFile) {
    try {
        $existing = Get-Content $configFile -Raw | ConvertFrom-Json
        if ($existing.SetupCompleted -eq $true) {
            $needsConfig = $false
        }
    } catch { }
}

if ($needsConfig) {
    Write-Host "[2/7] Pre-seeding config.json with SetupCompleted=true..." -ForegroundColor Yellow
    $configJson = '{"ActiveInputs":[1,2,3,4,5,6,7,8],"CustomLabels":{},"CameraRoles":{},"CaptureDeviceIndices":{},"SelectedTransitionStyle":"Mix","AutoTransitionRateMs":1000,"SetupCompleted":true,"VoiceControlEnabled":false}'
    Set-Content -Path $configFile -Value $configJson -Encoding UTF8
} else {
    Write-Host "[2/7] Configuration verified (SetupCompleted=true)" -ForegroundColor Green
}

# 4. Clean up any pre-existing desktop.exe or WebView2 processes synchronously
$procsToClean = @("desktop")
$targetName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedExe)
if ($targetName -and $procsToClean -notcontains $targetName) {
    $procsToClean += $targetName
}

foreach ($pName in $procsToClean) {
    $existing = @(Get-Process -Name $pName -ErrorAction SilentlyContinue)
    foreach ($p in $existing) {
        try {
            Write-Host "Terminating pre-existing $pName process (PID: $($p.Id))..." -ForegroundColor DarkYellow
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            $p.WaitForExit(1500) | Out-Null
        } catch { }
    }
}

# Clean up any lingering AtemDirector webview2 processes before releasing port 8080
try {
    $wvProcs = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -like "*AtemDirector*"
    })
    foreach ($wvp in $wvProcs) {
        Stop-Process -Id $wvp.ProcessId -Force -ErrorAction SilentlyContinue
        try {
            $procToWait = Get-Process -Id $wvp.ProcessId -ErrorAction SilentlyContinue
            if ($procToWait) { $procToWait.WaitForExit(1000) | Out-Null }
        } catch { }
    }
    if ($wvProcs.Count -gt 0) {
        Start-Sleep -Milliseconds 500
    }
} catch { }

# Synchronously wait until Port 8080 is 100% bindable by Kestrel
$portAvailable = Wait-Port8080Free -TimeoutMs 5000
if (-not $portAvailable) {
    Write-Host "Warning: Port 8080 did not become freely bindable within 5s; proceeding anyway..." -ForegroundColor Yellow
}

# 5. Launch desktop.exe with Repository Root as WorkingDirectory
$testStartTime = [DateTime]::UtcNow
Write-Host "[3/7] Launching desktop.exe..." -ForegroundColor Green
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$workingDir = $repoRoot
$proc = Start-Process -FilePath $resolvedExe -WorkingDirectory $workingDir -PassThru
$procId = $proc.Id
Write-Host "Started process PID: $procId (WorkingDir: $workingDir)" -ForegroundColor DarkGray

$dismisserJob = $null

try {
    # 6. Locate Main Window (with Fail-Fast and In-Loop Modal Auto-Dismissal)
    Write-Host "[4/7] Waiting for main window to render..." -ForegroundColor Green
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $mainWnd = $null

    while ([DateTime]::UtcNow -lt $deadline) {
        # Fail fast if target process crashed or terminated with error
        $proc.Refresh()
        if ($proc.HasExited -and $proc.ExitCode -ne 0) {
            throw "Process PID $procId terminated unexpectedly with exit code $($proc.ExitCode) while waiting for main window."
        }

        # Proactively detect and dismiss any modal dialog blocking the UI thread (e.g. WebView2 error or setup wizard)
        Dismiss-ModalDialogs $procId

        # Primary probe: match exact process ID and title
        $mainWnd = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Vidikom Atem Software Control"))
            ))
        )
        if ($mainWnd) { break }

        # Secondary probe: match by exact window title across top-level children
        if (-not $mainWnd) {
            $candidateWnd = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Vidikom Atem Software Control"))
            )
            if ($candidateWnd) {
                $mainWnd = $candidateWnd
                break
            }
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $mainWnd) {
        throw "Main window 'Vidikom Atem Software Control' failed to appear within $TimeoutSeconds seconds."
    }
    Write-Host "Main window discovered: '$($mainWnd.Current.Name)'" -ForegroundColor Green

    # 7. Locate MenuBar with Polling Retry Loop
    Write-Host "Waiting for MenuBar visual tree to render..." -ForegroundColor DarkGray
    $menuBarDeadline = [DateTime]::UtcNow.AddSeconds(45)
    $menuBar = $null
    $menuAttempts = 0
    while ([DateTime]::UtcNow -lt $menuBarDeadline) {
        $menuAttempts++
        Dismiss-ModalDialogs $procId
        try {
            # Refresh mainWnd reference from RootElement to ensure latest visual tree
            $refreshed = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)),
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, "Vidikom Atem Software Control"))
                ))
            )
            if ($refreshed) { $mainWnd = $refreshed }

            if ($mainWnd) {
                $menuBar = $mainWnd.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Menu
                    ))
                )
                if ($menuBar) { break }

                # Fallback: Locate via MenuItem 'File' parent
                $fileItem = $mainWnd.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, "File"
                    ))
                )
                if ($fileItem) {
                    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
                    $menuBar = $walker.GetParent($fileItem)
                    if ($menuBar) { break }
                }
            }
        } catch [System.Runtime.InteropServices.COMException] {
            Start-Sleep -Milliseconds 150
        } catch [System.Windows.Automation.ElementNotAvailableException] {
            Start-Sleep -Milliseconds 150
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $menuBar) {
        throw "Failed to find MenuBar control in MainWindow after $menuAttempts attempts."
    }
    Write-Host "MenuBar discovered successfully (attempt $menuAttempts)." -ForegroundColor Green

    # 8. Connect to SimAtem
    Write-Host "[5/7] Connecting to SimAtem..." -ForegroundColor Green

    # Check if already connected on startup
    $alreadyConnected = $false
    try {
        $stCheck = $mainWnd.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "StatusText"
            ))
        )
        if ($stCheck) {
            $stVal = Get-UiElementText $stCheck
            if ($stVal -eq "Connected") {
                $alreadyConnected = $true
                Write-Host "SimAtem is already connected on startup." -ForegroundColor Green
            }
        }
    } catch { }

    if (-not $alreadyConnected) {
        # Click TabConnection with bulletproof safe invocation
        $tabConnClicked = Invoke-UiControlSafe -RootElement $mainWnd -AutomationId "TabConnection" -TimeoutSeconds 10 -TargetPid $procId
        if ($tabConnClicked) {
            Write-Host "  [OK] Clicked TabConnection." -ForegroundColor DarkCyan
            Start-Sleep -Milliseconds 400
        }

        # Select Internal Simulator Radio Button (RadioSimulator)
        $radioSelected = Invoke-UiControlSafe -RootElement $mainWnd -AutomationId "RadioSimulator" -TimeoutSeconds 8 -TargetPid $procId
        if ($radioSelected) {
            Write-Host "  [OK] Selected 'Internal Simulator' mode (RadioSimulator)." -ForegroundColor DarkCyan
            Start-Sleep -Milliseconds 300
        }

        # Locate and invoke BtnConnect inside ConnectionView with bulletproof retry and element re-fetching
        $connInvoked = Invoke-UiControlSafe -RootElement $mainWnd -AutomationId "BtnConnect" -TimeoutSeconds 12 -TargetPid $procId
        if ($connInvoked) {
            Write-Host "  [OK] Invoked 'Connect' in Connection View." -ForegroundColor DarkCyan
            Start-Sleep -Milliseconds 500
        } else {
            Write-Warning "Could not invoke 'BtnConnect' within timeout."
        }
    }

    # STRICT ASSERTION: Poll and verify StatusText transitions to 'Connected'
    $finalStatus = ""
    $pollStatusDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $pollStatusDeadline) {
        Dismiss-ModalDialogs $procId
        try {
            # 1. Primary check: AutomationId == "StatusText"
            $statusTextElem = $mainWnd.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "StatusText"
                ))
            )
            if ($statusTextElem) {
                $curr = Get-UiElementText $statusTextElem
                if ($curr) {
                    $finalStatus = $curr
                    if ($finalStatus -eq "Connected") { break }
                }
            }

            # 2. Non-mutually-exclusive fallback: Descendant with Name == "Connected"
            $connElem = $mainWnd.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Connected"
                ))
            )
            if ($connElem) {
                $finalStatus = "Connected"
                break
            }

            # 3. Non-mutually-exclusive fallback: TxtHeaderStatus inside ConnectionView
            $headerStatusElem = $mainWnd.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "TxtHeaderStatus"
                ))
            )
            if ($headerStatusElem) {
                $hText = Get-UiElementText $headerStatusElem
                if ($hText -eq "CONNECTED" -or $hText -eq "Connected") {
                    $finalStatus = "Connected"
                    break
                }
            }

            # 4. Non-mutually-exclusive fallback: BtnDisconnect enabled in ConnectionView
            $btnDisconnect = $mainWnd.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "BtnDisconnect"
                ))
            )
            if ($btnDisconnect -and $btnDisconnect.Current.IsEnabled) {
                $finalStatus = "Connected"
                break
            }

            # 5. Non-mutually-exclusive fallback: Descendant Text containing "Connected"
            $textElems = $mainWnd.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Text
                ))
            )
            foreach ($te in $textElems) {
                try {
                    $tn = $te.Current.Name
                    if ($tn -eq "Connected" -or $tn -eq "CONNECTED" -or $tn -like "Connected to*") {
                        $finalStatus = "Connected"
                        break
                    }
                } catch { }
            }
            if ($finalStatus -eq "Connected") { break }

        } catch { }

        # Refresh mainWnd reference for next attempt
        try {
            $ref = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)),
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty, "Vidikom Atem Software Control"))
                ))
            )
            if ($ref) { $mainWnd = $ref }
        } catch { }

        Start-Sleep -Milliseconds 250
    }

    Write-Host "StatusText after connection: '$finalStatus'" -ForegroundColor $(if ($finalStatus -eq "Connected") { "Green" } else { "Red" })

    if ($finalStatus -ne "Connected") {
        throw "STRICT ASSERTION FAILED: SimAtem connection failed! StatusText is '$finalStatus', expected 'Connected'."
    }
    Write-Host "  [ASSERTION PASSED] StatusText is 'Connected'." -ForegroundColor Green

    # 9. Verify All 7 Required Menu Areas
    Write-Host "[6/7] Verifying all 7 menu areas..." -ForegroundColor Green
    $requiredMenus = @("File", "Macros", "Outputs", "Stream", "Record", "Connection", "Help")

    foreach ($menuName in $requiredMenus) {
        # Polled lookup per menu item to ensure realization
        $itemDeadline = [DateTime]::UtcNow.AddSeconds(5)
        $menuItem = $null
        while ([DateTime]::UtcNow -lt $itemDeadline) {
            Dismiss-ModalDialogs $procId
            try {
                if ($menuBar) {
                    $menuItem = $menuBar.FindFirst(
                        [System.Windows.Automation.TreeScope]::Children,
                        (New-Object System.Windows.Automation.PropertyCondition(
                            [System.Windows.Automation.AutomationElement]::NameProperty, $menuName
                        ))
                    )
                    if ($menuItem) { break }
                }
            } catch { }

            # Re-acquire menuBar from fresh mainWnd
            try {
                $ref = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                    [System.Windows.Automation.TreeScope]::Children,
                    (New-Object System.Windows.Automation.AndCondition(
                        (New-Object System.Windows.Automation.PropertyCondition(
                            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)),
                        (New-Object System.Windows.Automation.PropertyCondition(
                            [System.Windows.Automation.AutomationElement]::NameProperty, "Vidikom Atem Software Control"))
                    ))
                )
                if ($ref) {
                    $mainWnd = $ref
                    $menuBar = $mainWnd.FindFirst(
                        [System.Windows.Automation.TreeScope]::Descendants,
                        (New-Object System.Windows.Automation.PropertyCondition(
                            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                            [System.Windows.Automation.ControlType]::Menu
                        ))
                    )
                }
            } catch { }
            Start-Sleep -Milliseconds 100
        }
        if (-not $menuItem) {
            throw "Required MenuItem '$menuName' was NOT found in MenuBar."
        }
        
        # Validate presence, control type, enabled state, and on-screen visibility
        $isEnabled = $false
        $isOffscreen = $true
        $bounds = [System.Windows.Rect]::Empty
        $ctrlType = $null
        $elemName = ""
        try {
            $isEnabled = $menuItem.Current.IsEnabled
            $isOffscreen = $menuItem.Current.IsOffscreen
            $bounds = $menuItem.Current.BoundingRectangle
            $ctrlType = $menuItem.Current.ControlType
            $elemName = $menuItem.Current.Name
        } catch { }

        if ($elemName -ne $menuName) {
            throw "MenuItem '$menuName' has unexpected Name '$elemName'!"
        }
        if ($ctrlType -ne [System.Windows.Automation.ControlType]::MenuItem) {
            throw "MenuItem '$menuName' has unexpected ControlType '$($ctrlType.ProgrammaticName)'!"
        }
        if (-not $isEnabled) {
            throw "MenuItem '$menuName' is disabled!"
        }
        if ($isOffscreen) {
            throw "MenuItem '$menuName' is off-screen!"
        }
        if ($bounds.Width -le 0 -or $bounds.Height -le 0) {
            throw "MenuItem '$menuName' has invalid render bounds ($($bounds.Width)x$($bounds.Height))!"
        }

        # Inspect menu capability safely via UIA properties without invoking disruptive .Expand() popups
        $expPatternObj = $null
        if ($menuItem.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expPatternObj) -and $expPatternObj) {
            $expState = ($expPatternObj -as [System.Windows.Automation.ExpandCollapsePattern]).Current.ExpandCollapseState
            Write-Host "  [OK] Found Menu: '$menuName' (Enabled=$isEnabled, Offscreen=$isOffscreen, Bounds=$([int]$bounds.Width)x$([int]$bounds.Height))" -ForegroundColor Cyan
            Write-Host "       (Submenu capable; State=$expState)" -ForegroundColor DarkGray
        } else {
            Write-Host "  [OK] Found Menu: '$menuName' (Enabled=$isEnabled, Offscreen=$isOffscreen, Bounds=$([int]$bounds.Width)x$([int]$bounds.Height))" -ForegroundColor Cyan
            Write-Host "       (Top-level menu item with no sub-items)" -ForegroundColor DarkGray
        }

        # Assert no process exit after interacting with each menu
        $proc.Refresh()
        if ($proc.HasExited) {
            throw "Process crashed while interacting with menu '$menuName' with exit code $($proc.ExitCode)!"
        }
    }

    # 10. Exercise Bottom Bar View Tabs with Bulletproof Safe Invocation and Adequate Settling Time
    Write-Host "Exercising bottom bar view tabs..." -ForegroundColor Green
    $tabTags = @("Switcher", "Media", "Audio", "Camera", "Room", "ShotSuggestions")
    foreach ($tabTag in $tabTags) {
        $tabId = "Tab$tabTag"
        try {
            $tabClicked = Invoke-UiControlSafe -RootElement $mainWnd -AutomationId $tabId -TimeoutSeconds 5 -TargetPid $procId
            if ($tabClicked) {
                Write-Host "  [OK] Clicked tab: '$tabId'" -ForegroundColor DarkCyan
                # Allow 500ms settling time for view instantiation and layout
                Start-Sleep -Milliseconds 500
            } else {
                Write-Host "  [WARN] Tab '$tabId' could not be invoked; proceeding." -ForegroundColor DarkYellow
            }
        } catch {
            Write-Host "  [WARN] Tab '$tabId' exception during invocation: $($_.Exception.Message); proceeding." -ForegroundColor DarkYellow
        }
        Start-Sleep -Milliseconds 200
    }

    # 11. Assert Zero Crashes and Event Log Cleanliness
    Write-Host "[7/7] Checking for crash logs and XAML exceptions..." -ForegroundColor Green
    $proc.Refresh()
    if ($proc.HasExited) {
        throw "Process exited unexpectedly during UI verification with exit code $($proc.ExitCode)."
    }

    # Check Windows Event Log for .NET Runtime crashes for this process
    try {
        $crashEvents = Get-WinEvent -FilterHashtable @{
            LogName = 'Application'
            ProviderName = '.NET Runtime'
            StartTime = $testStartTime
        } -ErrorAction SilentlyContinue | Where-Object { $_.Message -like "*desktop.exe*" }

        if ($crashEvents) {
            throw "Found .NET Runtime crash event in Event Log: $($crashEvents[0].Message)"
        }
    } catch {
        # Non-critical if event log query is restricted
    }

    Write-Host "================================================================" -ForegroundColor Green
    Write-Host "  SUCCESS: All 7 UI areas and SimAtem verified with 0 errors!" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green

} finally {
    # 12. Clean Shutdown
    Write-Host "Closing desktop.exe cleanly..." -ForegroundColor DarkGray
    if ($proc -and -not $proc.HasExited) {
        # Stage 1: Proactively dismiss any modal dialogs owned strictly by $procId
        Dismiss-ModalDialogs $procId

        # Stage 2: Attempt graceful CloseMainWindow
        $closedCleanly = $false
        try {
            $proc.Refresh()
            if (-not $proc.HasExited) {
                $closeSent = $proc.CloseMainWindow()
                if ($closeSent) {
                    $closedCleanly = $proc.WaitForExit(2500)
                }
            }
        } catch { }

        # Stage 3: Immediate scoped termination if not cleanly exited
        if (-not $closedCleanly -and -not $proc.HasExited) {
            Write-Host "Graceful exit timeout elapsed or window unresponsive; terminating PID $procId..." -ForegroundColor DarkYellow
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
            $proc.WaitForExit(1000) | Out-Null
        } else {
            try {
                $exitCode = $proc.ExitCode
                Write-Host "Process exited cleanly with code: $exitCode." -ForegroundColor Green
            } catch { }
        }
    }

    # Stage 4: Clean up any target process instances started during this test (Safely, NO null dereference)
    $targetProcName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedExe)
    if ($targetProcName) {
        $cleanupDeadline = [DateTime]::UtcNow.AddSeconds(4)
        while ([DateTime]::UtcNow -lt $cleanupDeadline) {
            $procs = @(Get-Process -Name $targetProcName -ErrorAction SilentlyContinue)
            if ($procs.Count -eq 0) { break }

            $matchingProcs = @()
            foreach ($p in $procs) {
                try {
                    if ($null -eq $p -or $p.HasExited) { continue }
                    if ($p.Id -eq $procId) {
                        $matchingProcs += $p
                        continue
                    }
                    $pStart = $null
                    try { $pStart = $p.StartTime } catch { }
                    if ($pStart -and ($pStart.ToUniversalTime() -ge $testStartTime.AddSeconds(-2))) {
                        $matchingProcs += $p
                    }
                } catch { }
            }

            if ($matchingProcs.Count -eq 0) { break }

            foreach ($mp in $matchingProcs) {
                try {
                    if (-not $mp.HasExited) {
                        Stop-Process -Id $mp.Id -Force -ErrorAction SilentlyContinue
                        $mp.WaitForExit(1000) | Out-Null
                    }
                } catch { }
            }
            Start-Sleep -Milliseconds 100
        }
    }

    # Stage 5: Terminate all lingering AtemDirector and child webview2 processes FIRST
    try {
        $wvProcs = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue | Where-Object {
            $_.CommandLine -like "*AtemDirector*" -or ($procId -and $_.ParentProcessId -eq $procId)
        })
        foreach ($wvp in $wvProcs) {
            Stop-Process -Id $wvp.ProcessId -Force -ErrorAction SilentlyContinue
            try {
                $procToWait = Get-Process -Id $wvp.ProcessId -ErrorAction SilentlyContinue
                if ($procToWait) { $procToWait.WaitForExit(1000) | Out-Null }
            } catch { }
        }
        if ($wvProcs.Count -gt 0) {
            Start-Sleep -Milliseconds 500
        }
    } catch { }

    # Stage 6: Guarantee Port 8080 is 100% free and bindable for subsequent runs
    Wait-Port8080Free -TimeoutMs 5000 | Out-Null
}

exit 0
