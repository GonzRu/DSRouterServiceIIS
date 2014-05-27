using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using DSFakeService.DataSources;

namespace DSRouterService.Helpers
{
    public class FileUploadHelper
    {
        #region Private Fields

        private Object _lockObject = new object();

        /// <summary>
        /// Словарь заблокирвоанных для загрузки файлов DS с объектом блокирования
        /// </summary>
        private Dictionary<UInt16, Object> _lockedDsToFileUpload;

        /// <summary>
        /// Информация о передаваемом файле
        /// </summary>
        private Dictionary<UInt16, Tuple<Int32, string, string>>  _fileUploadInfo;

        /// <summary>
        /// Источник данных, с помощью которого будет производиться передача файлов
        /// </summary>
        private readonly DataServersCollector _dataSource;

        #endregion

        #region Constructor

        public FileUploadHelper(DataServersCollector dataSource)
        {
            _dataSource = dataSource;
        }

        #endregion

        #region Public-metods

        /// <summary>
        /// Пытается получить доступ к загрузке файла на DS
        /// </summary>
        public bool TryInitFileUploadSession(UInt16 dsGuid, Int32 devGuid, string fileName, string fileComment, object lockObject)
        {
            throw new NotImplementedException();
        }

        public bool UploadFileChunk(byte[] fileChunkBytes, object lockObject)
        {
            throw new NotImplementedException();
        }

        public string SaveUploadedFile(object lockObject)
        {
            throw new NotImplementedException();
        }

        public void TerminateUploadFileSession(object lockObject)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}