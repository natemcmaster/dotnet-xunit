using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static int Main(string[] args)
    {
        var testProject = Directory.EnumerateFiles(
               Directory.GetCurrentDirectory(),
               "*.*proj")
               .Where(f => !f.EndsWith(".xproj"))
               .SingleOrDefault();

        var projExtTarget = Path.Combine(
            Path.GetDirectoryName(testProject),
            "obj",
            Path.GetFileName(testProject) + ".dotnet-xunit.targets");

        File.WriteAllText(projExtTarget, @"
<Project>
   <Target Name=""_GetXunitTestInfo"" DependsOnTargets=""Build"">
     <ItemGroup>
       <_XunitInfoLines Include=""OutputPath: $(OutputPath)""/>
       <_XunitInfoLines Include=""AssemblyName: $(AssemblyName)""/>
       <_XunitInfoLines Include=""TargetFileName: $(TargetFileName)""/>
     </ItemGroup>
     <WriteLinesToFile File=""$(_XunitInfoFile)"" Lines=""@(_XunitInfoLines)"" Overwrite=""true"" />
   </Target>
</Project>
");

        var tmpFile = Path.GetTempFileName();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"msbuild \"{testProject}\" /t:_GetXunitTestInfo /nologo \"/p:_XunitInfoFile={tmpFile}\""
        };

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("dotnet " + psi.Arguments);
        Console.ResetColor();

        var process = Process.Start(psi);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine("Build failed");
            File.Delete(tmpFile);
            return 1;
        }
        
        var lines = File.ReadAllLines(tmpFile);
        File.Delete(tmpFile); // cleanup

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var name = line.Substring(0, idx)?.Trim();
            var value = line.Substring(idx + 1)?.Trim();
            properties.Add(name, value);
        }

        var thisAssemblyPath = typeof(Program).GetTypeInfo().Assembly.Location;
        //find the console app within the nupkg
        var consolePath = Path.Combine(
            Path.GetDirectoryName(thisAssemblyPath),
            "..", "..", 
            "tools", "netcoreapp1.0", "xunit.console.dll");  

        var depsFile = Path.Combine(properties["OutputPath"], properties["AssemblyName"] + ".deps.json");
        var runtimeConfigFile = Path.Combine(properties["OutputPath"], properties["AssemblyName"] + ".runtimeconfig.json");
        var assembly = Path.Combine(properties["OutputPath"], properties["TargetFileName"]);

        // TODO verify these files all exist, fail with better errors

        psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec --depsfile \"{depsFile}\" --runtimeconfig \"{runtimeConfigFile}\" \"{consolePath}\" \"{assembly}\""
        };
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("dotnet " + psi.Arguments);
        Console.ResetColor();

        var runTests = Process.Start(psi);
        runTests.WaitForExit();

        return runTests.ExitCode;
    }
}
