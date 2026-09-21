$AppName = "LOQ Native Control"
$AppExe = "LoqNative.exe"
$InstallDir = "$env:LOCALAPPDATA\Programs\LoqNative"
$StartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
$RegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LoqNative"

# 1. Compile the application into a single production executable
Write-Host "Compiling production build..." -ForegroundColor Cyan
dotnet publish -c Release -p:PublishSingleFile=true -o $InstallDir

# 2. Create Start Menu Shortcut (Enables Win+S Search)
Write-Host "Registering Start Menu search..." -ForegroundColor Cyan
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($StartMenuPath)
$Shortcut.TargetPath = "$InstallDir\$AppExe"
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.IconLocation = "$InstallDir\$AppExe,0"
$Shortcut.Save()

# 3. Register in Windows "Installed Apps"
Write-Host "Registering in Windows Registry..." -ForegroundColor Cyan
if (!(Test-Path $RegPath)) { New-Item -Path $RegPath -Force | Out-Null }
Set-ItemProperty -Path $RegPath -Name "DisplayName" -Value $AppName
Set-ItemProperty -Path $RegPath -Name "DisplayIcon" -Value "$InstallDir\$AppExe,0"
Set-ItemProperty -Path $RegPath -Name "Publisher" -Value "Phaniharam Venkata Ramanuja Manideep"
Set-ItemProperty -Path $RegPath -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $RegPath -Name "EstimatedSize" -Value 150000 -Type DWord
Set-ItemProperty -Path $RegPath -Name "NoModify" -Value 1 -Type DWord

# 4. Generate the native uninstaller
$UninstallCmd = "powershell.exe -NoProfile -Command `"Remove-Item -Path '$InstallDir' -Recurse -Force; Remove-Item -Path '$StartMenuPath' -Force; Remove-Item -Path '$RegPath' -Force`""
Set-ItemProperty -Path $RegPath -Name "UninstallString" -Value $UninstallCmd

Write-Host "`nDeployment Complete! You can now press Win+S and search for 'LOQ Native Control'." -ForegroundColor Green