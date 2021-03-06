﻿using System;
using System.Data;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Xml.Linq;
using DSRouterServiceIIS.DataSources;
using DSRouterServiceIIS.DSServiceReference;
using DSRouterServiceIIS.Helpers;
using HMI_MT_Settings;
using Reports;

namespace DSRouterServiceIIS
{
    //[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]    
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession)]
    public class DSRouterService : IDSRouter
    {
        #region Readonly static fields

        /// <summary>
        /// Путь до папки, в которой надо сохранять файлы
        /// </summary>
        private static readonly string DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES;

        /// <summary>
        /// Путь до папки, в которой надо сохранять файлы
        /// </summary>
        private static readonly string DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES;

        #endregion

        #region static fields

        /// <summary>
        /// объект блокировки
        /// </summary>
        private static object lockKey = new object();

        private static string _lockFileUploadSessionId = null;

        /// <summary>
        /// Содержит всю логику получения текущих данных ото всех DS
        /// </summary>
        private static IDataSource _dataSource;

        /// <summary>
        /// Помошник загрузки файлов на DS
        /// </summary>
        private static FileUploadHelper _fileUploadHelper;

        #endregion

        #region private-поля класса

        /// <summary>
        /// список DS и описывающих их секций
        /// </summary>
        private Dictionary<UInt16, XElement> dsList;

        /// <summary>
        /// список соответсвия клиентов и guids DataServer's
        /// </summary>
        private Dictionary<UInt16, DSService> dWCFClientsList = new Dictionary<ushort, DSService>();

        /// <summary>
        /// Текущий контекст
        /// </summary>
        private OperationContext CurrentСontext;

        /// <summary>
        /// признак активной команды
        /// </summary>
        private bool IsCMDActive { get; set; }

        #region Поля, необходимые для сохранения данных при работе сессии

        /// <summary>
        /// Callback текущего клиента
        /// </summary>
        private IDSRouterCallback currClient;

        /// <summary>
        /// Класс пользователя, который обслуживается текущей сессией
        /// </summary>
        private RouterAuthResult _authResult;

        /// <summary>
        /// Идентификатор сессии
        /// </summary>
        private readonly string _sessionId;

        #endregion

        #endregion

        #region Конструктор

        static DSRouterService()
        {
            InitRouterService();

            TraceSourceLib.TraceSourceDiagMes.StartTrace("AppDiagnosticLog", 30000);

            DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Share");
            DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES = "/Share/";

            try
            {
                if (Directory.Exists(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES))
                    Directory.Delete(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES, true);

                Directory.CreateDirectory(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("Исключение при удалении или создании папки share : " + ex.Message);
            }
        }

        public DSRouterService()
        {
            try
            {
                dWCFClientsList = (_dataSource as DataServersCollector).dWCFClientsList;

                OperationContext.Current.Channel.Closed += ClientDisconnected;
                OperationContext.Current.Channel.Faulted += ClientDisconnected;

                _sessionId = OperationContext.Current.SessionId;

                Log.WriteDebugMessage("DSRouterService: Создан объект DSRouterService");
            }
            catch (Exception ex)
            {
                Log.WriteCriticalMessage("DSRouterService.DSRouterService() : Исключение : " + ex.Message);
            }
        }

        ~DSRouterService()
        {
            Log.WriteDebugMessage("DSRouterService: Объект DSRouterService уничтожен");
        }

        #endregion

        #region Implementation IDSRouter

        #region Старые функции

        #region поддержка функционирования старого арма - обмен пакетами

        /// <summary>
        /// запрос данных в виде пакета
        /// </summary>
        /// <param name="arr"></param>
        /// <returns></returns>
        byte[] IDSRouter.GetDSValueAsByteBuffer(UInt16 DSGuid, byte[] arr)
        {
            MemoryStream rez = new MemoryStream();
            try
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 217, string.Format("{0} : Получен запрос от HMI-клиента для DS {1} длина= {2} байт.", DateTime.Now.ToString(), DSGuid, arr.Length));

                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;

                const int bytesread = 16000;

                if (arr.Length > bytesread)
                {
                    int cc = arr.Length / bytesread;
                    // остаток
                    int cd = arr.Length % bytesread;
                    /*
                     * корректируем число фрагментов 
                     * с учетом остатка
                     */
                    if (cd > 0)
                        cc++;

                    int srcoffset = 0;
                    int sizepart = 0;
                    int numpart = 1;

                    byte[] bytes4requst;

                    while (srcoffset < arr.Length)
                    {
                        if ((arr.Length - srcoffset) > bytesread)
                        {
                            sizepart = bytesread;
                            bytes4requst = new byte[bytesread];
                            Buffer.BlockCopy(arr, srcoffset, bytes4requst, 0, bytesread);
                            rez = iwds.GetDSValueAsPartialByteBuffer(bytes4requst, numpart, cc);
                            srcoffset += bytesread;
                            numpart++;
                        }
                        else
                        {
                            int ost = arr.Length - srcoffset;
                            bytes4requst = new byte[ost];
                            Buffer.BlockCopy(arr, srcoffset, bytes4requst, 0, ost);
                            rez = iwds.GetDSValueAsPartialByteBuffer(bytes4requst, numpart, cc);
                            srcoffset += ost;
                        }
                    }
                }
                else
                    rez = iwds.GetDSValueAsByteBuffer(arr);

                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 251, string.Format("{0} : Отправлено по запросу DS {1} длина= {2}  байт.", DateTime.Now.ToString(), DSGuid, rez.Length));
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSValueAsByteBuffer() : Исключение : " + ex.Message);
            }
            rez.Position = 0;
            return rez.ToArray();
        }

        /// <summary>
        /// запрос осциллограммы по ее 
        /// id в базе
        /// </summary>
        byte[] IDSRouter.GetDSOscByIdInBD(UInt16 DSGuid, byte[] arr)
        {
            MemoryStream rez = new MemoryStream();
            try
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1302, string.Format("{0} : Получен запрос от HMI-клиента для DS {1} длина= {2} байт.", DateTime.Now.ToString(), DSGuid, arr.Length));

                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;

                lock (lockKey)
                {
                    rez = iwds.GetDSOscByIdInBD(arr);
                }

                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1302, string.Format("{0} : Отправлено по запросу DS {1} длина= {2}  байт.", DateTime.Now.ToString(), DSGuid, rez.Length));
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSOscByIdInBD() : Исключение : " + ex.Message);
            }
            rez.Position = 0;
            return rez.ToArray();
        }

        /// <summary>
        /// запрос архивной по ее 
        /// id в базе
        /// </summary>
        void IDSRouter.SetReq2ArhivInfo(UInt16 DSGuid, byte[] arr)
        {
            MemoryStream rez = new MemoryStream();
            try
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1302, string.Format("{0} : Получен запрос от HMI-клиента для DS {1} длина= {2} байт.", DateTime.Now.ToString(), DSGuid, arr.Length));

                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                lock (lockKey)
                {
                    iwds.SetReq2ArhivInfo(arr);
                }

                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1302, string.Format("{0} : Отправлено по запросу DS {1} длина= {2}  байт.", DateTime.Now.ToString(), DSGuid, rez.Length));
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.SetReq2ArhivInfo() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// выполнить команду
        /// </summary>
        byte[] IDSRouter.RunCMDMOA(UInt16 DSGuid, byte[] arr)
        {
            MemoryStream rez = new MemoryStream();
            try
            {
                IsCMDActive = true;

                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1385, string.Format("{0} : Получен запрос от HMI-клиента для DS {1} длина= {2} байт.", DateTime.Now.ToString(), DSGuid, arr.Length));

                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                rez = iwds.RunCMDMOA(arr);
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1389, string.Format("{0} : Отправлено по запросу DS {1} длина= {2}  байт.", DateTime.Now.ToString(), DSGuid, rez.Length));
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.RunCMDMOA() : Исключение : " + ex.Message);
            }
            rez.Position = 0;
            return rez.ToArray();// as Stream;
        }

        /// <summary>
        /// выполнить команду по ее TagGUID
        /// </summary>
        /// <param name="dsdevTagGUID">ид команды в формате ds.dev.tagguid</param>
        /// <param name="pq">массив параметров</param>
        /// <returns>успешность запуска команды</returns>
        bool IDSRouter.RunCMD(string dsdevTagGUID, byte[] pq)
        {
            bool rez = false;
            try
            {
                IsCMDActive = true;

                string[] arrcmdid = dsdevTagGUID.Split(new char[] { '.' });
                UInt16 ds = UInt16.Parse(arrcmdid[0]);
                uint dev = UInt32.Parse(arrcmdid[1]);
                uint tagguid = UInt32.Parse(arrcmdid[2]);

                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1385, string.Format("{0} : Получен запрос на команду от HMI-клиента для {1}.", DateTime.Now.ToString(), dsdevTagGUID));

                IWcfDataServer iwds = dWCFClientsList[ds].wcfDataServer;
                iwds.RunCMD(ds, dev, tagguid, pq);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.RunCMD() : Исключение : " + ex.Message);
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }

            return rez;
        }

        #endregion поддержка функционирования старого арма - обмен пакетами

        #region Получение данных о конфигурации

        /// <summary>
        /// информация о конфигурации DataServer
        /// в виде файла
        /// </summary>       
        Stream IDSRouter.GetDSConfigFile(UInt16 DSGuid)
        {
            Stream fst = null;
            FileStream fstr = null;
            try
            {
                string path2 = AppDomain.CurrentDomain.BaseDirectory + "ds.cfg";

                using (Stream fstream = new FileStream(path2, FileMode.OpenOrCreate))
                {
                    IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                    TextWriter tw = new StreamWriter(fstream);
                    fst = iwds.GetDSConfigFile() as MemoryStream;
                    fst.CopyTo(fstream);
                }

                fstr = new FileStream(path2, FileMode.Open);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSConfigFile() : Исключение : " + ex.Message);
            }
            return fstr;
        }

        /// <summary>
        /// Информация об DataServer’ах
        /// </summary>     
        string IDSRouter.GetDSGUIDs()
        {
            StringBuilder strb = new StringBuilder();

            try
            {
                foreach (KeyValuePair<UInt16, XElement> kvp in dsList)
                    strb.Append(kvp.Key.ToString() + ";");
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSGUIDs() : Исключение : " + ex.Message);
            }
            return strb.ToString().Remove(strb.Length - 1);
        }

        /// <summary>
        /// информация об имени DataServer 
        /// </summary>
        string IDSRouter.GetDSINFO(UInt16 DSGuid)
        {
            String str = "1";

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetDSINFO();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSINFO() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список идентификаторов источников
        /// указанного DataServer 
        /// </summary>
        string IDSRouter.GetSourceGUIDs(UInt16 DSGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetSourceGUIDs();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetSourceGUIDs() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// возвращает имя указанного 
        /// источника указанного DataServer
        /// </summary>
        string IDSRouter.GetSourceName(UInt16 DSGuid, UInt16 SrcGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetSourceName(SrcGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetSourceName() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список идентификаторов контроллеров 
        /// источника SrcGuid DataServer DSGuid 
        /// в формате EcuGuid; EcuGuid; EcuGuid
        /// </summary>
        string IDSRouter.GetECUGUIDs(UInt16 DSGuid, UInt16 SrcGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetECUGUIDs(SrcGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetECUGUIDs() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// возвращает имя контролллера EcuGuid 
        /// источника SrcGuid для  DataServer DSGuid
        /// </summary>
        string IDSRouter.GetECUName(UInt16 DSGuid, UInt16 SrcGuid, UInt16 EcuGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetECUName(SrcGuid, EcuGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetECUName() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список идентификаторов устройств 
        /// контроллера EcuGuid источника SrcGuid DataServer DSGuid 
        /// в формате RtuGuid; RtuGuid; RtuGuid…
        /// </summary>     
        string IDSRouter.GetSrcEcuRTUGUIDs(UInt16 DSGuid, UInt16 SrcGuid, UInt16 EcuGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetSrcEcuRTUGUIDs(SrcGuid, EcuGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetSrcEcuRTUGUIDs() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список идентификаторов устройств DataServer
        /// DSGuid в формате RtuGuid; RtuGuid; RtuGuid…
        /// </summary>
        string IDSRouter.GetRTUGUIDs(UInt16 DSGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetRTUGUIDs();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetRTUGUIDs() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// признак доступности устройства для обработки
        /// </summary>
        bool IDSRouter.IsRTUEnable(UInt16 DSGuid, UInt32 RtuGuid)
        {
            bool rez = false;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                rez = iwds.IsRTUEnable(RtuGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.IsRTUEnable() : Исключение : " + ex.Message);
            }
            return rez;
        }

        /// <summary>
        /// строка описания устройства
        /// </summary>       
        string IDSRouter.GetRTUDescription(UInt16 DSGuid, UInt32 RtuGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetRTUDescription(RtuGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetRTUDescription() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список групп первого уровня 
        /// в формате GroupGuid; GroupGuid; GroupGuid…
        /// </summary>
        string IDSRouter.GetGroupGUIDs(UInt16 DSGuid, UInt32 RtuGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetGroupGUIDs(RtuGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetGroupGUIDs() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// признак доступности группы для обработки.
        /// </summary>
        bool IDSRouter.IsGroupEnable(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid)
        {
            bool rez = false;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                rez = iwds.IsGroupEnable(RtuGuid, GroupGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.IsGroupEnable() : Исключение : " + ex.Message);
            }
            return rez;
        }

        /// <summary>
        /// имя группы GroupGuid
        /// устройства RtuGuid
        /// </summary>
        string IDSRouter.GetRTUGroupName(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetRTUGroupName(RtuGuid, GroupGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetRTUGroupName() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список подгрупп группы(подгруппы) 
        /// (Sub)GroupGuid в формате 
        /// SubGroupGuid; SubGroupGuid; SubGroupGuid…
        /// </summary>
        string IDSRouter.GetSubGroupGUIDsInGroup(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetSubGroupGUIDsInGroup(RtuGuid, GroupGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetSubGroupGUIDsInGroup() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// список тегов группы GroupGuid 
        /// устройства RtuGuid в формате 
        /// TagGuid; TagGuid; TagGuid…
        /// </summary>
        string IDSRouter.GetRtuGroupTagGUIDs(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetRtuGroupTagGUIDs(RtuGuid, GroupGuid);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetRtuGroupTagGUIDs() : Исключение : " + ex.Message);
            }
            return str;
        }

        /// <summary>
        /// информация о имени и типе тега 
        /// в формате имя_тега;тип_тега
        /// </summary>
        string IDSRouter.GetRTUTagName(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid, UInt32 TagGUID)
        {
            String str = string.Empty;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                str = iwds.GetRTUTagName(RtuGuid, TagGUID);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetRTUTagName() : Исключение : " + ex.Message);
            }
            return str;
        }

        #endregion  Получение данных о конфигурации

        #region функции общего назначения

        #region код последней ошибки

        /// <summary>
        /// получить список последних ошибок при обмене с DS.
        /// Формат кода ошибки: codeerror@timestamp 
        /// </summary>
        LstError IDSRouter.GetDSLastErrorsGUID(UInt16 DSGuid)
        {
            LstError rez = null;

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                rez = iwds.GetDSLastErrorsGUID();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSLastErrorsGUID() : Исключение : " + ex.Message);
            }
            return rez;
        }

        /// <summary>
        /// получить код последней ошибки при обмене с DS
        /// </summary>
        string IDSRouter.GetDSLastErrorGUID(UInt16 DSGuid)
        {
            string rez = "0";

            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                rez = iwds.GetDSLastErrorGUID();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSLastErrorGUID() : Исключение : " + ex.Message);
            }
            return rez;
        }

        /// <summary>
        /// получить код последней ошибки 
        /// при обмене с DS по ее коду
        /// </summary>
        string IDSRouter.GetDSErrorTextByErrorGUID(UInt16 DSGuid, string lastErrorGUID)
        {
            string rez = string.Empty;
            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                rez = iwds.GetDSErrorTextByErrorGUID(lastErrorGUID);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.GetDSErrorTextByErrorGUID() : Исключение : " + ex.Message);
            }
            return rez;
        }

        /// <summary>
        /// квитировать (очистить) стек ошибок
        /// </summary>
        void IDSRouter.AcknowledgementOfErrors(UInt16 DSGuid)
        {
            try
            {
                IWcfDataServer iwds = dWCFClientsList[DSGuid].wcfDataServer;
                iwds.AcknowledgementOfErrors();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.AcknowledgementOfErrors() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// регистрация клиента для 
        /// процесса оповещения о новой ошибке
        /// </summary>
        void IDSRouter.RegisterForErrorEvent(string keyticker)
        {
            try
            {
                currClient = OperationContext.Current.GetCallbackChannel<IDSRouterCallback>();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.WriteErrorMessage("DSRouterService.RegisterForErrorEvent() : Исключение : " + ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// Функция для проверки состояния связи
        /// </summary>
        void IDSRouter.Ping()
        {
            Log.LogTrace("DSRouterService : Ping() от клиента HMI");
            try
            {
                lock (lockKey)
                {
                    currClient.Pong();
                    Log.LogTrace("DSRouterService : Pong() клиенту HMI");
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 127, string.Format("{0} : {1} : {2} : ОШИБКА: {3}", DateTime.Now.ToString(), @"X:\Projects\00_DataServer\DSRouterService\DSRouterService\DSRouterService.svc.cs", "Ping()()", ex.Message));
                Log.WriteErrorMessage("DSRouterService.Ping() : Исключение : " + ex.Message);
            }
        }

        #endregion

        #endregion

        #region Текущие значения

        /// <summary>
        /// Запрос всех тегов и одновременно подписка на изменения тегов
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDSRouter.GetTagsValue(List<string> ATagIDsList)
        {
            return _dataSource.GetTagsValue(_sessionId, ATagIDsList);
        }

        /// <summary>
        /// Запрос на получение тегов, чьи значения изменились
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDSRouter.GetTagsValuesUpdated()
        {
            return _dataSource.GetTagsValuesUpdated(_sessionId);
        }

        #endregion

        #region Работа с пользователем

        /// <summary>
        /// Метод авторизации пользователя
        /// </summary>
        RouterAuthResult IDSRouter.Authorization(string userName, string userPassword, Boolean isFirstEnter)
        {
            var routerAuthResult = new RouterAuthResult();
            routerAuthResult.DSAuthResults = new Dictionary<ushort, DSRouterAuthResult>();

            #warning Use dWCFClientsList
            foreach (var dsGuid in dWCFClientsList.Keys)
            {
                var dsProxy = _dataSource.GetDsProxy(dsGuid);

                try
                {
                    var dsAuthResult = new DSServiceReference.DSAuthResult();
                    lock (dsProxy)
                    {
                        dsAuthResult = dsProxy.Authorization(userName, userPassword, isFirstEnter, CreateDsUserSessionInfoForAuthorization());                        
                    }

                    var dsRouterAuthResult = new DSRouterAuthResult(dsAuthResult);
                    dsRouterAuthResult.UserName = userName;
                    dsRouterAuthResult.UserPassword = userPassword;
                    routerAuthResult.DSAuthResults.Add(dsGuid, dsRouterAuthResult);
                }
                catch (Exception ex)
                {
                    routerAuthResult.DSAuthResults.Add(dsGuid, new DSRouterAuthResult {AuthResult = AuthResult.NoConnectionToDs});
                }
            }

            _authResult = routerAuthResult;

            return routerAuthResult;
        }

        /// <summary>
        /// Получить список пользователей
        /// </summary>
        List<DSRouterUser> IDSRouter.GetUsersList()
        {
            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    List<DSUser> users;
                    lock (dataServerProxy)
                    {
                        users = dataServerProxy.GetUsersList();
                    }

                    if (users == null)
                        return null;

                    List<DSRouterUser> dsRouterUsers = new List<DSRouterUser>();
                    foreach (var dsUser in users)
                        dsRouterUsers.Add(new DSRouterUser(dsUser));

                    return dsRouterUsers;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetUsersList() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить список групп пользователей
        /// </summary>
        List<DSRouterUserGroup> IDSRouter.GetUserGroupsList()
        {
            if (_authResult == null)
                return null;

            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    List<DSUserGroup> userGroups;
                    lock (dataServerProxy)
                    {
                        userGroups = dataServerProxy.GetUserGroupsList();
                    }

                    if (userGroups == null)
                        return null;

                    List<DSRouterUserGroup> dsRouterUserGroups = new List<DSRouterUserGroup>();
                    foreach (var dsUserGroup in userGroups)
                        dsRouterUserGroups.Add(new DSRouterUserGroup(dsUserGroup));

                    return dsRouterUserGroups;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetUserGroupsList() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Создание группы пользователей
        /// </summary>
        bool IDSRouter.CreateUserGroup(string groupName, string groupComment, string groupRight)
        {
            if (_authResult == null)
                return false;

            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    lock (dataServerProxy)
                    {
                        return dataServerProxy.CreateUserGroup(groupName, groupComment, groupRight, CreateDsUserSessionInfo(0));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.CreateUserGroup() : Исключение : " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Создание пользователя
        /// </summary>
        bool IDSRouter.CreateUser(string userName, string userPassword, string userComment, int userGroupID)
        {
            if (_authResult == null)
                return false;

            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    lock (dataServerProxy)
                    {
                        return dataServerProxy.CreateUser(userName, userPassword, userComment, userGroupID, CreateDsUserSessionInfo(0));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.CreateUser() : Исключение : " + ex.Message);
            }

            return false;
        }

        #endregion

        #region Работа с событиями

        #region Запрос событий

        /// <summary>
        /// Получение событий
        /// </summary>
        List<DSRouterEventValue> IDSRouter.GetEvents(DateTime dateTimeFrom, DateTime dateTimeTo, bool needSystemEvents, bool needUserEvents, bool needTerminalEvents, List<Tuple<ushort, uint>> requestDevicesList)
        {
            if (_authResult == null)
                return null;

            List<DSRouterEventValue> resultEventsList = new List<DSRouterEventValue>();

            try
            {
                Dictionary<UInt16, List<UInt32>> dsDeviceListDictionary = new Dictionary<ushort, List<uint>>();

                #region Подготавливаем список запрашиваемых устройств для каждого DS

                // Создаем словарь списков устройств по каждому DS, если нужно                
                if (needTerminalEvents && requestDevicesList != null)
                {
                    foreach (var device in requestDevicesList)
                    {
                        UInt16 dsGuid = device.Item1;
                        UInt32 devGuid = device.Item2;

                        if (dsDeviceListDictionary.ContainsKey(dsGuid))
                            dsDeviceListDictionary[dsGuid].Add(devGuid);
                        else
                            dsDeviceListDictionary.Add(dsGuid, new List<uint> { devGuid });
                    }
                }

                #endregion                

                #region Запрашиваем события у каждого DS  и формируем конечный список событий

                // Запрашиваем у каждого DS события
                foreach (var dsGuid in dWCFClientsList.Keys)
                {
                    IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    try
                    {
                        List<UInt32> requestDevicesListForDs = null;

                        if (needTerminalEvents && requestDevicesList != null && dsDeviceListDictionary.ContainsKey(dsGuid))
                            requestDevicesListForDs = dsDeviceListDictionary[dsGuid];

                        List<DSEventValue> eventsFromDs;
                        lock (dsProxy)
                        {
                            eventsFromDs = dsProxy.GetEvents(dateTimeFrom, dateTimeTo, needSystemEvents, needUserEvents, needTerminalEvents, requestDevicesListForDs);
                        }                        

                        if (eventsFromDs == null)
                            continue;

                        resultEventsList.AddRange(ConvertDsEventValueDictionaryToDsRouterEventValueDictionary(dsGuid, eventsFromDs));
                    }
                    catch (Exception ex)
                    {
                        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                        Log.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                    }
                }

                #endregion
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetEvents() : Исключение : " + ex.Message);
            }

            // Если список сообщений пуст, то добавляем сообщение о том, что список пуст
            if (resultEventsList.Count == 0)
                resultEventsList.Add(CreateEmptyInfoEvent());

            return resultEventsList;
        }

        #endregion

        #region Получение данных событий

        /// <summary>
        /// Получить ссылку на осциллограмму по её номеру
        /// </summary>
        string IDSRouter.GetOscillogramAsUrlByID(UInt16 dsGuid, Int32 eventDataID)
        {
            if (_authResult == null)
                return null;

            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    DSOscillogram dsOscillogram;
                    lock (dsProxy)
                    {
                        dsOscillogram = dsProxy.GetOscillogramByID(eventDataID);
                    }

                    #region Разбор полученных данных

                    if (dsOscillogram == null)
                        return null;

                    return DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES + OscConverter.SaveOscillogrammToFile(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES, dsOscillogram);

                    #endregion
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetOscillogramAsUrlByID() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить zip архив с осциллограммами как кортеж массива байтов и имени архива
        /// </summary>
        Tuple<byte[], string> IDSRouter.GetOscillogramAsByteArray(UInt16 dsGuid, Int32 eventDataID)
        {
            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    DSOscillogram dsOscillogram;
                    lock (dsProxy)
                    {
                        dsOscillogram = dsProxy.GetOscillogramByID(eventDataID);
                    }

                    #region Разбор полученных данных

                    if (dsOscillogram == null)
                        return null;

                    return OscConverter.GetOscillogramData(dsOscillogram);

                    #endregion
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetOscillogramAsByteArray() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить архивную информацию (аварии, уставки и т.д.) как словарь значений
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDSRouter.GetHistoricalDataByID(UInt16 dsGuid, Int32 dataID)
        {
            if (_authResult == null)
                return null;

            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        return ConvertDsTagDictionaryToDsRouterDictionary(dsProxy.GetHistoricalDataByID(dataID));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetHistoricalDataByID() : Исключение : " + ex.Message);
            }

            return null;
        }

        #endregion

        #region Работа с квитированием

        #region По конкретному DS

        ///// <summary>
        ///// Проверяет есть ли не квитированные сообщения на конкретном DS
        ///// </summary>
        //Boolean IDSRouter.IsNotReceiptedEventsExistAtDS(UInt16 dsGuid)
        //{
        //    try
        //    {
        //        if (dWCFClientsList.ContainsKey(dsGuid))
        //        {
        //            IWcfDataServer dataServerProxy = dWCFClientsList[dsGuid].wcfDataServer;

        //            lock (dataServerProxy)
        //            {
        //                return dataServerProxy.IsNotReceiptedEventsExist();
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
        //        Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
        //    }

        //    return false;
        //}

        ///// <summary>
        ///// Получить не квитированные сообщения от конкретного DS
        ///// </summary>
        //List<DSRouterEventValue> IDSRouter.GetNotReceiptedEvents(UInt16 dsGuid)
        //{
        //    var notReceiptedDsRouterEvents = new List<DSRouterEventValue>();

        //    if (dWCFClientsList.ContainsKey(dsGuid))
        //    {
        //        IWcfDataServer dataServerProxy = dWCFClientsList[dsGuid].wcfDataServer;

        //        try
        //        {
        //            DSEventValue[] result = null;
        //            lock (dataServerProxy)
        //            {
        //                result = dataServerProxy.GetNotReceiptedEvents();
        //            }

        //            if (result != null)
        //                notReceiptedDsRouterEvents.AddRange(ConvertDsEventValueDictionaryToDsRouterEventValueDictionary(dsGuid, result));
        //        }
        //        catch (Exception ex)
        //        {
        //            TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
        //            Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);

        //            notReceiptedDsRouterEvents.Add(CreateErrorInfoEvent());
        //        }
        //    }

        //    if (notReceiptedDsRouterEvents.Count == 0)
        //        notReceiptedDsRouterEvents.Add(CreateEmptyInfoEvent("Не квитированных сообщений нет."));

        //    return notReceiptedDsRouterEvents;
        //}

        ///// <summary>
        ///// Квитировать все собщения конкретного DS
        ///// </summary>
        //void IDSRouter.ReceiptAllEventsAtDS(UInt16 dsGuid, string receiptComment)
        //{
        //    try
        //    {
        //        if (dWCFClientsList.ContainsKey(dsGuid))
        //        {
        //            IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

        //            lock (dsProxy)
        //            {
        //                dsProxy.ReceiptAllEvents(_currentUser.UserID, receiptComment);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
        //        Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
        //    }
        //}

        #endregion

        #region По всем DS

        /// <summary>
        /// Проверяет есть ли не квитированные сообщения
        /// </summary>
        Boolean IDSRouter.IsNotReceiptedEventsExist()
        {
            if (_authResult == null)
                return false;

            foreach (var dsGuid in dWCFClientsList.Keys)
            {
                IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        // Если хоть в одном DS есть не квитированные сообщения, то возвращаем true
                        if (dsProxy.IsNotReceiptedEventsExist())
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.IsNotReceiptedEventsExist() : Исключение : " + ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Получить все не квитированные сообщения
        /// </summary>
        List<DSRouterEventValue> IDSRouter.GetNotReceiptedEvents()
        {
            if (_authResult == null)
                return null;

            var notReceiptedDsRouterEvents = new List<DSRouterEventValue>();

            foreach (var dsGuid in dWCFClientsList.Keys)
            {
                IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    List<DSEventValue> result = null;
                    lock (dsProxy)
                    {
                        result = dsProxy.GetNotReceiptedEvents();
                    }

                    if (result != null)
                        notReceiptedDsRouterEvents.AddRange(ConvertDsEventValueDictionaryToDsRouterEventValueDictionary(dsGuid, result));
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.GetNotReceiptedEvents() : Исключение : " + ex.Message);
                }
            }

            if (notReceiptedDsRouterEvents.Count == 0)
                notReceiptedDsRouterEvents.Add(CreateEmptyInfoEvent("Не квитированных сообщений нет."));

            return notReceiptedDsRouterEvents;
        }

        /// <summary>
        /// Квитировать все собщения
        /// </summary>
        void IDSRouter.ReceiptAllEvents(string receiptComment)
        {
            if (_authResult == null)
                return;

            foreach (var dsGuid in dWCFClientsList.Keys)
            {
                IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.ReceiptAllEvents(_authResult.DSAuthResults[dsGuid].User.UserID, receiptComment, CreateDsUserSessionInfo(dsGuid));
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.ReceiptAllEvents() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Квитировать сообщения
        /// </summary>
        void IDSRouter.ReceiptEvents(List<DSRouterEventValue> eventValues, string receiptComment)
        {
            if (_authResult == null)
                return;

            // Список ID событий, разбитых по DS
            var eventsByDs = new Dictionary<UInt16, List<Int32>>();

            // Все события разбиваем по DS
            foreach (var eventValue in eventValues)
            {
                UInt16 dsGuid = eventValue.DsGuid;

                if (eventsByDs.ContainsKey(dsGuid))
                    eventsByDs[dsGuid].Add(eventValue.EventID);
                else
                    eventsByDs.Add(dsGuid, new List<int> { eventValue.EventID });
            }

            // Квитируем события по каждому DS отдельно
            foreach (var dsGuid in eventsByDs.Keys)
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    try
                    {
                        lock (dsProxy)
                        {
                            dsProxy.ReceiptEvents(eventsByDs[dsGuid], _authResult.DSAuthResults[dsGuid].User.UserID, receiptComment, CreateDsUserSessionInfo(dsGuid));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteErrorMessage("DSRouterService.ReceiptEvents() : Исключение : " + ex.Message);
                    }
                }
            }
        }

        #endregion

        #endregion

        #endregion

        #region Уставки

        /// <summary>
        /// Получение списка архивных записей уставок для устройства
        /// </summary>
        List<DSRouterSettingsSet> IDSRouter.GetSettingsSetsList(ushort dsGuid, uint devGuid)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Получение значений для указанных тегов из конкретного архивного набора уставок
        /// </summary>
        Dictionary<String, DSRouterTagValue> IDSRouter.GetValuesFromSettingsSet(UInt16 dsGuid, Int32 settingsSetID)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Запись набора уставкок в устройство
        /// </summary>
        void IDSRouter.SaveSettingsToDevice(ushort dsGuid, uint devGuid, Dictionary<string, DSRouterTagValue> tagsValues)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Комманды
        private object lockObjectForCommands = new object();
        /// <summary>
        /// состояние текуцей команды
        /// </summary>
        EnumerationCommandStates CurrentCMDState = EnumerationCommandStates.undefined;
        private UInt16 DS4CurrtntCMD = 0;
        /// <summary>
        /// /// Запрос на запуск команды на устройстве
        /// </summary>
        /// <param name="ACommandID">ds.dev.cmdid</param>
        /// <param name="AParameters">массив параметров</param>
        /// <returns>false - если роутер уже выполняет другую команду</returns>
        public void CommandRun(string ACommandID, object[] AParameters)
        {
            try
            {
                CurrentCMDState = EnumerationCommandStates.sentFromClientToRouter;

                string[] arrcmdid = ACommandID.Split(new char[] { '.' });
                DS4CurrtntCMD = UInt16.Parse(arrcmdid[0]);
                uint dev = UInt32.Parse(arrcmdid[1]);
                uint tagguid = UInt32.Parse(arrcmdid[2]);

                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1385, string.Format("{0} : Получен запрос на команду от HMI-клиента для {1}.", DateTime.Now.ToString(), ACommandID));

                IWcfDataServer iwds = dWCFClientsList[DS4CurrtntCMD].wcfDataServer;
                //bool rez = iwds.RunCMD(DS4CurrtntCMD, dev, tagguid, null /*byte[] pq*/);   // нужно поправить контракт на DS на массив объектов
                iwds.BeginCommandRun(ACommandID, null, GetCMDStateCallback, iwds);

                CurrentCMDState = EnumerationCommandStates.sentFromRouterToDataServer;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.CommandRun() : Исключение : " + ex.Message);
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
        /// <summary>
        /// асинхронная функция для
        /// инициирования запроса состояния выполнения команды 
        /// с клиента
        /// </summary>
        /// <param name="result"></param>
        void GetCMDStateCallback(IAsyncResult result)
        {
            try
            {

            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        /// <summary>
        /// возврат клиенту статуса 
        /// выполнения команды        
        /// </summary>
        /// <param name="ACommandID"></param>
        /// <returns></returns>
        public EnumerationCommandStates CommandStateCheck(string ACommandID)
        {
            try
            {
                lock (lockObjectForCommands)
                {
                    IWcfDataServer iwds = dWCFClientsList[DS4CurrtntCMD].wcfDataServer;

                    DSServiceReference.EnumerationCommandStates ecs = iwds.CommandStateCheck();

                    CurrentCMDState = (EnumerationCommandStates)ecs;
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);

                CurrentCMDState = EnumerationCommandStates.cmdNotSend_DSR_2_DS;
            }
            return CurrentCMDState;
        }
        ///// <summary>
        ///// асинхронная функция для
        ///// инициирования запроса состояния выполнения команды 
        ///// с клиента
        ///// </summary>
        ///// <param name="result"></param>
        //void GetCMDStateCheck(IAsyncResult result)
        //{
        //    try
        //    {

        //    }
        //    catch (Exception ex)
        //    {
        //        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
        //    }
        //}
        #endregion

        #region Работа с документами

        #region Методы для работы с существующими документами

        /// <summary>
        /// Получить список документов терминала
        /// </summary>
        List<DSRouterDocumentDataValue> IDSRouter.GetDocumentsList(UInt16 dsGuid, Int32 devGuid)
        {
            if (_authResult == null)
                return null;

            var documentsList = new List<DSRouterDocumentDataValue>();

            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    List<DSDocumentDataValue> dsDocuments;
                    lock (dsProxy)
                    {
                        dsDocuments = dsProxy.GetDocumentsList(devGuid);
                    }

                    if (dsDocuments != null)
                        documentsList = ConvertDsDocumentsToDsRouterDocuments(dsDocuments);
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.GetDocumentsList() : Исключение : " + ex.Message);
                }
            }

            if (documentsList.Count == 0)
                documentsList.Add(CreateInformDsRouterDocumentDataValue("Для данного устройства нет приложенных файлов."));

            return documentsList;
        }

        /// <summary>
        /// Получить ссылку на документ
        /// </summary>
        string IDSRouter.GetDocumentByID(UInt16 dsGuid, Int32 documentId)
        {
            if (_authResult == null)
                return null;

            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        var dsFile = dsProxy.GetDocumentByID(documentId);

                        if (dsFile == null)
                            return null;

                        string resultUrl = DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES +
                               FileSaver.SaveFile(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES, dsFile.FileName, dsFile.Content);

                        return resultUrl;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.GetDocumentByID() : Исключение : " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Получить документ
        /// </summary>
        Tuple<byte[], string> IDSRouter.GetDocument(UInt16 dsGuid, Int32 documentId)
        {
            if (_authResult == null)
                return null;

            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        var dsFile = dsProxy.GetDocumentByID(documentId);

                        if (dsFile == null)
                            return null;

                        return new Tuple<byte[], string>(dsFile.Content, dsFile.FileName);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.GetDocument() : Исключение : " + ex.Message);
                }
            }

            return null;
        }

        #endregion

        #region Методы для загрузки документов

        /// <summary>
        /// Иницилизировать передачу файлов
        /// </summary>
        bool IDSRouter.InitUploadFileSession(UInt16 dsGuid, Int32 devGuid, string fileName, string comment)
        {
            if (_authResult == null)
                return false;

            return _fileUploadHelper.TryInitFileUploadSession(dsGuid, devGuid, fileName, comment, _sessionId);
        }

        /// <summary>
        /// Загрузить кусок файла
        /// </summary>
        bool IDSRouter.UploadFileChunk(byte[] fileChunkBytes)
        {
            if (_authResult == null)
                return false;

            return _fileUploadHelper.UploadFileChunk(fileChunkBytes, _sessionId);
        }

        /// <summary>
        /// Сохранить загруженный файл
        /// </summary>
        string IDSRouter.SaveUploadedFile()
        {
            if (_authResult == null)
                return null;

            var dsGuid = _fileUploadHelper.GetDsGuid(_sessionId);
            return _fileUploadHelper.SaveUploadedFile(_sessionId, _authResult.DSAuthResults[dsGuid].User.UserID);
        }

        /// <summary>
        /// Сбрасывает передачу файла
        /// </summary>
        void IDSRouter.TerminateUploadFileSession()
        {
            _fileUploadHelper.TerminateUploadFileSession(_sessionId);
        }

        #endregion

        #endregion

        #region Время

        /// <summary>
        /// Получить текущее время роутера
        /// </summary>
        DateTime IDSRouter.GetCurrentDateTime()
        {
            return DateTime.Now;
        }

        #endregion

        #region Ручной ввод данных

        #region Ручной ввод значений тегов

        /// <summary>
        /// Установить значение тега 
        /// с уровня HMI через тип object
        /// (качество тега vqHandled)
        /// </summary>
        void IDSRouter.SetTagValueFromHMI(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid, object valinobject)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.SetTagValueFromHMI(String.Format("{0}.{1}.{2}", dsGuid, devGuid, tagGuid), valinobject);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.SetTagValueFromHMI() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// восстановить процесс естесвенного обновления тега
        /// (качество тега vqGood или по факту)
        /// </summary>
        void IDSRouter.ReSetTagValueFromHMI(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.ReSetTagValueFromHMI(String.Format("{0}.{1}.{2}", dsGuid, devGuid, tagGuid));
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.ReSetTagValueFromHMI() : Исключение : " + ex.Message);
                }
            }
        }

        #endregion

        #region Ручной ввод преобразовывающих коэффициентов

        /// <summary>
        /// Получить коэффициент преобразования для тега
        /// </summary>
        Object IDSRouter.GetTagAnalogTransformationRatio(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        return dsProxy.GetTagAnalogTransformationRatio(dsGuid, devGuid, tagGuid);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.GetTagAnalogTransformationRatio() : Исключение : " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Установить коэффициент преобразования
        /// </summary>
        void IDSRouter.SetTagAnalogTransformationRatio(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid, Object transformationRatio)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.SetTagAnalogTransformationRatio(dsGuid, devGuid, tagGuid, transformationRatio);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.SetTagAnalogTransformationRatio() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Сбросить коэффициент преобразования
        /// </summary>
        void IDSRouter.ReSetTagAnalogTransformationRatio(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.ReSetTagAnalogTransformationRatio(dsGuid, devGuid, tagGuid);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.ReSetTagAnalogTransformationRatio() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Возвращает true, если значение дискретного тега инверсируется
        /// </summary>
        bool? IDSRouter.IsInverseDiscretTag(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        return dsProxy.IsInverseDiscretTag(dsGuid, devGuid, tagGuid);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.IsInverseDiscretTag() : Исключение : " + ex.Message);
                }
            }

            return null;
        }

        /// <summary>
        /// Инверсирует значение дискретного тега
        /// </summary>
        void IDSRouter.InverseDiscretTag(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid, Boolean newInverseValue)
        {
            if (dWCFClientsList.ContainsKey(dsGuid))
            {
                var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.InverseDiscretTag(dsGuid, devGuid, tagGuid, newInverseValue);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.InverseDiscretTag() : Исключение : " + ex.Message);
                }
            }
        }

        #endregion

        #endregion

        #region Тренды

        /// <summary>
        /// Получить список тегов, у которых включена запись значений
        /// </summary>
        public List<string> GetTagsListWithEnabledTrendSave()
        {
            var result = new List<string>();

            foreach (var dsService in dWCFClientsList.Values)
            {
                var dsProxy = dsService.wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        var r = dsProxy.GetTagsListWithEnabledTrendSave();

                        result.AddRange(from s in r select String.Format("{0}.{1}", dsService.dsUID, s));
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage("DSRouterService.GetTagsListWithEnabledTrendSave() : Исключение : " + ex.Message);
                }
            }

            return result;
        }

        /// <summary>
        /// Получить доступные диапозоны значений тренда
        /// </summary>
        public List<Tuple<DateTime, DateTime>> GetTrendDateTimeRanges(ushort dsGuid, uint devGuid, uint tagGuid)
        {
            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        return dsProxy.GetTagTrendDateTimeRanges(devGuid, tagGuid).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetTrendDateTimeRanges() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить тренд единым списком
        /// </summary>
        public List<Tuple<DateTime, object>> GetTagTrend(ushort dsGuid, uint devGuid, uint tagGuid, DateTime startDateTime, DateTime endDateTime)
        {
            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        return dsProxy.GetTagTrend(devGuid, tagGuid, startDateTime, endDateTime).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetTagTrend() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить список обособленных трендов
        /// </summary>
        public List<List<Tuple<DateTime, object>>> GetTagTrendsList(ushort dsGuid, uint devGuid, uint tagGuid, DateTime startDateTime, DateTime endDateTime)
        {
            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        return dsProxy.GetTagTrendsList(devGuid, tagGuid, startDateTime, endDateTime);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetTagTrendsList() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить настройки режима работы записи тренда
        /// </summary>
        public DSRouterTrendSettings GetTrendSettings(ushort dsGuid, uint devGuid, uint tagGuid)
        {
            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        var result = dsProxy.GetTrendSettings(devGuid, tagGuid);

                        if (result != null)
                            return new DSRouterTrendSettings(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetTrendSettings() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Установить настройки режима работы записи тренда
        /// </summary>
        public void SetTrendSettings(ushort dsGuid, uint devGuid, uint tagGuid, DSRouterTrendSettings trendSettings)
        {
            try
            {
                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    var dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        var dsTrendSettings = new DSTrendSettings
                        {
                            Enable = trendSettings.Enable,
                            Sample = trendSettings.Sample,
                            AbsoluteError = trendSettings.AbsoluteError,
                            RelativeError = trendSettings.RelativeError,
                            MaxCacheMinutes = trendSettings.MaxCacheMinutes,
                            MaxCacheValuesCount = trendSettings.MaxCacheValuesCount
                        };

                        dsProxy.SetTrendSettings(devGuid, tagGuid, dsTrendSettings);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.SetTrendSettings() : Исключение : " + ex.Message);
            }
        }

        #endregion

        #region Отчеты

        /// <summary>
        /// Получить список доступных отчетов
        /// </summary>
        /// <returns></returns>
        public List<DSRouterReportDescription> GetReportsDescriptions()
        {
            return null;
        }

        /// <summary>
        /// Получить ежедневнвый отчет
        /// </summary>
        public string GetDailyReport(DSRouterDailyReportSettings reportSettings)
        {
            string result = null;

            try
            {
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetDailyReport() : Исключение : " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Получить ежедневнвый отчет
        /// </summary>
        public byte[] GetDailyReportAsByteArray(DSRouterDailyReportSettings reportSettings)
        {
            byte[] result = null;

            try
            {
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetDailyReportAsByteArray() : Исключение : " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Получить отчет по событиям устройства
        /// </summary>
        public string GetEventsReport(DSRouterEventsReportSettings reportSettings)
        {
            string result = null;

            try
            {
                string reportName = "Отчет " + DateTime.Now.ToShortDateString();

                if (SaveEventsReport(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES, reportName, reportSettings) == null)
                    return null;

                result = DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES + reportName + reportSettings.ReportExtension;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetEventsReport() : Исключение : " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Получить отчет по событиям устройства
        /// </summary>
        public byte[] GetEventsReportAsByteArray(DSRouterEventsReportSettings reportSettings)
        {
            byte[] result = null;

            try
            {
                string reportName = "Отчет " + DateTime.Now.ToShortDateString();

                var pathToReport = SaveEventsReport(Path.GetTempPath(), reportName, reportSettings);
                if (pathToReport == null)
                    return null;

                using (var stream = File.Open(pathToReport, FileMode.Open))
                {
                    result = new byte[stream.Length];

                    stream.Read(result, 0, result.Length);
                    stream.Close();    
                }
                
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetEventsReportAsByteArray() : Исключение : " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Получить отчет по значениям тегов
        /// </summary>
        public string GetTagsReport(DSRouterTagsReportSettings reportSettings)
        {
            string result = null;

            try
            {
                string reportName = "Отчет " + DateTime.Now.ToShortDateString();

                if (SaveTagsReport(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES, reportName, reportSettings) == null)
                    return null;

                result = DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES + reportName + reportSettings.ReportExtension;
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetTagsReport() : Исключение : " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Получить отчет по значениям тегов
        /// </summary>
        public byte[] GetTagsReportAsByteArray(DSRouterTagsReportSettings reportSettings)
        {
            byte[] result = null;

            try
            {
                string reportName = "Отчет " + DateTime.Now.ToShortDateString();

                var pathToReport = SaveTagsReport(Path.GetTempPath(), reportName, reportSettings);
                if (pathToReport == null)
                    return null;

                using (var stream = File.Open(pathToReport, FileMode.Open))
                {
                    result = new byte[stream.Length];

                    stream.Read(result, 0, result.Length);
                    stream.Close();
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSRouterService.GetTagsReportAsByteArray() : Исключение : " + ex.Message);
            }

            return result;
        }

        #endregion

        #endregion

        #region Private - методы

        #region Вспомогательные методы для иницилизации роутера

        /// <summary>
        /// Иницилизирует класс для последющей работы
        /// </summary>
        private static void InitRouterService()
        {
            InitProjectSettings();

            _dataSource = new DataServersCollector();
            _fileUploadHelper = new FileUploadHelper(_dataSource as DataServersCollector);
        }

        /// <summary>
        /// Иницилизирует настройки проекта
        /// </summary>
        private static void InitProjectSettings()
        {
            HMI_Settings.PathToConfigurationFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Project", "Configuration.cfg");
        }

        #endregion

        #region Вспомогательный методы для работы с данными

        #region Методы для работы с TagValue

        /// <summary>
        /// Метод для преобразования словаря из формата DS в формат роутера
        /// </summary>
        private Dictionary<string, DSRouterTagValue> ConvertDsTagDictionaryToDsRouterDictionary(Dictionary<string, DSTagValue> dsTagDictionary)
        {
            var result = new Dictionary<string, DSRouterTagValue>();

            foreach (var dsTagValue in dsTagDictionary)
            {
                result.Add(dsTagValue.Key, new DSRouterTagValue(dsTagValue.Value));
            }

            return result;
        }

        #endregion

        #region Методы для работа с DSEventValue

        /// <summary>
        /// Метод для преобразования событий из формата DS в формат роутера
        /// </summary>
        /// <param name="dsEvents"></param>
        /// <returns></returns>
        private List<DSRouterEventValue> ConvertDsEventValueDictionaryToDsRouterEventValueDictionary(UInt16 dsGuid, IEnumerable<DSEventValue> dsEvents)
        {
            var dsRouterEvents = new List<DSRouterEventValue>();

            foreach (var dsEvent in dsEvents)
            {
                dsRouterEvents.Add(new DSRouterEventValue(dsGuid, dsEvent));
            }

            return dsRouterEvents;
        }

        /// <summary>
        /// Создает событие, информирующее о том, что событий не найдено
        /// </summary>
        private DSRouterEventValue CreateEmptyInfoEvent(string eventText = "По данному запросу ничего не найдено")
        {
            return new DSRouterEventValue()
            {
                EventDataID = -1,
                EventID = -1,
                EventTime = DateTime.Now,
                EventSourceName = "DSRouter",
                EventText = eventText,
                EventSourceComment = "DSRouter",
                EventDataType = DSRouterEventDataType.None
            };
        }

        #endregion

        #region Методы для работы с DSDocumentDataValue

        /// <summary>
        /// Преобразует список документов из формата DS в формат роутера
        /// </summary>
        public List<DSRouterDocumentDataValue> ConvertDsDocumentsToDsRouterDocuments(IEnumerable<DSDocumentDataValue> dsDocuments)
        {
            var result = new List<DSRouterDocumentDataValue>();

            foreach (var dsDocumentDataValue in dsDocuments)
                result.Add(new DSRouterDocumentDataValue(dsDocumentDataValue));

            return result;
        }

        /// <summary>
        /// Создает фиктивный DSRouterDocumentDataValue с информационным сообщением
        /// </summary>
        /// <param name="comment"></param>
        /// <returns></returns>
        public DSRouterDocumentDataValue CreateInformDsRouterDocumentDataValue(string comment)
        {
            var result = new DSRouterDocumentDataValue();
            result.DocumentAddDate = DateTime.Now;
            result.DocumentComment = comment;
            result.DocumentFileName = String.Empty;
            result.DocumentID = -1;
            result.DocumentUserName = "Сервер данных";

            return result;
        }

        #endregion

        #region Методы для работы с загружаемыми файлами

        /// <summary>
        /// Собирает все куски файла в один массив
        /// </summary>
        byte[] CombineFileChunks(List<byte[]> fileChunks)
        {
            List<byte> result = new List<byte>();

            foreach (var fileChunk in fileChunks)
                result.AddRange(fileChunk);

            return result.ToArray();
        }

        #endregion

        #endregion

        #region Вспомогательные методы для работы с текущей сессией

        /// <summary>
        /// Создает класс, несущий информацию о пользовательской сессии для DS
        /// </summary>
        /// <returns></returns>
        private DSUserSessionInfo CreateDsUserSessionInfo(UInt16 dsGuid)
        {
            //MessageProperties messageProperties = OperationContext.Current.IncomingMessageProperties;
            //RemoteEndpointMessageProperty endpointProperty = messageProperties[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;

            return new DSUserSessionInfo
            {
                UserId = _authResult.DSAuthResults[dsGuid].User.UserID,
                UserIpAddress = GetuserIpAddress(),
                UserMacAddress = GetUserMacAddress()
            };
        }

        private DSUserSessionInfo CreateDsUserSessionInfoForAuthorization()
        {
            return new DSUserSessionInfo {UserIpAddress = GetuserIpAddress(), UserMacAddress = GetUserMacAddress()};
        }

        private string GetuserIpAddress()
        {
            return "127.0.0.1";
        }

        private string GetUserMacAddress()
        {
            return "00-00-00-00-00-00";
        }

        /// <summary>
        /// Закрывает сеанс передачи данных
        /// </summary>
        void CloseFileUploadSession()
        {
            _fileUploadHelper.TerminateUploadFileSession(_sessionId);
        }

        #endregion

        #region Вспомогательные методы для подготовки отчетов

        #region EventsReport

        /// <summary>
        /// Собирает данные и сохраняет отчет в указанное место
        /// </summary>
        private string SaveEventsReport(string pathToSave, string reportName, DSRouterEventsReportSettings reportSettings)
        {
            string result = null;

            #region Получение данных

            var dsGuid = reportSettings.DsGuid;
            var deviceGuid = reportSettings.DeviceGuid;

            var events = (this as IDSRouter).GetEvents(reportSettings.StartDateTime, reportSettings.EndDateTime,
                false, false, true, new List<Tuple<ushort, uint>> { new Tuple<ushort, uint>(dsGuid, deviceGuid) });

            if (events == null)
                return null;

            #endregion

            #region Подготовка данных

            var dataSource = FillDataSet(events, null, null);

            #endregion

            #region Формирование отчета

            string reportTemplateName = String.IsNullOrWhiteSpace(reportSettings.ReportTamplateName) ? "EventsReport" : reportSettings.ReportTamplateName;

            var report = new Report(reportTemplateName);
            report.SetDataSource(dataSource);

            report.SaveReportFile(pathToSave, reportName, reportSettings.ReportExtension.ToString());

            result = Path.Combine(pathToSave, reportName + "." + reportSettings.ReportExtension.ToString());

            #endregion

            return result;
        }

        #endregion

        #region TagsReport

        /// <summary>
        /// Собирает данные и сохраняет отчет в указанное место
        /// </summary>
        private string SaveTagsReport(string pathToSave, string reportName, DSRouterTagsReportSettings reportSettings)
        {
            string result = null;

            // Получаем тренды
            var trends = GetTrends(reportSettings.Tags, reportSettings.StartDateTime, reportSettings.EndDateTime);
            if (trends == null || trends.Count == 0)
                return null;

            // Если надо, то приводим тренды к единому шагу
            Dictionary<DateTime, Tuple<object, object, object, object>> trendsWithFixStep = null;
            if (reportSettings.Interval != 0)
            {
                trendsWithFixStep = GetTrendsWithFixStep(trends, reportSettings.Interval, reportSettings.StartDateTime, reportSettings.EndDateTime);
            }

            // Заполняем DataSet
            var dataSource = FillDataSet(null, trends, trendsWithFixStep);

            #region Формирование отчета

            string reportTemplateName = String.IsNullOrWhiteSpace(reportSettings.ReportTamplateName) ? "TagsReport" : reportSettings.ReportTamplateName;

            var report = new Report(reportTemplateName);
            report.SetDataSource(dataSource);

            report.SaveReportFile(pathToSave, reportName, reportSettings.ReportExtension.ToString());

            result = Path.Combine(pathToSave, reportName + "." + reportSettings.ReportExtension.ToString());

            #endregion

            return result;
        }

        /// <summary>
        /// Получить тренды для указанных тегов в указанных диапазонах с дополнительной информацией,
        /// такой как: dsGuid, devGuid, tagGuid, tagName
        /// </summary>
        private Dictionary<Tuple<UInt16, UInt32, UInt32, string>, List<Tuple<DateTime, object>>> GetTrends(List<string> tags, DateTime startDateTime, DateTime endDateTime)
        {
            var trends = new Dictionary<Tuple<UInt16, UInt32, UInt32, string>, List<Tuple<DateTime, object>>>();

            foreach (var tag in tags)
            {
                var c = tag.Split('.');

                var dsGuid = UInt16.Parse(c[0]);
                var deviceGuid = UInt32.Parse(c[1]);
                var tagGuid = UInt32.Parse(c[2]);
                var tagName = (this as IDSRouter).GetRTUTagName(dsGuid, deviceGuid, 0, tagGuid);

                var trend = GetTagTrend(dsGuid, deviceGuid, tagGuid, startDateTime, endDateTime);
                if (trend != null && trend.Count > 0)
                    trends.Add(new Tuple<ushort, uint, uint, string>(dsGuid, deviceGuid, tagGuid, tagName), trend);
            }

            return trends;
        }

        private Dictionary<DateTime, Tuple<object, object, object, object>> GetTrendsWithFixStep(
            Dictionary<Tuple<UInt16, UInt32, UInt32, string>, List<Tuple<DateTime, object>>> trends,
            uint interval,
            DateTime startDateTime,
            DateTime endDateTime)
        {
            /*
             * На данный момент считаем, что
             * указанный шаг всегда больше чем шаг между реальными значениями.
             * В качестве парвила формирования - берем последнее значение в интервале.
             * Надо будет переделать.
             */

            var result = new Dictionary<DateTime, Tuple<object, object, object, object>>();
            var values = new object[4];

            var startInterval = startDateTime;
            var endInterval = startDateTime.AddSeconds(interval);
            while (endInterval < endDateTime)
            {
                int i = 0;
                foreach (var trend in trends.Values)
                {
                    if (i >= 4)
                        break;

                    var valuesInInterval = trend.Where(tuple => tuple.Item1 > startInterval && tuple.Item1 <= endInterval).ToList();

                    if (valuesInInterval.Count == 0)
                        values[i] = null;
                    else
                        values[i] = valuesInInterval.Last().Item2;
                    i++;
                }

                result.Add(endInterval, new Tuple<object, object, object, object>(values[0], values[1], values[2], values[3]));

                startInterval = endInterval;
                endInterval = endInterval.AddSeconds(interval);
            }

            return result;
        }

        #endregion

        #region Заполнение DataSet

        /// <summary>
        /// Создает и заполняет DataSet
        /// </summary>
        private DataSet FillDataSet(List<DSRouterEventValue> events,
            Dictionary<Tuple<UInt16, UInt32, UInt32, string>, List<Tuple<DateTime, object>>> trends,
            Dictionary<DateTime, Tuple<object, object, object, object>> trendsWithFixStep)
        {
            var dataSet = new ReportsDataSource();

            if (events != null)
            {
                var eventsTable = dataSet.Tables["Events"];

                foreach (var dsRouterEvent in events)
                {
                    eventsTable.Rows.Add(dsRouterEvent.EventTime, dsRouterEvent.EventText);
                }
            }

            if (trends != null)
            {
                var tagsDescriptionTable = dataSet.Tables["TagsDescription"];
                var trendsValuesTable = dataSet.Tables["TrendsValues"];

                foreach (var trend in trends)
                {
                    var row = tagsDescriptionTable.Rows.Add(trend.Key.Item1, trend.Key.Item2, trend.Key.Item3, trend.Key.Item4);
                    var tagId = (int)row[4];

                    foreach (var trendValue in trend.Value)
                    {
                        trendsValuesTable.Rows.Add(tagId, trendValue.Item1, trendValue.Item2);
                    }
                }
            }

            if (trendsWithFixStep != null)
            {
                var fixTrendsValuesTable = dataSet.Tables["FixTrendsValues"];

                foreach (var tuple in trendsWithFixStep)
                {
                    fixTrendsValuesTable.Rows.Add(tuple.Key, tuple.Value.Item1, tuple.Value.Item2, tuple.Value.Item3, tuple.Value.Item4);
                }
            }

            return dataSet;
        }

        #endregion

        #endregion

        #endregion

        #region Handlers

        #region Обработчики событий, связанные с DataServer'ом

        #region Результат выполнения команд

        /// <summary>
        /// вызывается когда от DS 
        /// приходит результат выполнения команды
        /// </summary>
        /// <param name="cmdarray"></param>
        void cbh_OnCMDExecuted(byte[] cmdarray)
        {
            try
            {
                // уберем факт выдачи команды
                //if (/*kvp.Value.*/IsCMDActive)
                //{
                //    currClient.NotifyCMDExecuted(cmdarray);

                //    IsCMDActive = false;

                //    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Critical, 2416, string.Format("{0} : {1} : {2} : Результат команды ретранслирован от DataServer клиенту.", DateTime.Now.ToString(), @"X:\Projects\00_DataServer_ps_304\DSRouterService\DSRouterService\DSRouterService.svc.cs", "cbh_OnCMDExecuted()"));
                //}
                //else
                //    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Critical, 2419, string.Format("{0} : {1} : {2} : Ошибка ретрансляции команды от DataServer клиенту.", DateTime.Now.ToString(), @"X:\Projects\00_DataServer_ps_304\DSRouterService\DSRouterService\DSRouterService.svc.cs", "cbh_OnCMDExecuted()"));
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Log.LogTrace("DSRouterService.cbh_OnCMDExecuted() : Исключение : " + ex.Message);
            }
        }

        #endregion

        #endregion

        #region Обработчики событий, связанных с текущей сессией

        /// <summary>
        /// Обработчик события разрыва канала с клиентом
        /// </summary>
        private void ClientDisconnected(object sender, EventArgs e)
        {
            CloseFileUploadSession();

            _dataSource.UnsubscribeTags(_sessionId);
        }

        #endregion

        #endregion
    }

}
