using Rubedo.Compiler.Util;
using System.IO;

namespace Rubedo.Compiler.ContentBuilders
{
    /// <summary>
    /// TODO: I am IBuildFile, and I don't have a summary yet.
    /// </summary>
    public interface IBuildFile
    {
        int BuildMap(Builder builder, RelativeDirectory currentDirectory);
        int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory);
    }
}