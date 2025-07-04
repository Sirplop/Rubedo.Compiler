using System.IO;

namespace Rubedo.Compiler.Util
{
    public class RelativeDirectory
    {
        public string relativePath;
        public DirectoryInfo directory;

        public RelativeDirectory(string path, string relativeTo, bool relative)
        {
            if (relative)
            {
                directory = new DirectoryInfo(Path.Combine(relativeTo, path));
                relativePath = path;
            } else
            {
                directory = new DirectoryInfo(path);
                relativePath = Path.GetRelativePath(relativeTo, path);
            }
        }

        public override string ToString()
        {
            return relativePath;
        }

        public override bool Equals(object obj)
        {
            return obj is RelativeDirectory dir && dir.relativePath.Equals(relativePath);
        }

        public override int GetHashCode()
        {
            return relativePath.GetHashCode();
        }
    }
}