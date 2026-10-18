using System.Reflection;

// Version of the Serial Generator, compiled into BOTH CardStudio.SerialGenerator.exe and its setup
// (SerialGenerator-Setup.exe), so Settings > Apps shows the version that is installed.
// Separate from Card Studio's version (<repo>\Version.cs). Bump it when the generator changes, or let the build stamp it:
//     powershell -ExecutionPolicy Bypass -File Installer\build-serial-generator.ps1 -Version 1.1.0
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
