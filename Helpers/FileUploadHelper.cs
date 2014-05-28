using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Web;
using DSFakeService.DataSources;
using DSFakeService.DSServiceReference;

namespace DSRouterService.Helpers
{
    public class FileUploadHelper
    {
        #region Private Fields

        private readonly Object _lockObject = new object();

        /// <summary>
        /// Информация о передаваемом файле
        /// </summary>
        private readonly Dictionary<Object, Tuple<UInt16, Int32, string, string>>  fileUploadSessions;

        /// <summary>
        /// Источник данных, с помощью которого будет производиться передача файлов
        /// </summary>
        private readonly DataServersCollector _dataSource;

        #endregion

        #region Constructor

        public FileUploadHelper(DataServersCollector dataSource)
        {
            _dataSource = dataSource;
            fileUploadSessions = new Dictionary<object, Tuple<ushort, int, string, string>>();
        }

        #endregion

        #region Public-metods

        /// <summary>
        /// Пытается получить доступ к загрузке файла на DS
        /// </summary>
        public bool TryInitFileUploadSession(UInt16 dsGuid, Int32 devGuid, string fileName, string fileComment, object lockObject)
        {
            lock (_lockObject)
            {
                // Если можем инициировать передачу файла
                if (!CanInitUploadFileSession(dsGuid, lockObject))
                    return false;

                // Если такой DS вообще есть
                if (GetDsProxyByGuid(dsGuid) == null)
                    return false;

                InitUploadFileSession(dsGuid, devGuid, fileName, fileComment, lockObject);

                return true;
            }
        }

        /// <summary>
        /// Загрузить часть файла на DS
        /// </summary>
        /// <param name="fileChunkBytes"></param>
        /// <param name="lockObject"></param>
        /// <returns></returns>
        public bool UploadFileChunk(byte[] fileChunkBytes, object lockObject)
        {
            if (!IsHaveAcceess(lockObject))
                return false;

            var dsProxy = GetDsProxyByLockObject(lockObject);

            try
            {
                lock (dsProxy)
                {
                    dsProxy.UploadFileChunk(fileChunkBytes);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("FileUploadhelper:UploadFileChunk() : исключение " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Сохранить загруженный файл
        /// </summary>
        public string SaveUploadedFile(object lockObject, Int32 userId)
        {
            if (!IsHaveAcceess(lockObject))
                return "Сервер данных сейчас занят.";

            var dsProxy = GetDsProxyByLockObject(lockObject);

            try
            {
                var uploadedFileInfo = fileUploadSessions[lockObject];
                var devGuid = uploadedFileInfo.Item2;
                var fileName = uploadedFileInfo.Item3;
                var fileComment = uploadedFileInfo.Item4;

                lock (dsProxy)
                {
                    dsProxy.SaveUploadedFile(devGuid, userId, fileName, fileComment);
                }

                RemoveFileUploadSessionLockObject(lockObject);

                return "Файл успешно записан.";
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("FileUploadhelper:SaveUploadedFile() : исключение " + ex.Message);
            }

            return "Во время записи файла произошла ошибка.";
        }

        /// <summary>
        /// Отменить передачу файла
        /// </summary>
        public void TerminateUploadFileSession(object lockObject)
        {
            CloseUploadFileSession(lockObject);

            RemoveFileUploadSessionLockObject(lockObject);
        }

        #endregion

        #region Private-metods

        /// <summary>
        /// Проверяет, можем ли мы инициировать сессию передачи файлов
        /// </summary>
        private bool CanInitUploadFileSession(UInt16 dsGuid, object lockObject)
        {
            lock (_lockObject)
            {
                if (fileUploadSessions.ContainsKey(lockObject))
                    return false;

                var dsInUseKeyValuePair = fileUploadSessions.SingleOrDefault(pair => pair.Value.Item1 == dsGuid);
                
                // Если данный DS не заблокирован, то можно инициировать передачу файлов
                if (dsInUseKeyValuePair.Equals(default(KeyValuePair<object, Tuple<UInt16, Int32, string, string>>)))
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Создать сессию передачи файла
        /// </summary>
        private void InitUploadFileSession(UInt16 dsGuid, Int32 devGuid, string fileName, string fileComment, object lockObject)
        {
            lock (_lockObject)
            {
                fileUploadSessions.Add(lockObject, new Tuple<ushort, int, string, string>(dsGuid, devGuid, fileName, fileComment));
            }
        }

        /// <summary>
        /// Определяет, имеет ли данный объект право на текущую сессию передачи файла
        /// </summary>
        private bool IsHaveAcceess(object lockObject)
        {
            lock (_lockObject)
            {
                return fileUploadSessions.ContainsKey(lockObject);
            }
        }

        /// <summary>
        /// Получить проксе DS по lockObject
        /// </summary>
        private IWcfDataServer GetDsProxyByGuid(UInt16 dsGuid)
        {
            return ((IDataSource)_dataSource).GetDsProxy(dsGuid);
        }

        /// <summary>
        /// Получить проксе DS по lockObject
        /// </summary>
        private IWcfDataServer GetDsProxyByLockObject(object lockObject)
        {
            UInt16 dsGuid;

            lock (_lockObject)
            {
                if (!fileUploadSessions.ContainsKey(lockObject))
                    return null;

                dsGuid = fileUploadSessions[lockObject].Item1;
            }

            return ((IDataSource) _dataSource).GetDsProxy(dsGuid);
        }

        /// <summary>
        /// Закрывает сессию передачи файла на DS
        /// </summary>
        private void CloseUploadFileSession(object lockObject)
        {
            var dsProxy = GetDsProxyByLockObject(lockObject);

            if (dsProxy == null)
                return;

            try
            {
                lock (dsProxy)
                {
                    dsProxy.TerminateUploadFileSession();
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("FileUploadhelper:CloseUploadFileSession() : исключение " + ex.Message);
            }
        }

        /// <summary>
        /// Удаляет блокирующий объект сессии передачи файла
        /// </summary>
        private void RemoveFileUploadSessionLockObject(object lockObject)
        {
            lock (_lockObject)
            {
                if (fileUploadSessions.ContainsKey(lockObject))
                    fileUploadSessions.Remove(lockObject);
            }
        }

        #endregion
    }
}