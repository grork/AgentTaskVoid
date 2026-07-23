@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  Dogfood uninstaller launcher.
rem
rem  Bounces to Windows PowerShell with -ExecutionPolicy Bypass so uninstall.ps1
rem  runs straight from a UNC file share (\\server\share\...) or by double-click,
rem  without changing the machine's execution policy or unblocking the script by
rem  hand. Arguments (for example -Thumbprint <hash>) pass through to
rem  uninstall.ps1.
rem ---------------------------------------------------------------------------

rem Give the child process a real filesystem working directory even when this
rem ran from a UNC path (cmd.exe cannot cd into \\server\share); %~dp0 still
rem resolves to this launcher's own folder.
pushd "%~dp0" 2>nul

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
set "_rc=%ERRORLEVEL%"

popd 2>nul

rem Keep the window open when launched by double-click (Explorer starts cmd.exe
rem with /c) so the recipient can read the final output; a console caller gets
rem no extra prompt.
echo(%CMDCMDLINE% | find /i "/c" >nul && pause

exit /b %_rc%
