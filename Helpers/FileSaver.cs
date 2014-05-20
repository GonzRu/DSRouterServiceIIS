using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace DSFakeService.Helpers
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