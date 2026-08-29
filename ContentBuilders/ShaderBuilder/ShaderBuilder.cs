using Rubedo.Compiler.Util;
using System.Diagnostics;

namespace Rubedo.Compiler.ContentBuilders.ShaderBuilders;

/// <summary>
/// Compiles .fx shader source files into .mgfxo binaries using the MonoGame Effect
/// Compiler (mgfxc), once per configured <see cref="ShaderProfiles"/> entry.
/// <br/><br/>
/// Requires the mgfxc dotnet tool to be available on PATH:
/// <c>dotnet tool install -g dotnet-mgfxc</c>
/// </summary>
public class ShaderBuilder : IBuildFile
{   
    /// <summary>
    /// The set of mgfxc profiles to compile every shader for, and the filename suffix
    /// used to disambiguate each profile's output on disk.
    /// </summary>
    public static readonly (string Profile, string Suffix)[] ShaderProfiles = new[]
    {
        ("OpenGL", "ogl"),
        ("DirectX_11", "dx11"),
    };

    public int BuildMap(Builder builder, RelativeDirectory currentDirectory)
    {
        //shader include files (.fxh) are compile-time only; never copy them to output.
        FileInfo[] includes = currentDirectory.directory.GetFiles($"*{FileExtensions.SHADER_INCLUDE}");
        for (int i = 0; i < includes.Length; i++)
            builder.excludedFiles.Add(includes[i].FullName);

        FileInfo[] shaders = currentDirectory.directory.GetFiles($"*{FileExtensions.SHADER}");
        if (shaders.Length == 0)
            return ErrorCodes.NONE; //no shaders in this directory.

        RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);

        for (int i = 0; i < shaders.Length; i++)
        {
            FileInfo file = shaders[i];
            builder.excludedFiles.Add(file.FullName);

            string baseName = Path.GetFileNameWithoutExtension(file.Name);

            for (int p = 0; p < ShaderProfiles.Length; p++)
            {
                (string profile, string suffix) = ShaderProfiles[p];

                string outputName = $"{baseName}.{suffix}{FileExtensions.COMPILED_SHADER}";
                builder.touchedPaths.Add(outputDir.relativePath + "\\" + outputName);

                FileInfo outputFile = new FileInfo(Path.Combine(outputDir.directory.FullName, outputName));

                int updateCode = ShouldUpdate(builder, new FileInfo[] { file, outputFile }, currentDirectory);
                if (updateCode == ErrorCodes.SKIPPED)
                {
                    Program.Logger.Info($"Shader '{currentDirectory.relativePath}\\{file.Name}' [{profile}] already up-to-date.");
                    continue;
                }
                else if (updateCode > ErrorCodes.END_OF_NON_ERRORS)
                {
                    return updateCode;
                }

                Program.Logger.Info($"Compiling shader: {currentDirectory.relativePath}\\{file.Name} [{profile}]");
                int code = Compile(file.FullName, outputFile.FullName, profile);
                if (code != ErrorCodes.NONE)
                    return code;

                Program.Logger.Info($"Shader compiled: {outputName}");
            }
        }
        return ErrorCodes.NONE;
    }

    /// <summary>
    /// Shells out to mgfxc to compile a single .fx file for a single profile.
    /// </summary>
    private int Compile(string sourcePath, string outputPath, string profile)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "mgfxc",
            Arguments = $"\"{sourcePath}\" \"{outputPath}\" /Profile:{profile}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using Process? process = Process.Start(psi);
            if (process == null)
            {
                Program.Logger.Error($"Failed to start mgfxc process for '{sourcePath}'.");
                return ErrorCodes.SHADER_COMPILE_FAILED;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Program.Logger.Error($"Shader compilation failed for '{sourcePath}' [{profile}]:");
                if (!string.IsNullOrWhiteSpace(stdout))
                    Program.Logger.Error(stdout.Trim());
                if (!string.IsNullOrWhiteSpace(stderr))
                    Program.Logger.Error(stderr.Trim());
                return ErrorCodes.SHADER_COMPILE_FAILED;
            }

            if (!string.IsNullOrWhiteSpace(stdout))
                Program.Logger.Debug(stdout.Trim());

            return ErrorCodes.NONE;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Program.Logger.Error("Could not find 'mgfxc' on PATH. Install it with: dotnet tool install -g dotnet-mgfxc");
            Program.Logger.Error(e.Message);
            return ErrorCodes.MGFXC_NOT_FOUND;
        }
    }

    /// <summary>
    /// relevantFiles[0] = source .fx file, relevantFiles[1] = this profile's compiled output.
    /// </summary>
    public int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
    {
        FileInfo source = relevantFiles[0];
        FileInfo output = relevantFiles[1];

        if (!output.Exists || output.LastWriteTimeUtc < source.LastWriteTimeUtc)
            return ErrorCodes.NONE; //needs (re)compiling.

        return ErrorCodes.SKIPPED;
    }
}