using System.Runtime.InteropServices;

// Screen capture only imports Windows system libraries. Restrict resolution to System32 so an
// attacker cannot shadow user32/gdi32/dwmapi with a DLL beside the executable or in the CWD.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
