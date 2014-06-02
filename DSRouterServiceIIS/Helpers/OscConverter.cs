using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DSFakeService.DSServiceReference;

namespace DSFakeService.Helpers
{
    public static class OscConverter
    {
        #region CONSTS

        /// <summary>
        /// Шаблон имени файла, который будет на выходе
        /// </summary>
        private const string RESULT_ZIP_FILE_NAME = "{0}____{1}____{2}.zip";

        /// <summary>
        /// Шаблон имени файла осциллограммы, которая состояит из одной части
        /// </summary>
        private const string OSC_FILE_NAME_WITHOUT_PARTS = "{0}____{1}____{2}{3}";

        /// <summary>
        /// Шаблон имени файла осциллограммы, которая состояит из нескольких частей
        /// </summary>
        private const string OSC_FILE_NAME_WITH_PARTS = "{0}____{1}____{2}_part-{3}{4}";

        private const string DARE_TOSTRING_TEMPLATE = "";

        #endregion

        #region Public - методы

        /// <summary>
        /// Сохраняет осциллограмму на диск, архивирует её и возвращает ссылку на неё
        /// </summary>
        public static string SaveOscillogrammToFile(string pathToSave, DSOscillogram dsOscillogram)
        {
            string oscDate = dsOscillogram.Date.ToString("yy-MM-dd");

            // Создаем временную директорию
            string pathToTempDirectory = GetTemporaryDirectory();
            // Имя конечного zip-архива
            string resultZipFileName = String.Format(RESULT_ZIP_FILE_NAME, dsOscillogram.SourceName, dsOscillogram.SourceComment, oscDate);
            // Путь до конечного zip-архива
            string pathToResultZipFile = Path.Combine(pathToSave, resultZipFileName);
            // Определяем необходимое расширение для оцсциллограммы
            string oscFileExtension = GetOscExtensionByOscType(dsOscillogram.OscillogramType);

            int i = 1;
            foreach (var oscContent in dsOscillogram.Content)
            {
                string oscFileName;
                if (dsOscillogram.Content.Count() == 1)
                    oscFileName = String.Format(OSC_FILE_NAME_WITHOUT_PARTS,
                        dsOscillogram.SourceName,
                        dsOscillogram.SourceComment,
                        oscDate,
                        oscFileExtension);
                else
                    oscFileName = String.Format(OSC_FILE_NAME_WITH_PARTS,
                        dsOscillogram.SourceName,
                        dsOscillogram.SourceComment,
                        oscDate,
                        i,
                        oscFileExtension);

                FileSaver.SaveFile(pathToTempDirectory, oscFileName, oscContent);

                i++;
            }

            if (File.Exists(pathToResultZipFile))
                File.Delete(pathToResultZipFile);

            ZipFile.CreateFromDirectory(pathToTempDirectory, pathToResultZipFile, CompressionLevel.Optimal, false, System.Text.Encoding.GetEncoding("cp866"));

            Directory.Delete(pathToTempDirectory, true);

            return resultZipFileName;
        }

        #endregion

        #region Private - методы

        /// <summary>
        /// Создает папку со случайным именем и возвращает путь до нее
        /// </summary>
        public static string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        /// <summary>
        /// Возвращает необходимое для файла осциллограммы расширение
        /// </summary>
        private static string GetOscExtensionByOscType(DSEventType oscillogramType)
        {
            switch (oscillogramType)
            {
                case DSEventType.Diagramm:
                    return ".dgm";
                case DSEventType.Oscillogram:
                    return ".osc";
                case DSEventType.OscillogramSirius2:
                    return ".trd";
                case DSEventType.OscillogramEkra:
                    return ".dfr";
                case DSEventType.OscillogramBresler:
                    return ".zbrs";
            }

            throw new Exception("Для данного типа формат не предусмотрен");
        }

        #endregion
    }
}