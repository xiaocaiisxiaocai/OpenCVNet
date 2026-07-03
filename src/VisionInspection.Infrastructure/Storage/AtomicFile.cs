using System.IO;
using System.Text;

namespace VisionInspection.Infrastructure.Storage
{
    public static class AtomicFile
    {
        public static void WriteText(string path, string content, bool backup)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                w.Write(content);
                w.Flush();
                fs.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(tmp, path, backup ? path + ".bak" : null);
            else
                File.Move(tmp, path);
        }
    }
}
