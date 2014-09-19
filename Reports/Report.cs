using System;
using System.IO;

namespace Reports
{
    public class Report
    {
        #region Consts

        private readonly string PATH_TO_REPORTS_TEMPLATES;

        #endregion

        #region Protected fields

        /// <summary>
        /// Источник данных
        /// </summary>
        protected object _dataSource = null;


        #region Constructor

        public Report(string reportTemplateName)
        {
            _reportTemplateName = reportTemplateName;

            PATH_TO_REPORTS_TEMPLATES = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Project",
                "ReportsTemplates");
        }

        #endregion

        #region Public metods

        /// <summary>
        /// Установить источник данных
        /// </summary>
        /// <param name="dataSource"></param>
        public void SetDataSource(object dataSource)
        {
            _dataSource = dataSource;
        }

        /// <summary>
        /// Получить массив содержимое отчета в виде массива байтов
        /// </summary>
        public byte[] GetReportFileContent(string reportFileName, string reportFileExtension)
        {
            return null;
        }

        /// <summary>
        /// Сохранить отчет
        /// </summary>
        /// <param name="path">Путь к папке, куда надо сохранить отчет</param>
        /// <param name="reportFileName">Имя отчета</param>
        /// <param name="reportFileExtension">Расширение отчета</param>
        public void SaveReportFile(string path, string reportFileName, string reportFileExtension)
        {
        }

        #endregion

        #region Private metods

        /// <summary>
        /// Проверяет - задан ли источник данных
        /// </summary>
        private void CheckDataSource()
        {
            if (_dataSource == null)
                throw new Exception("Источник данных не задан.");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="reportFileExtension"></param>
        /// <returns></returns>
        private ExportFormatType GetExportFormatType(string reportFileExtension)
        {
            switch (reportFileExtension.ToLower())
            {
                case "pdf":
                    return ExportFormatType.PortableDocFormat;
                case "doc":
                    return ExportFormatType.WordForWindows;
                case "xls":
                    return ExportFormatType.Excel;
                case "xlsx":
                    return ExportFormatType.ExcelWorkbook;
            }

            throw new ArgumentException("Данный формат не поддерживается.");
        }

        #endregion
    }
}
