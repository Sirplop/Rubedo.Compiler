using AsepriteDotNet;
using AsepriteDotNet.Aseprite;
using AsepriteDotNet.Aseprite.Types;
using AsepriteDotNet.Common;
using AsepriteDotNet.IO;
using Rubedo.Compiler.Util;
using SkiaSharp;
using System.Formats.Asn1;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rubedo.Compiler.ContentBuilders.SpriteBuilder
{
    /// <summary>
    /// Maps Aseprite files to the <see cref="Builder.virtualFileMap"/>
    /// </summary>
    public class AsepriteLoader : IMapFile, IBuildFile
    {
        private JsonSerializerOptions _jsonOptions;

        public AsepriteLoader()
        {
            _jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Default);
            _jsonOptions.WriteIndented = true;
        }

        private AsepriteFile currentLoadedFile;
        private string loadedFilePath;

        public int Map(Builder builder, RelativeDirectory currentDirectory)
        {
            string directoryPath = currentDirectory.relativePath;
            if (!directoryPath.StartsWith(builder.TexturesDirectory))
                return ErrorCodes.NONE; //we're not in atlas country.

            FileInfo[] files = currentDirectory.directory.GetFiles($"*{FileExtensions.ASEPRITE}");
            if (files.Length == 0)
                return ErrorCodes.NONE; //no aseprite files here.

            Dictionary<RelativeDirectory, List<VirtualFile<object>>> virtualMap;
            List<VirtualFile<object>> virtualList;

            if (!builder.virtualFileMap.TryGetValue(nameof(SKBitmap), out virtualMap))
            {
                builder.virtualFileMap.Add(nameof(SKBitmap), new Dictionary<RelativeDirectory, List<VirtualFile<object>>>());
                virtualMap = builder.virtualFileMap[nameof(SKBitmap)];
                virtualMap.Add(currentDirectory, new List<VirtualFile<object>>());
                virtualList = virtualMap[currentDirectory];
            }
            else if (!virtualMap.TryGetValue(currentDirectory, out virtualList))
            {
                virtualMap.Add(currentDirectory, new List<VirtualFile<object>>());
                virtualList = virtualMap[currentDirectory];
            }

            foreach (FileInfo file in files)
            {
                currentLoadedFile = AsepriteFileLoader.FromFile(file.FullName);
                builder.excludedFiles.Add(file.FullName);

                for (int i = 0; i < currentLoadedFile.FrameCount; i++)
                {
                    AsepriteFrame frame = currentLoadedFile.Frames[i];
                    int newVal = i; //passing in i directly would lead to a reference, we don't want that!
                    VirtualFile<object> frameFile = new VirtualFile<object>(frame.Name, file.LastWriteTimeUtc.Ticks, () => LoadFrame(file.FullName, newVal));

                    virtualList.Add(frameFile);
                }
            }
            currentLoadedFile = null;

            return ErrorCodes.NONE;
        }

        public int BuildMap(Builder builder, RelativeDirectory currentDirectory)
        {
            string directoryPath = currentDirectory.relativePath;

            FileInfo[] sprites = currentDirectory.directory.GetFiles($"*{FileExtensions.ASEPRITE}");
            if (sprites.Length == 0)
                return ErrorCodes.NONE; //no aseprite files in this directory.

            bool contained = false;
            FileInfo atlasPath = null;
            string relativeAtlasPath = string.Empty;
            foreach (RelativeDirectory path in builder.directoryMap.Keys)
            {
                if (directoryPath.StartsWith(path.relativePath))
                {
                    contained = true;
                    //find the atlas file in this directory.
                    RelativeDirectory atlasDir = new RelativeDirectory(path.relativePath, builder.TargetDirectory, true);
                    FileInfo[] atlas = atlasDir.directory.GetFiles($"*{FileExtensions.ATLAS_MAP}");
                    if (atlas.Length == 0)
                    {
                        Program.Logger.Error($"Missing {FileExtensions.ATLAS_MAP} file in output at '{path}'! Did something go wrong somewhere else?");
                        return ErrorCodes.MISSING_FILE;
                    }
                    atlasPath = atlas[0];
                    relativeAtlasPath = path.relativePath + Path.GetFileNameWithoutExtension(atlasPath.Name);
                    break;
                }
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);
                FileInfo file = sprites[i];

                Program.Logger.Info("Building aseprite file: " + currentDirectory.relativePath + "\\" + file.Name);
                string baseName = "\\" + Path.GetFileNameWithoutExtension(file.Name);
                if (!contained)
                {
                    builder.touchedPaths.Add(outputDir.relativePath + baseName + ".png");
                    builder.touchedPaths.Add(outputDir.relativePath + baseName + FileExtensions.ATLAS_MAP);
                }
                builder.touchedPaths.Add(outputDir.relativePath + baseName + FileExtensions.SPRITE_ANIM);

                //ShouldUpdate sets the currentloadedfile, so we don't have to do another load.
                int updateCode = ShouldUpdate(builder, new FileInfo[] { file, atlasPath == null ? new FileInfo(outputDir.directory.FullName +
                "\\" + Path.GetFileNameWithoutExtension(file.Name) + FileExtensions.ATLAS_MAP) : atlasPath }, currentDirectory);

                if (updateCode == ErrorCodes.SKIPPED)
                {
                    Program.Logger.Info("Aseprite file already up to date.");
                    continue;
                }
                else if (updateCode > ErrorCodes.END_OF_NON_ERRORS)
                {
                    return updateCode;
                }

                if (!contained)
                {
                    TexturePacker.Config packerConfig = new TexturePacker.Config();

                    string atlasName = baseName + ".png";
                    string mapName = baseName + FileExtensions.ATLAS_MAP;

                    packerConfig.OutputAtlasFile = outputDir.directory.FullName + atlasName;
                    packerConfig.OutputMapFile = outputDir.directory.FullName + mapName;
                    packerConfig.Trim = false; //TODO: Make trim an option in the animation file.
                    packerConfig.InputPaths = new List<string>();
                    List<(string, SKBitmap)> extraBitmaps = new List<(string, SKBitmap)>();
                    for (int j = 0; j < currentLoadedFile.FrameCount; j++)
                    {
                        extraBitmaps.Add((currentLoadedFile.Frames[j].Name, LoadFrameNoCheck(j)));
                    }

                    Program.Logger.Info("Generating atlas...");
                    TexturePacker.Generate(packerConfig, extraBitmaps);
                    atlasPath = new FileInfo(packerConfig.OutputMapFile);
                    relativeAtlasPath = outputDir.relativePath + "\\" + Path.GetFileNameWithoutExtension(atlasPath.Name);
                    Program.Logger.Info("Atlas generated.");
                }

                AtlasReader reader = new AtlasReader(atlasPath);

                var animations = currentLoadedFile.Tags;
                var frames = currentLoadedFile.Frames;
                if (animations.Length == 0)
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);
                    List<(int, int)> animationFrames = new List<(int, int)>(currentLoadedFile.FrameCount);

                    for (int j = 0; j < currentLoadedFile.FrameCount; j++)
                    {
                        animationFrames.Add((reader.nameToIndexMap[frames[j].Name], frames[j].Duration.Milliseconds));
                    }
                    JsonObject spriteAnim = SpriteAnim.CreateSpriteAnimJson(name, true, false, false, animationFrames, relativeAtlasPath);

                    string output = JsonSerializer.Serialize(spriteAnim, _jsonOptions);
                    File.WriteAllText(outputDir.directory.FullName + baseName + FileExtensions.SPRITE_ANIM, output);
                    Program.Logger.Info($"Aseprite animation '{name}' built.");
                }
                else
                {
                    Program.Logger.Info("Building animations...");
                    for (int j = 0; j < animations.Length; j++)
                    {
                        AsepriteTag tag = animations[j];
                        JsonObject spriteAnim = ProcessTag(tag, frames, relativeAtlasPath, reader);

                        string output = JsonSerializer.Serialize(spriteAnim, _jsonOptions);
                        File.WriteAllText(outputDir.directory.FullName + "\\" + tag.Name + FileExtensions.SPRITE_ANIM, output);
                        Program.Logger.Info($"Aseprite animation '{tag.Name}' built.");
                    }
                }
                Program.Logger.Info($"Aseprite file built.");
            }

            return ErrorCodes.NONE;
        }

        private JsonObject ProcessTag(AsepriteTag aseTag, ReadOnlySpan<AsepriteFrame> aseFrames, string atlas, AtlasReader reader)
        {
            int frameCount = aseTag.To - aseTag.From + 1;
            List<(int, int)> animationFrames = new List<(int, int)>(frameCount);

            for (int i = 0; i < frameCount; i++)
            {
                int index = aseTag.From + i;
                animationFrames.Add((reader.nameToIndexMap[aseFrames[index].Name], aseFrames[index].Duration.Milliseconds));
            }

            //  In Aseprite, all tags are looping
            int loopCount = aseTag.Repeat;
            bool isReversed = aseTag.LoopDirection == AsepriteLoopDirection.Reverse || aseTag.LoopDirection == AsepriteLoopDirection.PingPongReverse;
            bool isPingPong = aseTag.LoopDirection == AsepriteLoopDirection.PingPong || aseTag.LoopDirection == AsepriteLoopDirection.PingPongReverse;

            return SpriteAnim.CreateSpriteAnimJson(aseTag.Name, loopCount > 1, isPingPong, isReversed, animationFrames, atlas);
        }

        public int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
        {
            //does this actually HAVE an animation?
            currentLoadedFile = AsepriteFileLoader.FromFile(relevantFiles[0].FullName);
            if (currentLoadedFile.FrameCount > 1) //yes it does!
                return AnimatedShouldUpdate(builder, relevantFiles, currentDirectory);
            else
                return NotAnimatedShouldUpdate(builder, relevantFiles, currentDirectory);
        }
        private int NotAnimatedShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
        {
            RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);
            //check for either a containing atlas or an output file.
            if (relevantFiles[1] != null)
                return ErrorCodes.SKIPPED; //an atlas exists, and we must assume the image is in it.

            FileInfo textureOutput = new FileInfo(outputDir.directory.FullName + "\\" + Path.GetFileNameWithoutExtension(relevantFiles[0].Name) + ".png");
            if (!textureOutput.Exists || textureOutput.LastWriteTimeUtc.Ticks < relevantFiles[0].LastWriteTimeUtc.Ticks)
                return ErrorCodes.NONE; //texture either doesn't exist or needs an update!
            else
                return ErrorCodes.SKIPPED; //texture up to date.
        }
        private int AnimatedShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
        {
            RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);

            FileInfo asespriteFile = relevantFiles[0];
            if (currentLoadedFile.Tags.Length > 0)
            {
                for (int i = 0; i < currentLoadedFile.Tags.Length; i++)
                {
                    FileInfo outputFile = new FileInfo(outputDir.directory.FullName + "\\" + currentLoadedFile.Tags[i].Name + FileExtensions.SPRITE_ANIM);
                    if (!outputFile.Exists || outputFile.LastWriteTimeUtc < asespriteFile.LastWriteTimeUtc)
                        return ErrorCodes.NONE; //at least one of the animations has updated!
                }
            } else
            {
                FileInfo outputFile = new FileInfo(outputDir.directory.FullName + "\\" + currentLoadedFile.Name + FileExtensions.SPRITE_ANIM);
                if (!outputFile.Exists || outputFile.LastWriteTimeUtc < asespriteFile.LastWriteTimeUtc)
                    return ErrorCodes.NONE; //at least one of the animations has updated!
            }

                FileInfo atlasFile = relevantFiles[1]; //target atlasmap file
            if (atlasFile == null || !atlasFile.Exists || atlasFile.LastWriteTimeUtc < asespriteFile.LastWriteTimeUtc)
                return ErrorCodes.NONE; //atlas doesn't exist, needs to be created!

            return ErrorCodes.SKIPPED;
        }


        private SKBitmap LoadFrame(string asepriteFile, int frameIndex)
        {
            if (currentLoadedFile == null || loadedFilePath != asepriteFile)
            {
                currentLoadedFile = AsepriteFileLoader.FromFile(asepriteFile);
                loadedFilePath = asepriteFile;
            }
            return LoadFrameNoCheck(frameIndex);
        }
        private SKBitmap LoadFrameNoCheck(int frameIndex)
        {
            AsepriteFrame frame = currentLoadedFile.Frames[frameIndex];
            Size size = frame.Size;
            SKBitmap bitmap = new SKBitmap(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            SKPixmap pix = bitmap.PeekPixels();
            Span<byte> pixels = pix.GetPixelSpan();
            Rgba32[] colors = frame.FlattenFrame();

            for (int i = 0; i < colors.Length; i++)
            {
                pixels[i * 4] = colors[i].R;
                pixels[i * 4 + 1] = colors[i].G;
                pixels[i * 4 + 2] = colors[i].B;
                pixels[i * 4 + 3] = colors[i].A;
            }

            return bitmap;
        }
    }
}