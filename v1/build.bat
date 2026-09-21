@echo off
echo Compiling Assembly Backend...
ml64 /c telemetry.asm
link /DLL /NOENTRY /DEF:telemetry.def telemetry.obj /OUT:telemetry.dll

echo Compiling C# Application...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /reference:System.Management.dll /out:LoqApp.exe Program.cs

echo Build Complete.
pause