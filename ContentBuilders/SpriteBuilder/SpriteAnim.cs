using System.Text.Json.Nodes;
using System.Text.Json;
using Rubedo.Compiler.Util;

namespace Rubedo.Compiler.ContentBuilders.SpriteBuilder;

/// <summary>
/// TODO: I am SpriteAnim, and I don't have a summary yet.
/// </summary>
public class SpriteAnim : IBuildFile
{
    private JsonSerializerOptions _jsonOptions;

    public SpriteAnim()
    {
        _jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Default);
        _jsonOptions.WriteIndented = true;
    }

    public int BuildMap(Builder builder, RelativeDirectory currentDirectory)
    {
        string directoryPath = currentDirectory.relativePath;
        if (!directoryPath.StartsWith(builder.TexturesDirectory))
            return ErrorCodes.NONE; //we're not in sprite country.

        FileInfo[] anims = currentDirectory.directory.GetFiles($"*{FileExtensions.SPRITE_ANIM}");
        if (anims.Length == 0)
            return ErrorCodes.NONE; //no animations in this directory.


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

        for (int i = 0; i < anims.Length; i++)
        {
            RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);
            FileInfo file = anims[i];
            builder.excludedFiles.Add(file.FullName);
            Program.Logger.Info("Building sprite animation: " + currentDirectory.relativePath + "\\" + file.Name);
            if (!contained)
            {
                string baseName = "\\" + Path.GetFileNameWithoutExtension(file.Name);
                builder.touchedPaths.Add(outputDir.relativePath + baseName + ".png");
                builder.touchedPaths.Add(outputDir.relativePath + baseName + FileExtensions.ATLAS_MAP);
            }
            builder.touchedPaths.Add(outputDir.relativePath + "\\" + file.Name);

            int updateCode = ShouldUpdate(builder, new FileInfo[] { file, atlasPath == null ? new FileInfo(outputDir.directory.FullName +
                "\\" + Path.GetFileNameWithoutExtension(file.Name) + FileExtensions.ATLAS_MAP) : atlasPath }, currentDirectory);

            if (updateCode == ErrorCodes.SKIPPED)
            {
                Program.Logger.Info("Sprite animation already up to date.");
                continue;
            }
            else if (updateCode > ErrorCodes.END_OF_NON_ERRORS)
            {
                return updateCode;
            }

            JsonNode nodeObj;
            try
            {
                nodeObj = JsonNode.Parse(File.ReadAllText(file.FullName), documentOptions: new JsonDocumentOptions() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            } catch (Exception e)
            {
                Throw(outputDir + "\\" + file.Name, e);
                return ErrorCodes.BAD_JSON;
            }
            JsonObject origObj = nodeObj.AsObject();

            if (!contained)
            { //this animation isn't part of an atlasconfig, generate its atlas!
                TexturePacker.Config packerConfig = new TexturePacker.Config();

                string baseName = "\\" + Path.GetFileNameWithoutExtension(file.Name);
                string atlasName = baseName + ".png";
                string mapName = baseName + FileExtensions.ATLAS_MAP;

                packerConfig.OutputAtlasFile = outputDir.directory.FullName + atlasName;
                packerConfig.OutputMapFile = outputDir.directory.FullName + mapName;
                packerConfig.Trim = false; //TODO: Make trim an option in the animation file.
                List<string> inputPaths = new List<string>();
                if (origObj.TryGetPropertyValue("frames", out JsonNode arr))
                {
                    JsonArray frameArray = arr.AsArray();
                    for (int h = 0; h < frameArray.Count; h++)
                    {
                        JsonObject frame = (JsonObject)frameArray[h];
                        if (!frame.TryGetPropertyValue("name", out JsonNode val))
                        {
                            Throw(outputDir + "\\" + file.Name);
                            return ErrorCodes.BAD_JSON;
                        }
                        else
                        {
                            string path = currentDirectory.directory + "\\" + val.ToString();
                            inputPaths.Add(path);
                            builder.excludedFiles.Add(path);
                        }
                    }
                }
                else
                {
                    Throw(outputDir + "\\" + file.Name);
                    return ErrorCodes.BAD_JSON;
                }

                packerConfig.InputPaths = inputPaths;

                Program.Logger.Info("Generating atlas...");
                TexturePacker.Generate(packerConfig);
                atlasPath = new FileInfo(packerConfig.OutputMapFile);
                relativeAtlasPath = outputDir.relativePath + "\\" + Path.GetFileNameWithoutExtension(atlasPath.Name);
                Program.Logger.Info("Atlas generated.");
            }

            origObj.AsObject().Add("atlas", JsonValue.Create(relativeAtlasPath.Substring(builder.TexturesDirectory.Length + 1)));

            //time to convert file names into indices into the atlas.
            AtlasReader atlas = new AtlasReader(atlasPath);
            if (origObj.TryGetPropertyValue("frames", out JsonNode array))
            {
                JsonArray frameArray = array.AsArray();
                foreach (JsonNode node in frameArray)
                {
                    JsonObject frame = node.AsObject();
                    if (!frame.TryGetPropertyValue("name", out JsonNode val))
                    {
                        Throw(outputDir + "\\" + file.Name);
                        return ErrorCodes.BAD_JSON;
                    }
                    string frameName = val.GetValue<string>();
                    val.ReplaceWith(atlas.nameToIndexMap[frameName]);
                }
            }

            //save animation file
            string output = JsonSerializer.Serialize(origObj, _jsonOptions);
            File.WriteAllText(outputDir.directory.FullName + "\\" + file.Name, output);
            Program.Logger.Info("Sprite animation built.");
        }
        return ErrorCodes.NONE;
    }

    private static void Throw(string path, Exception error = null)
    {
        Program.Logger.Error($"Malformed JSON spriteanim at '{path}'! Go fix it.");
        if (error != null)
            Program.Logger.Error(error.Message);
    }

    public int ShouldUpdate(Builder builder, FileInfo[] relevantFiles, RelativeDirectory currentDirectory)
    {
        //output directory should always exist.
        RelativeDirectory outputDir = new RelativeDirectory(currentDirectory.relativePath, builder.TargetDirectory, true);
        FileInfo spriteAnimFile = relevantFiles[0];
        FileInfo outputFile = new FileInfo(outputDir.directory.FullName + "\\" + spriteAnimFile.Name);
        if (!outputFile.Exists || outputFile.LastWriteTimeUtc < spriteAnimFile.LastWriteTimeUtc)
            return ErrorCodes.NONE; //sprite animation file has updated!

        FileInfo atlasFile = relevantFiles[1]; //target atlasmap file. (cannot target png because there might be multiple!!!)
        if (!atlasFile.Exists)
            return ErrorCodes.NONE; //atlas doesn't exist, needs to be created!

        JsonNode nodeObj;
        try
        {
            nodeObj = JsonNode.Parse(File.ReadAllText(spriteAnimFile.FullName), documentOptions: new JsonDocumentOptions() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        } catch (Exception e)
        {
            Throw(currentDirectory + "\\" + spriteAnimFile.Name, e);
            return ErrorCodes.BAD_JSON; //broken json
        }
        JsonObject origObj = nodeObj.AsObject();

        bool needsUpdate = false;
        if (origObj.TryGetPropertyValue("frames", out JsonNode arr))
        {
            JsonArray frameArray = arr.AsArray();
            for (int h = 0; h < frameArray.Count; h++)
            {
                JsonObject frame = (JsonObject)frameArray[h];
                if (!frame.TryGetPropertyValue("name", out JsonNode val))
                {
                    Throw(currentDirectory + "\\" + spriteAnimFile.Name);
                    return ErrorCodes.BAD_JSON;
                }
                FileInfo inputFrame = new FileInfo(currentDirectory.directory + "\\" + val.ToString());
                builder.excludedFiles.Add(inputFrame.FullName);
                if (inputFrame.LastWriteTimeUtc > atlasFile.LastWriteTimeUtc)
                    needsUpdate = true; //sprite update! rebuild atlas and animation!
            }
        }

        return needsUpdate ? ErrorCodes.NONE : ErrorCodes.SKIPPED;
    }


    public static JsonObject CreateSpriteAnimJson(string name, bool loops, bool pingpong, bool reversed, List<(int, int)> frames, string? atlas)
    {
        JsonArray frameArray = new JsonArray();
        for (int i = 0; i < frames.Count; i++)
        {
            JsonObject frameObj = new JsonObject()
            {
                { "name", frames[i].Item1 },
                { "duration", frames[i].Item2 }
            };
            frameArray.Add(frameObj);
        }

        JsonObject outObj = new JsonObject
        {
            { "name", name },
            { "loops", loops },
            { "pingpong", pingpong },
            { "reversed", reversed }
        };
        if (atlas != null)
            outObj.Add("atlas", atlas);

        outObj.Add("frames", frameArray);

        return outObj;
    }
}