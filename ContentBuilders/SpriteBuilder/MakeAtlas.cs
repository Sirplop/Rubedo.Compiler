using Rubedo;
using Rubedo.Compiler;
using Rubedo.Compiler.Util;
using System.Collections.Generic;
using System.IO;

namespace Rubedo.Compiler.ContentBuilders.SpriteBuilder
{
    /// <summary>
    /// TODO: I am AtlasConfig, and I don't have a summary yet.
    /// </summary>
    public class MakeAtlas : IMapFile, IBuildFile
    {
        public int Map(Builder builder, RelativeDirectory currentDirectory)
        {
            string directoryPath = currentDirectory.relativePath;
            if (!directoryPath.StartsWith(builder.TexturesDirectory))
                return ErrorCodes.NONE; //we're not in atlas country.

            bool atlasAlreadyExists = false;
            foreach (FileInfo file in currentDirectory.directory.GetFiles($"*{FileExtensions.MAKE_ATLAS}"))
            {
                //check if our file buckets already contains a parent config to this file.
                //if not, add a new file bucket.
                if (atlasAlreadyExists)
                {
                    Program.Logger.Error($"Multiple {FileExtensions.MAKE_ATLAS}'s found in '{directoryPath}'! This is not supported.");
                    break;
                }
                Program.Logger.Info($"Generating mapping targeting '{currentDirectory.relativePath}'");

                atlasAlreadyExists = true;
                bool contained = false;
                RelativeDirectory errorPath = null;
                foreach (RelativeDirectory path in builder.directoryMap.Keys)
                {
                    if (directoryPath.StartsWith(path.relativePath))
                    {
                        errorPath = path;
                        contained = true;
                        break;
                    }
                }
                builder.excludedFiles.Add(file.FullName);
                if (!contained) //create mapping from other directories to this one.
                {
                    builder.directoryMap.Add(currentDirectory, (Path.GetFileNameWithoutExtension(file.Name), new List<RelativeDirectory>() { currentDirectory }));
                }
                else
                {
                    Program.Logger.Error($"{FileExtensions.MAKE_ATLAS} at '{directoryPath}' already covered by file in '{errorPath}'! Consider removing one of them.");
                    return ErrorCodes.SUBDIRECTORY_MAKEATLAS;
                }
            }

            if (!atlasAlreadyExists)
            { //if no atlas in this folder, check if this directory is included in any.
                foreach (RelativeDirectory dir in builder.directoryMap.Keys)
                {
                    if (directoryPath.StartsWith(dir.relativePath))
                    {
                        builder.directoryMap[dir].Item2.Add(currentDirectory);
                        break;
                    }
                }
            }
            return ErrorCodes.NONE;
        }
        int IBuildFile.BuildMap(Builder builder, RelativeDirectory currentDirectory)
        {
            string directoryPath = currentDirectory.relativePath;
            if (!directoryPath.StartsWith(builder.TexturesDirectory) || !builder.directoryMap.ContainsKey(currentDirectory))
                return ErrorCodes.NONE; //we're not in atlas country.

            (string name, List<RelativeDirectory> mappedDirectories) = builder.directoryMap[currentDirectory];

            Program.Logger.Info("Building sprite atlas: " + currentDirectory.relativePath + "\\" + name);

            RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);

            string atlasName = "\\" + name + ".png";
            string mapName = "\\" + name + FileExtensions.ATLAS_MAP;
            builder.touchedPaths.Add(outputDir.relativePath + atlasName);
            builder.touchedPaths.Add(outputDir.relativePath + mapName);

            int updateCode = ShouldUpdate(builder, null, currentDirectory);

            if (updateCode == ErrorCodes.SKIPPED)
            {
                Program.Logger.Info("Sprite atlas already up to date.");
                return updateCode; //no need to update.
            }
            else if (updateCode > ErrorCodes.END_OF_NON_ERRORS)
                return updateCode;

            TexturePacker.Config packerConfig = new TexturePacker.Config();

            packerConfig.OutputAtlasFile = outputDir.directory + atlasName;
            packerConfig.OutputMapFile = outputDir.directory + mapName;
            packerConfig.Trim = false; //TODO: Make trim an option in the atlasconfig file. Maybe?
            List<string> inputPaths = new List<string>();
            foreach (RelativeDirectory dir in mappedDirectories)
            {
                DirectoryInfo dirInfo = dir.directory;
                foreach (FileInfo file in dirInfo.GetFiles("*.png"))
                {
                    inputPaths.Add(file.FullName);
                    builder.excludedFiles.Add(file.FullName);
                }
            }
            packerConfig.InputPaths = inputPaths;

            Program.Logger.Info("Generating atlas...");
            TexturePacker.Generate(packerConfig);
            Program.Logger.Info("Atlas generated.");
            return ErrorCodes.NONE;
        }


        public int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
        {
            (string name, List<RelativeDirectory> mappedDirectories) = builder.directoryMap[currentDirectory];
            RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);
            FileInfo outputMap = new FileInfo(outputDir.directory + "\\" + name + FileExtensions.ATLAS_MAP);
            if (!outputMap.Exists)
                return ErrorCodes.NONE; //no existing atlas, make it happen!

            bool needsUpdate = false;
            int includedFiles = 0;
            foreach (RelativeDirectory dir in mappedDirectories)
            {
                DirectoryInfo dirInfo = dir.directory;
                foreach (FileInfo file in dirInfo.GetFiles("*.png"))
                {
                    includedFiles++;
                    builder.excludedFiles.Add(file.FullName);
                    if (file.LastWriteTimeUtc > outputMap.LastWriteTimeUtc)
                        needsUpdate = true;
                }
            }
            if (needsUpdate)
                return ErrorCodes.NONE;

            int lineCount = 0;
            using (var sr = new StreamReader(outputMap.FullName))
            {
                while (!String.IsNullOrEmpty(sr.ReadLine()))
                    lineCount++;
            }
            if (lineCount != includedFiles)
                return ErrorCodes.NONE; //either new files, or removed files, but something has changed!

            return ErrorCodes.SKIPPED;
        }

    }
}