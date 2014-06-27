using System;
using System.IO;

namespace DSRouterServiceIIS.Helpers
{
    public static class FileSaver
    {
        public static string SaveFile(string pathToSave, string fileName, byte[] content)
        {
            if (!Directory.Exists(pathToSave))
                throw new ArgumentException();

            string pathToFile = Path.Combine(pathToSave, fileName);

            if (File.Exists(pathToFile))
                File.Delete(pathToFile);

            FileStream fileStream = File.Create(pathToFile);
            fileStream.Write(content, 0, content.Length);
            fileStream.Close();

            return fileName;
        }
    }
}