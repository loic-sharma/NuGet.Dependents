using System;
using System.IO;

namespace Intern
{
    internal class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.GetTempFileName();

            File.Delete(Path);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                var directory = new DirectoryInfo(Path);
                foreach (var file in directory.EnumerateFiles()) file.Delete();
                foreach (var dir in directory.EnumerateDirectories()) dir.Delete(recursive: true);
            }
            catch
            {
                // TODO: remove this
            }
        }
    }
}
