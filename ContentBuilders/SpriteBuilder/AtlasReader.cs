using System.Drawing;

namespace Rubedo.Compiler.ContentBuilders.SpriteBuilder
{
    /// <summary>
    /// TODO: I am AtlasReader, and I don't have a summary yet.
    /// </summary>
    public class AtlasReader
    {
        public Dictionary<string, int> nameToIndexMap = new Dictionary<string, int>();
        public Dictionary<int, string> indexToNameMap = new Dictionary<int, string>();

        public AtlasReader(FileInfo atlasMapFile)
        {
            using (TextReader file = new StreamReader(atlasMapFile.OpenRead()))
            {
                int lineNumber = 0;
                while (true)
                {
                    string? lineRead = file.ReadLine();
                    if (lineRead == null)
                    {
                        break;
                    }
                    ReadOnlySpan<char> line = lineRead.AsSpan();
                    Range[] ranges = new Range[8];
                    Span<Range> sections = new Span<Range>(ranges); //doing it with spans to save memory - no need to allocate more than the line!
                    line.Split(sections, ',');

                    string name = line[sections[0]].ToString();
                    nameToIndexMap.Add(name, lineNumber);
                    indexToNameMap.Add(lineNumber, name);

                    lineNumber++;
                }
            }
        }
    }
}