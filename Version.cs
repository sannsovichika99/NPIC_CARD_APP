using System.Reflection;

// The one version number of Card Studio, compiled into BOTH the app (NPIC_CARD_APP.exe) and the setup
// (CardStudio-Setup.exe), so the app knows which version it is when it checks for updates and the setup writes
// the same number to Settings > Apps.
//
// Bump it for every release, or let the build stamp it:
//     powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.2.0
[assembly: AssemblyVersion("1.0.0.5")]
[assembly: AssemblyFileVersion("1.0.0.5")]
