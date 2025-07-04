using Rubedo.Compiler.ContentBuilders.SpriteBuilder;
using Rubedo.Compiler.Util;
using Rubedo.Lib.Extensions;
using System.Diagnostics;

namespace Rubedo.Compiler.ContentBuilders
{
    /// <summary>
    /// TODO: I am Builder, and I don't have a summary yet.
    /// </summary>
    public class Builder
    {
        public readonly string SourceDirectory;
        public readonly string TargetDirectory;
        public readonly string TexturesDirectory;

        /// <summary>
        /// Maps several directories to a specific directory, with a given map name.<br></br>
        /// Target - TargetName, List_of_things_that_map_to_the_target
        /// </summary>
        public Dictionary<RelativeDirectory, (string, List<RelativeDirectory>)> directoryMap = new Dictionary<RelativeDirectory, (string, List<RelativeDirectory>)>();
        public List<DirectoryInfo> directories = new List<DirectoryInfo>();
        public HashSet<string> excludedFiles = new HashSet<string>();

        public List<string> touchedPaths = new List<string>();

        public List<IMapFile> Mappers { get; private set; } = new List<IMapFile>()
        {
            new MakeAtlas()
        };
        public List<IBuildFile> Builders { get; private set; } = new List<IBuildFile>()
        {
            new MakeAtlas(),
            new SpriteAnim()
        };

        public Builder(string source, string target, string textures)
        {
            SourceDirectory = source;
            TargetDirectory = target;
            TexturesDirectory = textures;
            if (!Directory.Exists(TargetDirectory))
                Directory.CreateDirectory(TargetDirectory); //make sure ./Content exists in output.
            string spacer = new string(' ', 36);
            Program.Logger.Info($"Starting content build from->to:\n{spacer}'{SourceDirectory}'\n{spacer}'{target}'");
        }

        public void Build()
        {
            Stopwatch sw = Stopwatch.StartNew();

            int code = GenerateMappings();
            if (code > ErrorCodes.END_OF_NON_ERRORS)
            {
                Program.Logger.Error($"Error code {code}. Aborting build.");
                return;
            }
            BuildDirectories();
            code = BuildMaps();
            if (code > ErrorCodes.END_OF_NON_ERRORS)
            {
                Program.Logger.Error($"Error code {code}. Aborting build.");
                return;
            }
            BuildNonMapped();
            RemoveDeletedFiles();

            sw.Stop();
            Program.Logger.Info($"Completed build in {sw.Elapsed.TotalSeconds} seconds.");
        }

        private int GenerateMappings()
        {
            Program.Logger.Info("Generating Mappings...");
            directoryMap.Clear();
            directories.Clear();
            excludedFiles.Clear();
            touchedPaths.Clear();

            touchedPaths.Add("Fonts"); //default font in rubedo.
            touchedPaths.Add("Fonts\\Consolas.ttf"); //default font in rubedo.

            //generate a list of directories, and map them as we go.
            //always guarenteed to view parents before children.

            DirectoryInfo baseDirectoryInfo = new DirectoryInfo(SourceDirectory);
            directories.Add(baseDirectoryInfo);

            int code;
            for (int i = 0; i < directories.Count; i++)
            { //creates list of all directories.
                DirectoryInfo info = directories[i];
                foreach (DirectoryInfo dir in info.GetDirectories())
                {
                    directories.Add(dir);
                }
                RelativeDirectory rel = new RelativeDirectory(info.FullName, SourceDirectory, false);
                touchedPaths.Add(rel.relativePath);
                foreach (IMapFile map in Mappers)
                {
                    code = map.Map(this, rel);
                    if (code > ErrorCodes.END_OF_NON_ERRORS)
                        return code;
                }
            }
            return ErrorCodes.NONE;
        }

        private void BuildDirectories()
        {
            Program.Logger.Info("Creating output directories...");
            for (int i = 0; i < directories.Count; i++)
            {
                DirectoryInfo curDir = directories[i];
                RelativeDirectory outRel = new RelativeDirectory(Path.GetRelativePath(SourceDirectory, curDir.FullName), TargetDirectory, true);
                Directory.CreateDirectory(outRel.directory.FullName);
            }
        }

        private int BuildMaps()
        {
            Program.Logger.Info("Building mappings...");
            int code;
            for (int i = 0; i < directories.Count; i++)
            {
                RelativeDirectory rel = new RelativeDirectory(directories[i].FullName, SourceDirectory, false);
                foreach (IBuildFile builder in Builders)
                {
                    code = builder.BuildMap(this, rel);
                    if (code > ErrorCodes.END_OF_NON_ERRORS)
                        return code;
                }
            }
            return ErrorCodes.NONE;
        }

        private void BuildNonMapped()
        {
            Program.Logger.Info("Building non-mapped files...");
            int included = 0, excluded = 0;
            int sourceLen = SourceDirectory.Length;
            for (int i = 0; i < directories.Count; i++)
            {
                DirectoryInfo dir = directories[i];
                FileInfo[] files = dir.GetFiles();
                for (int j = 0; j < files.Length; j++)
                {
                    FileInfo file = files[j];
                    if (excludedFiles.Contains(file.FullName))
                        continue;

                    touchedPaths.Add(Path.GetRelativePath(SourceDirectory, file.FullName));

                    FileInfo output = new FileInfo(file.FullName.Replace(SourceDirectory, TargetDirectory));
                    if (!output.Exists || output.LastWriteTimeUtc.Ticks < file.LastWriteTimeUtc.Ticks)
                    {
                        file.CopyTo(output.FullName, true);
                        included++;
                    } 
                    else 
                        excluded++;
                }
            }
            Program.Logger.Info($"Finished building non-mapped files: {included} {(included == 1 ? "file" : "files")} copied, {excluded} {(excluded == 1 ? "file" : "files")} already up-to-date.");
        }
        private void RemoveDeletedFiles()
        {
            Program.Logger.Info("Removing old files not present in the new build...");
            List<RelativeDirectory> outputDirs = new List<RelativeDirectory>();

            RelativeDirectory baseDirectoryInfo = new RelativeDirectory(TargetDirectory, TargetDirectory, false);
            outputDirs.Add(baseDirectoryInfo);

            int removed = 0;
            for (int i = 0; i < outputDirs.Count; i++)
            { //creates list of all directories.
                RelativeDirectory info = outputDirs[i];
                RelativeDirectory sourceDir = new RelativeDirectory(info.relativePath, SourceDirectory, true);

                int touchedIndex = touchedPaths.IndexOf(sourceDir.relativePath);

                if (touchedIndex == -1)
                { //directory not touched! Delete and continue.
                    removed++;
                    info.directory.Delete(true);
                    continue;
                }
                touchedPaths.SwapAndRemove(touchedIndex);

                foreach (FileInfo file in info.directory.GetFiles())
                {
                    string path = Path.GetRelativePath(TargetDirectory, file.FullName);
                    touchedIndex = touchedPaths.IndexOf(path);
                    if (touchedIndex == -1)
                    {
                        File.Delete(TargetDirectory + "\\" + path);
                        removed++;
                    }
                    else
                    {
                        touchedPaths.SwapAndRemove(touchedIndex);
                    }
                }

                foreach (DirectoryInfo dir in info.directory.GetDirectories())
                {
                    outputDirs.Add(new RelativeDirectory(dir.FullName, TargetDirectory, false));
                }
            }
            Program.Logger.Info($"Removed {removed} old {(removed == 1 ? "file" : "files")}.");
        }
    }
}