using Rubedo.Compiler.Util;
using ShadowDusk.Core;

namespace Rubedo.Compiler.ContentBuilders.ShaderBuilders;

/// <summary>
/// Compiles .fx shader source files into .mgfx binaries in-process, using ShadowDusk
/// (https://github.com/kaltinril/ShadowDusk) once per configured <see cref="ShaderProfiles"/>
/// NuGet package.
/// </summary>
public class ShaderBuilder : IBuildFile
{
    /// <summary>
    /// The set of platform targets to compile every shader for, and the filename suffix
    /// used to disambiguate each target's output on disk.
    /// </summary>
    public static readonly (PlatformTarget Target, string Suffix)[] ShaderProfiles = new[]
    {
        (PlatformTarget.OpenGL, "ogl"),
        (PlatformTarget.DirectX, "dx11"),
    };

    //Reused across every shader/profile in the build; safe to share, and avoids
    //re-touching the native compiler modules per-file.
    private readonly ShadowDusk.Compiler.EffectCompiler _compiler = new();

    public int BuildMap(Builder builder, RelativeDirectory currentDirectory)
    {
        //shader include files (.fxh) are compile-time only; never copy them to output.
        FileInfo[] includes = currentDirectory.directory.GetFiles($"*{FileExtensions.SHADER_INCLUDE}");
        long newestIncludeWriteTimeUtc = 0;
        for (int i = 0; i < includes.Length; i++)
        {
            builder.excludedFiles.Add(includes[i].FullName);
            if (includes[i].LastWriteTimeUtc.Ticks > newestIncludeWriteTimeUtc)
                newestIncludeWriteTimeUtc = includes[i].LastWriteTimeUtc.Ticks;
        }
        //NOTE: this only catches .fxh files that live alongside the .fx that includes them.
        //A shared include pulled in from a different directory won't trigger a rebuild on
        //its own - only the .fx file's own timestamp is guaranteed to be tracked correctly.

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
                (PlatformTarget target, string suffix) = ShaderProfiles[p];

                string outputName = $"{baseName}.{suffix}{FileExtensions.COMPILED_SHADER}";
                builder.TouchPath(outputDir.relativePath + "\\" + outputName);

                FileInfo outputFile = new FileInfo(Path.Combine(outputDir.directory.FullName, outputName));

                int updateCode = ShouldUpdate(builder, new FileInfo[] { file, outputFile }, currentDirectory, newestIncludeWriteTimeUtc);
                if (updateCode == ErrorCodes.SKIPPED)
                {
                    Program.Logger.Info($"Shader '{currentDirectory.relativePath}\\{file.Name}' [{target}] already up-to-date.");
                    continue;
                }
                else if (updateCode > ErrorCodes.END_OF_NON_ERRORS)
                {
                    return updateCode;
                }

                Program.Logger.Info($"Compiling shader: {currentDirectory.relativePath}\\{file.Name} [{target}]");
                int code = Compile(file.FullName, outputFile.FullName, target);
                if (code != ErrorCodes.NONE)
                    return code;

                Program.Logger.Info($"Shader compiled: {outputName}");
            }
        }
        return ErrorCodes.NONE;
    }

    /// <summary>
    /// Compiles a single .fx file for a single target via ShadowDusk's EffectCompiler.
    /// </summary>
    private int Compile(string sourcePath, string outputPath, PlatformTarget target)
    {
        string source;
        try
        {
            source = File.ReadAllText(sourcePath);
        }
        catch (IOException e)
        {
            Program.Logger.Error($"Could not read shader source '{sourcePath}': {e.Message}");
            return ErrorCodes.MISSING_FILE;
        }

        CompilerOptions options = new CompilerOptions
        {
            Target = target,
            //Relative #include directives resolve against this file's directory automatically.
            SourceFileName = sourcePath,
        };

        Result<CompiledShader, ShaderError[]> result = _compiler.Compile(source, options);

        if (result.IsFailure)
        {
            Program.Logger.Error($"Shader compilation failed for '{sourcePath}' [{target}]:");
            foreach (ShaderError error in result.Error)
                Program.Logger.Error("  " + error.FxcFormattedMessage);
            return ErrorCodes.SHADER_COMPILE_FAILED;
        }

        try
        {
            File.WriteAllBytes(outputPath, result.Value.Data);
        }
        catch (IOException e)
        {
            Program.Logger.Error($"Could not write compiled shader '{outputPath}': {e.Message}");
            return ErrorCodes.SHADER_COMPILE_FAILED;
        }

        foreach (ShaderError warning in result.Value.Warnings)
            Program.Logger.Warn("  " + warning.FxcFormattedMessage);

        return ErrorCodes.NONE;
    }

    /// <summary>
    /// relevantFiles[0] = source .fx file, relevantFiles[1] = this profile's compiled output.
    /// </summary>
    public int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
        => ShouldUpdate(builder, relevantFiles, currentDirectory, 0);

    private int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory, long newestIncludeWriteTimeUtc)
    {
        FileInfo source = relevantFiles[0];
        FileInfo output = relevantFiles[1];

        if (!output.Exists)
            return ErrorCodes.NONE; //needs compiling.

        if (output.LastWriteTimeUtc < source.LastWriteTimeUtc)
            return ErrorCodes.NONE; //source changed since last compile.

        if (newestIncludeWriteTimeUtc > 0 && output.LastWriteTimeUtc.Ticks < newestIncludeWriteTimeUtc)
            return ErrorCodes.NONE; //a sibling .fxh changed since last compile.

        return ErrorCodes.SKIPPED;
    }
}