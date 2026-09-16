Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes

$proc = Start-Process -FilePath "desktop\bin\Debug\net9.0-windows\desktop.exe" -WorkingDirectory "desktop\bin\Debug\net9.0-windows" -PassThru
Write-Host "Started PID: $($proc.Id)"

for ($sec = 1; $sec -le 35; $sec++) {
    Start-Sleep -Seconds 1
    $w = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, "Vidikom Atem Software Control"))
        ))
    )
    if ($w) {
        $descendants = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Host "Sec $sec : Descendants count = $($descendants.Count)"
        if ($descendants.Count -gt 0) {
            $menus = $w.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Menu
                ))
            )
            Write-Host "Sec $sec : Menus found = $($menus.Count)"
            if ($menus.Count -gt 0) {
                Write-Host "Menu Bar ready!"
                break
            }
        }
    }
}

if (-not $proc.HasExited) {
    Stop-Process -Id $proc.Id -Force
}
