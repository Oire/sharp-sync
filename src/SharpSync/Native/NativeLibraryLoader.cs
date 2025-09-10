using System.Reflection;
using System.Runtime.InteropServices;

namespace Oire.SharpSync.Native;

internal static class NativeLibraryLoader
{
    private static readonly string[] WindowsLibraryNames = { "csync.dll", "libcsync.dll" };
    private static readonly string[] LinuxLibraryNames = { "libcsync.so", "libcsync.so.0", "csync.so" };
    private static readonly string[] MacOSLibraryNames = { "libcsync.dylib", "csync.dylib" };
    
    private static IntPtr _libraryHandle;
    
    public static IntPtr LoadNativeLibrary()
    {
        if (_libraryHandle != IntPtr.Zero)
            return _libraryHandle;
        
        var libraryNames = GetPlatformLibraryNames();
        var searchPaths = GetSearchPaths();
        
        foreach (var path in searchPaths)
        {
            foreach (var libraryName in libraryNames)
            {
                var fullPath = Path.Combine(path, libraryName);
                if (TryLoadLibrary(fullPath, out _libraryHandle))
                {
                    return _libraryHandle;
                }
            }
        }
        
        foreach (var libraryName in libraryNames)
        {
            if (TryLoadLibrary(libraryName, out _libraryHandle))
            {
                return _libraryHandle;
            }
        }
        
        var errorMessage = BuildErrorMessage(libraryNames, searchPaths);
        throw new DllNotFoundException(errorMessage);
    }
    
    private static string[] GetPlatformLibraryNames()
    {
        if (OperatingSystem.IsWindows())
            return WindowsLibraryNames;
        else if (OperatingSystem.IsLinux())
            return LinuxLibraryNames;
        else if (OperatingSystem.IsMacOS())
            return MacOSLibraryNames;
        else
            throw new PlatformNotSupportedException($"Platform {Environment.OSVersion.Platform} is not supported");
    }
    
    private static List<string> GetSearchPaths()
    {
        var paths = new List<string>();
        
        var assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyLocation))
        {
            paths.Add(assemblyLocation);
            
            var rid = GetRuntimeIdentifier();
            if (!string.IsNullOrEmpty(rid))
            {
                paths.Add(Path.Combine(assemblyLocation, "runtimes", rid, "native"));
            }
            
            paths.Add(Path.Combine(assemblyLocation, "native"));
            
            if (OperatingSystem.IsWindows())
            {
                paths.Add(Path.Combine(assemblyLocation, "x64"));
                paths.Add(Path.Combine(assemblyLocation, "x86"));
            }
        }
        
        paths.Add(AppContext.BaseDirectory);
        paths.Add(Environment.CurrentDirectory);
        
        if (OperatingSystem.IsLinux())
        {
            paths.Add("/usr/lib");
            paths.Add("/usr/local/lib");
            paths.Add("/usr/lib/x86_64-linux-gnu");
            paths.Add("/usr/lib64");
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add("/usr/local/lib");
            paths.Add("/opt/homebrew/lib");
            paths.Add("/usr/lib");
        }
        
        return paths.Where(Directory.Exists).Distinct().ToList();
    }
    
    private static string GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        }
        else if (OperatingSystem.IsLinux())
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                return "linux-x64";
            else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "linux-arm64";
            else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm)
                return "linux-arm";
            else
                return "linux";
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "osx-arm64";
            else
                return "osx-x64";
        }
        
        return string.Empty;
    }
    
    private static bool TryLoadLibrary(string path, out IntPtr handle)
    {
        try
        {
            handle = NativeLibrary.Load(path);
            return handle != IntPtr.Zero;
        }
        catch
        {
            handle = IntPtr.Zero;
            return false;
        }
    }
    
    private static string BuildErrorMessage(string[] libraryNames, List<string> searchPaths)
    {
        var message = $"Unable to load CSync native library. Searched for: {string.Join(", ", libraryNames)}\n\n";
        message += "Search paths:\n";
        foreach (var path in searchPaths)
        {
            message += $"  - {path}\n";
        }
        message += "\nPlease ensure CSync is installed:\n";
        
        if (OperatingSystem.IsWindows())
        {
            message += "  - Download from https://csync.org/download\n";
            message += "  - Or place csync.dll in the application directory\n";
        }
        else if (OperatingSystem.IsLinux())
        {
            message += "  - Ubuntu/Debian: sudo apt-get install csync\n";
            message += "  - CentOS/RHEL: sudo yum install csync\n";
            message += "  - Or compile from source: https://github.com/csync/csync\n";
        }
        else if (OperatingSystem.IsMacOS())
        {
            message += "  - Homebrew: brew install csync\n";
            message += "  - Or compile from source: https://github.com/csync/csync\n";
        }
        
        return message;
    }
    
    public static void SetDllImportResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(CSyncNative).Assembly, DllImportResolver);
    }
    
    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == CSyncNative.CSyncLibrary)
        {
            return LoadNativeLibrary();
        }
        
        return IntPtr.Zero;
    }
}