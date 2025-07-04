using Rubedo.Compiler.Util;
using System.Collections.Generic;
using System.IO;

namespace Rubedo.Compiler.ContentBuilders
{
    /// <summary>
    /// TODO: I am IMapFile, and I don't have a summary yet.
    /// </summary>
    public interface IMapFile
    {
        int Map(Builder builder, RelativeDirectory currentDirectory);
    }
}