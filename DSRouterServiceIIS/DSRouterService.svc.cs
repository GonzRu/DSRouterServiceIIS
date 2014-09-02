using System;
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
                    Directory.Delete(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES);

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
                rez = iwds.RunCMD(ds, dev, tagguid, pq);
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

                    DSUser[] users;
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

                    DSUserGroup[] userGroups;
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
                        UInt32[] requestDevicesListForDs = null;

                        if (needTerminalEvents && requestDevicesList != null && dsDeviceListDictionary.ContainsKey(dsGuid))
                            requestDevicesListForDs = dsDeviceListDictionary[dsGuid].ToArray();

                        DSEventValue[] eventsFromDs;
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
                    DSEventValue[] result = null;
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
                            dsProxy.ReceiptEvents(eventsByDs[dsGuid].ToArray(), _authResult.DSAuthResults[dsGuid].User.UserID, receiptComment, CreateDsUserSessionInfo(dsGuid));
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

        /// <summary>
        /// Запрос на запуск команды на устройстве
        /// </summary>
        void IDSRouter.CommandRun(ushort dsGuid, uint devGuid, string commandID, object[] parameters)
        {
            throw new NotImplementedException();
        }

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
                    DSDocumentDataValue[] dsDocuments;
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
                catch (Exception)
                {
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
                        
                    }
                }
            }
            catch (Exception ex)
            {
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
            }
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

        #region Вспомогательные методы для работы с DS

        

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
                if (/*kvp.Value.*/IsCMDActive)
                {
                    currClient.NotifyCMDExecuted(cmdarray);

                    IsCMDActive = false;

                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Critical, 2416, string.Format("{0} : {1} : {2} : Результат команды ретранслирован от DataServer клиенту.", DateTime.Now.ToString(), @"X:\Projects\00_DataServer_ps_304\DSRouterService\DSRouterService\DSRouterService.svc.cs", "cbh_OnCMDExecuted()"));
                }
                else
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Critical, 2419, string.Format("{0} : {1} : {2} : Ошибка ретрансляции команды от DataServer клиенту.", DateTime.Now.ToString(), @"X:\Projects\00_DataServer_ps_304\DSRouterService\DSRouterService\DSRouterService.svc.cs", "cbh_OnCMDExecuted()"));
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
