using Rubedo.Compiler.Util;

namespace Rubedo.Compiler.ContentBuilders
{
    /// <summary>
    /// A virtual file, with a given way to load the "file"'s data.
    /// </summary>
    public class VirtualFile<T>
    {
        public string Name { get; set; }

        public long LastWriteTimeUtc { get; set; }

        private Func<T> loadFunc;

        public VirtualFile(string name, long lastModified, Func<T> loadFunc) 
        {
            Name = name;
            LastWriteTimeUtc = lastModified;
            this.loadFunc = loadFunc;
        }

        public T Load()
        {
            return loadFunc();
        }
    }
}