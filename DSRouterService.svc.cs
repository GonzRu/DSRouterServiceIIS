using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.Text;
using System.Timers;
using System.Xml.Linq;
using DSFakeService.DSServiceReference;
using DSFakeService.Helpers;

namespace DSRouterService
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
        /// таймер перезапуска соединенения 
        /// с отвалившимися DS
        /// </summary>
        private System.Timers.Timer tmrDSRestart;

        /// <summary>
        /// Текущий контекст
        /// </summary>
        private OperationContext CurrentСontext;

        /// <summary>
        /// список текущих тегов
        /// для подписки
        /// </summary>
        private List<string> ATagIDsList = new List<string>();

        /// <summary>
        /// список подписанных тегов
        /// </summary>
        /// bool - факт изменения значения
        private SortedDictionary<string, Tuple<DSRouterTagValue, bool>> lstSubscribeTagsInHMIContext = new SortedDictionary<string, Tuple<DSRouterTagValue, bool>>();

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
        private DSRouterUser _currentUser;

        #region Поля, нужные для записи файла

        #warning Неплохо бы вынести это в отдельный класс.

        /// <summary>
        /// Номер DS, куда будет записан файл
        /// </summary>
        private UInt16 _dsGuidFileTransfer;

        /// <summary>
        /// Номер устройства, которому будет приложен записываемый файл
        /// </summary>
        private Int32 _devGuidFileTransfer;

        /// <summary>
        /// Имя записываемого файла
        /// </summary>
        private string _fileNameFileTransfer;

        /// <summary>
        /// Комментарий к записываемому файлу
        /// </summary>
        private string _fileCommentFileTransfer;

        #endregion

        #endregion

        #endregion

        #region Конструктор

        static DSRouterService()
        {
            TraceSourceLib.TraceSourceDiagMes.StartTrace("AppDiagnosticLog", 30000);

            DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Share");
            DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES = "http://192.168.240.39/DSRouter.DSRouterService/Share/";
        }

        public DSRouterService()
        {
            try
            {
                InitDSRouter();

                CurrentСontext.Channel.Closed += ClientDisconnected;
                CurrentСontext.Channel.Faulted += ClientDisconnected;                

                Utilities.LogTrace("DSRouterService: Создан объект DSRouterService");
                Debug.WriteLine("DSRouterService: Создан объект DSRouterService");
            }
            catch (Exception ex)
            {
                Utilities.LogTrace("DSRouterService.DSRouterService() : Исключение : " + ex.Message);
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        ~DSRouterService()
        {
            Utilities.LogTrace("DSRouterService: Объект DSRouterService уничтожен");
            Debug.WriteLine("DSRouterService: Объект DSRouterService уничтожен");
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
                Utilities.LogTrace("DSRouterService.GetDSValueAsByteBuffer() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSOscByIdInBD() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.SetReq2ArhivInfo() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.RunCMDMOA() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSConfigFile() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSINFO() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetSourceGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetSourceName() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetECUGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetECUName() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetSrcEcuRTUGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetRTUGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.IsRTUEnable() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetRTUDescription() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetGroupGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.IsGroupEnable() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetRTUGroupName() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetSubGroupGUIDsInGroup() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetRtuGroupTagGUIDs() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetRTUTagName() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSLastErrorsGUID() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSLastErrorGUID() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.GetDSErrorTextByErrorGUID() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.AcknowledgementOfErrors() : Исключение : " + ex.Message);
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
                Utilities.LogTrace("DSRouterService.RegisterForErrorEvent() : Исключение : " + ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// Функция для проверки состояния связи
        /// </summary>
        void IDSRouter.Ping()
        {
            Utilities.LogTrace("DSRouterService : Ping() от клиента HMI");
            try
            {
                lock (lockKey)
                {
                    currClient.Pong();
                    Utilities.LogTrace("DSRouterService : Pong() клиенту HMI");
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 127, string.Format("{0} : {1} : {2} : ОШИБКА: {3}", DateTime.Now.ToString(), @"X:\Projects\00_DataServer\DSRouterService\DSRouterService\DSRouterService.svc.cs", "Ping()()", ex.Message));
                Utilities.LogTrace("DSRouterService.Ping() : Исключение : " + ex.Message);
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
            Dictionary<string, DSRouterTagValue> dlist = new Dictionary<string, DSRouterTagValue>();

            Utilities.LogTrace("DSRouterService : GetTagsValue()");

            try
            {
                if (currClient == null)
                {
                    lock (lockKey)
                    {
                        currClient = CurrentСontext.GetCallbackChannel<IDSRouterCallback>();
                    }
                }

                // запоминаем теги из запроса для подписки
                foreach (string tag in ATagIDsList)
                {
                    if (!tag.Equals("", StringComparison.InvariantCultureIgnoreCase))
                        dlist[tag] = new DSRouterTagValue();//(1, 3);
                }

                /*
                 * для ускорения процесса разработки
                 * считаем что DS один и все затачиваем под него
                 */
                IWcfDataServer iwds = null;
                if (dWCFClientsList.ContainsKey(0))
                {
                    iwds = dWCFClientsList.ElementAt(0).Value.wcfDataServer;

                    // формируем, запоминаем список и подписываемся на обновление
                    this.ATagIDsList = ATagIDsList;
                    iwds.SubscribeRTUTags(ATagIDsList.ToArray());
                    AddTags(ATagIDsList);

                    // получение тегов от DS
                    Dictionary<string, DSTagValue> dlistDS = iwds.GetTagsValue(this.ATagIDsList.ToArray());

                    foreach (KeyValuePair<string, DSTagValue> kvp in dlistDS)
                    {
                        DSRouterTagValue dsdstv = new DSRouterTagValue();
                        dsdstv.VarQuality = kvp.Value.VarQuality;
                        dsdstv.VarValueAsObject = kvp.Value.VarValueAsObject;

                        if (dlist.ContainsKey(kvp.Key))
                            dlist[kvp.Key] = dsdstv;
                        else
                            dlist.Add(kvp.Key, dsdstv);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return dlist;
        }

        /// <summary>
        /// Запрос на получение тегов, чьи значения изменились
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDSRouter.GetTagsValuesUpdated()
        {
            Dictionary<string, DSRouterTagValue> tagsDataUpdated = new Dictionary<string, DSRouterTagValue>();

            try
            {
                lock (lockKey)
                {
                    for (int i = 0; i < GetSubscribeTags().Count; i++)
                    {
                        KeyValuePair<string, Tuple<DSRouterTagValue, bool>> kvp = GetSubscribeTags().ElementAt(i);
                        if (kvp.Value.Item2)
                        {
                            tagsDataUpdated.Add(kvp.Key, GetDSTagValue(kvp.Key));
                            ResetReNewFlag(kvp.Key);
                        }

                    }
                }
            }
            catch (Exception exx)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(exx);
            }

            return tagsDataUpdated;
        }

        /// <summary>
        /// подписаться на теги
        /// </summary>
        /// <param name="request"></param>
        void IDSRouter.SubscribeRTUTags(List<string> request)
        {
            Utilities.LogTrace("DSRouterService : SubscribeRTUTags()");

            try
            {
                lock (lockKey)
                {
                    Utilities.LogTrace("DSRouterService.SubscribeRTUTags() : lock");

                    AddTags(request);

                    /*
                     * для ускорения процесса разработки
                     * считаем что DS один и все затачиваем под него
                     * если ds несколько, то перебираем запрос и формируем списки
                     * поотдельности для каждого ds
                     */
                    if (dWCFClientsList.Count() == 0)
                        return;

                    IWcfDataServer iwds = dWCFClientsList.ElementAt(0).Value.wcfDataServer;

                    iwds.SubscribeRTUTags(request.ToArray());
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.SubscribeRTUTags() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// отписаться от обновления тегов
        /// </summary>
        void IDSRouter.UnscribeRTUTags(List<string> request)
        {
            Utilities.LogTrace("DSRouterService : UnscribeRTUTags()");

            try
            {
                RemoveTags(request);

                /*
                 * для ускорения процесса разработки
                 * считаем что DS один и все затачиваем под него
                 * если ds несколько, то перебираем запрос и формируем списки
                 * поотдельности для каждого ds
                */
                IWcfDataServer iwds = dWCFClientsList.ElementAt(0).Value.wcfDataServer;
                lock (lockKey)
                {
                    iwds.UnscribeRTUTags(request.ToArray());
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.UnscribeRTUTags() : Исключение : " + ex.Message);
            }
        }

        #endregion

        #region Работа с пользователем

        /// <summary>
        /// Метод авторизации пользователя
        /// </summary>
        DSRouterUser IDSRouter.Authorization(string userName, string userPassword, Boolean isFirstEnter)
        {
            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    DSUser dsUser = null;
                    lock (dataServerProxy)
                    {
                        dsUser = dataServerProxy.Authorization(userName, userPassword);
                    }

                    if (dsUser == null)       
                        return null;

                    _currentUser = new DSRouterUser(dsUser);
                    return _currentUser;
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return null;
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
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить список групп пользователей
        /// </summary>
        List<DSRouterUserGroup> IDSRouter.GetUserGroupsList()
        {
            if (_currentUser == null)
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
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Создание группы пользователей
        /// </summary>
        bool IDSRouter.CreateUserGroup(string groupName, string groupComment, int groupRight)
        {
            if (_currentUser == null)
                return false;

            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    lock (dataServerProxy)
                    {
                        return dataServerProxy.CreateUserGroup(groupName, groupComment, groupRight);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Создание пользователя
        /// </summary>
        bool IDSRouter.CreateUser(string userName, string userPassword, string userComment, int userGroupID)
        {
            if (_currentUser == null)
                return false;

            try
            {
                #warning Необходимо определить "главный" DS, который будет выполнять авторизацию пользователя

                if (dWCFClientsList.ContainsKey(0))
                {
                    IWcfDataServer dataServerProxy = dWCFClientsList[0].wcfDataServer;

                    lock (dataServerProxy)
                    {
                        return dataServerProxy.CreateUser(userName, userPassword, userComment, userGroupID);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
            if (_currentUser == null)
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
                        Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                    }
                }

                #endregion
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
            if (_currentUser == null)
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

                    return DEFAULT_URL_TO_DIRECTORY_TO_SHARE_FILES + OscConverter.SaveOscillogrammToFile(DEFAULT_PATH_TO_DIRECTORY_TO_SHARE_FILES, dsOscillogram);

                    #endregion
                }
                else
                    throw new ArgumentException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить архивную информацию (аварии, уставки и т.д.) как словарь значений
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDSRouter.GetHistoricalDataByID(UInt16 dsGuid, Int32 dataID)
        {
            if (_currentUser == null)
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
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Получить архивную информацию (аварии, уставки и т.д.) как словарь значений
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDSRouter.GetHistoricalDataByEvent(DSRouterEventValue dsRouterEvent)
        {
            if (_currentUser == null)
                return null;

            try
            {
                UInt16 dsGuid = dsRouterEvent.DsGuid;

                if (dWCFClientsList.ContainsKey(dsGuid))
                {
                    IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                    lock (dsProxy)
                    {
                        return ConvertDsTagDictionaryToDsRouterDictionary(dsProxy.GetHistoricalDataByID(dsRouterEvent.EventID));
                    }
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
            if (_currentUser == null)
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
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Получить все не квитированные сообщения
        /// </summary>
        List<DSRouterEventValue> IDSRouter.GetNotReceiptedEvents()
        {
            if (_currentUser == null)
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
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
            if (_currentUser == null)
                return;

            foreach (var dsGuid in dWCFClientsList.Keys)
            {
                IWcfDataServer dsProxy = dWCFClientsList[dsGuid].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.ReceiptAllEvents(_currentUser.UserID, receiptComment);
                    }
                }
                catch (Exception ex)
                {
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Квитировать сообщения
        /// </summary>
        void IDSRouter.ReceiptEvents(List<DSRouterEventValue> eventValues, string receiptComment)
        {
            if (_currentUser == null)
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
                            dsProxy.ReceiptEvents(eventsByDs[dsGuid].ToArray(), _currentUser.UserID, receiptComment);
                        }
                    }
                    catch (Exception ex)
                    {
                        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                        Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
        Dictionary<string, DSRouterTagValue> IDSRouter.GetValuesFromSettingsSet(int settingsSetID, List<string> tagsList)
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
            if (_currentUser == null)
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
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
            if (_currentUser == null)
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
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
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
            if (_currentUser == null)
                return false;

            #warning Если вызывает этот метод один и тот же клиент без вызова Terminate.., то по хорошому бы разрешить ему передачу файла
            if (_lockFileUploadSessionId != null)
                return false;

            _dsGuidFileTransfer = dsGuid;
            _devGuidFileTransfer = devGuid;
            _fileNameFileTransfer = fileName;
            _fileCommentFileTransfer = comment;

            _lockFileUploadSessionId = CurrentСontext.SessionId;

            return true;
        }

        /// <summary>
        /// Загрузить кусок файла
        /// </summary>
        bool IDSRouter.UploadFileChunk(byte[] fileChunkBytes)
        {
            if (_currentUser == null || _lockFileUploadSessionId != CurrentСontext.SessionId)
                return false;

            if (dWCFClientsList.ContainsKey(_dsGuidFileTransfer))
            {
                var dsProxy = dWCFClientsList[_dsGuidFileTransfer].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        return dsProxy.UploadFileChunk(fileChunkBytes);
                    }                    
                }
                catch (Exception ex)
                {
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Сохранить загруженный файл
        /// </summary>
        string IDSRouter.SaveUploadedFile()
        {
            if (_currentUser == null)
                return null;

            if (_lockFileUploadSessionId != CurrentСontext.SessionId)
                return "Сервер данных занят.";

            bool result = false;

            if (dWCFClientsList.ContainsKey(_dsGuidFileTransfer))
            {
                var dsProxy = dWCFClientsList[_dsGuidFileTransfer].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        result = dsProxy.SaveUploadedFile(_devGuidFileTransfer, _currentUser.UserID, _fileNameFileTransfer,
                            _fileCommentFileTransfer);
                    }
                }
                catch (Exception ex)
                {
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }

            _lockFileUploadSessionId = null;

            if (result)
                return "Файл успешно записан в БД.";

            return "Во время записи произошла какая-то ошибка.";
        }

        /// <summary>
        /// Сбрасывает передачу файла
        /// </summary>
        void IDSRouter.TerminateUploadFileSession()
        {
            if (dWCFClientsList.ContainsKey(_dsGuidFileTransfer))
            {
                var dsProxy = dWCFClientsList[_dsGuidFileTransfer].wcfDataServer;

                try
                {
                    lock (dsProxy)
                    {
                        dsProxy.TerminateUploadFileSession();
                    }
                }
                catch (Exception ex)
                {
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }

            _lockFileUploadSessionId = null;
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

        /// <summary>
        /// Установить значение тега 
        /// с уровня HMI через тип object
        /// (качество тега vqHandled)
        /// </summary>
        void IDSRouter.SetTagValueFromHMI(UInt16 dsGuid, Int32 devGuid, Int32 tagGuid, object valinobject)
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
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// восстановить процесс естесвенного обновления тега
        /// (качество тега vqGood или по факту)
        /// </summary>
        void IDSRouter.ReSetTagValueFromHMI(UInt16 dsGuid, Int32 devGuid, Int32 tagGuid)
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
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                    Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                }
            }
        }

        #endregion

        #endregion

        #region Private - методы

        #region Вспомогательные методы для иницилизации роутера

        /// <summary>
        /// инициализация экземпляра 
        /// службы маршрутизатора запросов
        /// </summary>
        private void InitDSRouter()
        {
            Utilities.LogTrace("DSRouterService : InitDSRouter()");
            try
            {
                // Сформировать имена конфигурац файлов
                SetNamesCfgPrgFiles();

                #region фиксирование сессии (код ПР)
                CurrentСontext = OperationContext.Current;
                //currClient = CurrentСontext.GetCallbackChannel<IDSRouterCallback>();
                CurrentСontext.Channel.Faulted += Disconnect;
                CurrentСontext.Channel.Closed += Disconnect;
                #endregion

                lockKey = new object();

                ///*
                // * прочитать файл с конфигурацией DataServer'ов текущего проекта
                // * запустить клиентов для работы с каждым из DS по его guid
                // * инициализировать мультисессионный сервис для приема запроса клиентов                 * 
                // */

                #region иницализация таймера для перезапуска отвалившихся DS
                tmrDSRestart = new System.Timers.Timer();
                tmrDSRestart.Interval = 5000;
                tmrDSRestart.Elapsed += new ElapsedEventHandler(tmrDSRestart_Elapsed);
                tmrDSRestart.Stop();
                #endregion

                CreateDSList();
                CreateDSClientsList();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.InitDSRouter() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// Сформировать имена конфигурац файлов проекта
        /// </summary>
        public static void SetNamesCfgPrgFiles()
        {
            Utilities.LogTrace("DSRouterService : SetNamesCfgPrgFiles()");

            bool isPathFound = false;
            string path2cfgfiles = string.Empty;

            try
            {
                #region информацию о папке с конфиг параметрами извлекаем из реестра
                //// проверить существование ключа реестра
                //RegistryKey key = Registry.LocalMachine;
                //RegistryKey keysw = key.OpenSubKey("Software", true);
                //RegistryKey mtrakey = keysw.OpenSubKey("mtra");
                //RegistryKey mtrakeyds = null;

                //if (mtrakey == null)
                //{
                //    isPathFound = false;
                //}
                //else
                //    mtrakeyds = mtrakey.OpenSubKey("DSRouterService");

                //// путь к папке с ранее установленным ПО
                //string strmtrakeydskeypath = string.Empty;

                //if (mtrakeyds != null)
                //{
                //    isPathFound = true;

                //    /*
                //     * приложение DataServer уже ранее устанавливалось
                //     * поэтому читаем путь к его файлу запуска
                //     */
                //    //RegistryKey mtrakeydspartkey = mtrakey.OpenSubKey(idPTK);
                //    path2cfgfiles = (string)mtrakeyds.GetValue("mtraPathDSRouterServiceConfig");

                //    if (!Directory.Exists(path2cfgfiles))
                //    {
                //        //System.Windows.Forms.MessageBox.Show(string.Format("(83)Путь к файлу приложения недействителен : {0}.", path2dsexe), @"Запуск сервиса DataServer", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); ;
                //        isPathFound = false;
                //    }
                //}
                //else
                //    isPathFound = false;                

                //if (!isPathFound)
                //    return;
                #endregion

                path2cfgfiles = AppDomain.CurrentDomain.BaseDirectory + "Project";

                // проверяем существование файла конфигурации  проекта Project.cfg и файла конфигурации устройств проекта
                string PathToPrjFile = Path.Combine(path2cfgfiles, "Project.cfg");// AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar + "Project" + Path.DirectorySeparatorChar + "Project.cfg";

                if (!File.Exists(PathToPrjFile))
                {
                    //System.Windows.Forms.MessageBox.Show(string.Format("Нет доступа к конфигурации какого либо проекта, путь : {0} не существует. \nРабота программы будет прекрацена.", PathToPrjFile), @"(256)...\DataServerWPF\MainWindow.xaml.cs : CreateDescribeDeviceAsXDocument", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    Process.GetCurrentProcess().Kill();
                }

                HMI_MT_Settings.HMI_Settings.PathToPrjFile = PathToPrjFile;
                HMI_MT_Settings.HMI_Settings.XDoc4PathToPrjFile = XDocument.Load(HMI_MT_Settings.HMI_Settings.PathToPrjFile);

                // проверяем существование файла конфигурации устройств проекта Configuration.cfg 
                string PathToConfigurationFile = Path.Combine(path2cfgfiles, "Configuration.cfg"); //AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar + "Project" + Path.DirectorySeparatorChar + "Configuration.cfg";

                if (!File.Exists(PathToConfigurationFile))
                    throw new Exception(string.Format(@"(391) : ...\WCFDSService\WCFDSService\WcfDataServer.cs : SetNamesCfgPrgFiles() : Несуществующий файл = {0}", PathToConfigurationFile));

                HMI_MT_Settings.HMI_Settings.PathToConfigurationFile = PathToConfigurationFile;
                HMI_MT_Settings.HMI_Settings.XDoc4PathToConfigurationFile = XDocument.Load(HMI_MT_Settings.HMI_Settings.PathToConfigurationFile);

                // загрузить информацию об ошибках из файла ErrorList.xml
                // проверяем существование файла 
                string PathToErrorListFile = Path.Combine(path2cfgfiles, "ErrorList.xml");// AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar + "Project" + Path.DirectorySeparatorChar + "ErrorList.xml";

                if (!File.Exists(PathToErrorListFile))
                    throw new Exception(string.Format(@"(204) : ...\WCFDSService\WCFDSService\WcfDataServer.cs  : SetNamesCfgPrgFiles() : Несуществующий файл описания ошибок = {0}", PathToErrorListFile));

                // если файл есть - заполняем список
                XDocument xdocError = XDocument.Load(PathToErrorListFile);
                foreach (XElement kvp in xdocError.Element("ErrorList").Descendants("Error"))
                    if (!HMI_MT_Settings.HMI_Settings.SlListError.ContainsKey(kvp.Attribute("code").Value))
                        HMI_MT_Settings.HMI_Settings.SlListError.Add(kvp.Attribute("code").Value, kvp.Attribute("name").Value);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.SetNamesCfgPrgFiles() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// создать список DS из конфигурации проекта
        /// </summary>
        private void CreateDSList()
        {
            Utilities.LogTrace("DSRouterService : CreateDSList()");

            // список DS и описывающих их секций
            dsList = new Dictionary<UInt16, XElement>();
            try
            {
                // проверяем существование файла конфигурации  проекта Project.cfg и файла конфигурации устройств проекта
                string PathToPrjFile = AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar + "Project" + Path.DirectorySeparatorChar + "Project.cfg";

                if (!File.Exists(PathToPrjFile))
                    throw new Exception(string.Format(@"(152) : ...\DSRouterSolution\DSRouter\DSRouter.svc.cs : CreateDSList() : Несуществующий файл = {0}", PathToPrjFile));

                // проверяем существование файла конфигурации устройств проекта Configuration.cfg 
                string PathToConfigurationFile = AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar + "Project" + Path.DirectorySeparatorChar + "Configuration.cfg";

                if (!File.Exists(PathToConfigurationFile))
                    throw new Exception(string.Format("(391) : MainForm.cs : SetNamesCfgPrgFiles() : Несуществующий файл = {0}", PathToConfigurationFile));

                XDocument XDoc4PathToConfigurationFile = XDocument.Load(PathToConfigurationFile);

                if (XDoc4PathToConfigurationFile.Element("Project").Element("Configuration").Elements("Object").Count() > 0)
                    foreach (XElement xe in XDoc4PathToConfigurationFile.Element("Project").Element("Configuration").Elements("Object"))
                        dsList.Add(UInt16.Parse(xe.Attribute("UniDS_GUID").Value), xe);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.CreateDSList() : Исключение : " + ex.Message);
                Process.GetCurrentProcess().Kill();
            }
        }

        /// <summary>
        /// заполнить словарь соединений с DS
        /// при инициализации DSRouter
        /// </summary>
        private void CreateDSClientsList()
        {
            Utilities.LogTrace("DSRouterService : CreateDSClientsList()");
            try
            {
                foreach (KeyValuePair<UInt16, XElement> kvp in dsList)
                {
                    CreateWCFCL(kvp);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.CreateDSClientsList() : Исключение : " + ex.Message);
            }
        }

        private void CreateWCFCL(KeyValuePair<UInt16, XElement> kvp)
        {
            Utilities.LogTrace("DSRouterService : CreateWCFCL()");

            try
            {
                IWcfDataServer dsr = CreateWCFClient(kvp.Value);

                if (dsr != null)
                {
                    DSService dss = new DSService(kvp.Key, dsr);
                    if (!dWCFClientsList.ContainsKey(kvp.Key))
                        dWCFClientsList.Add(kvp.Key, dss);
                    else
                        dWCFClientsList[kvp.Key] = dss;

                    dss.OnDSSessionCancelled += new DSSessionCancelled(dss_OnDSSessionCancelled);
                    dWCFClientsList[kvp.Key].ConnectionState = true;
                }
                else
                {
                    /*
                     *  пытаемся соединиться через определенные периоды
                     *  когда служба будет доступна
                     */
                    // пытаемся перезапустить DS                       

                    if (dWCFClientsList.ContainsKey(kvp.Key))
                        dWCFClientsList[kvp.Key].ConnectionState = false;

                    tmrDSRestart.Start();
                    /*
                     * запускаем таймер на перезапуск 
                     * соединения с отвалившимися службами
                     */
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1743, ex.Message);
                Utilities.LogTrace("DSRouterService.CreateWCFCL() : Исключение : " + ex.Message);
            }
            Utilities.LogTrace("DSRouterService : CreateWCFCL()");
        }

        /// <summary>
        /// 
        /// </summary>
        private IWcfDataServer CreateWCFClient(XElement kvpValue)
        {
            Utilities.LogTrace("DSRouterService : CreateWCFClient()");

            string ipds = string.Empty;
            string port = string.Empty;
            WcfDataServerClient wcfDataServer = null;

            try
            {
                if (kvpValue.Element("DSAccessInfo").Element("binding").Element("IPAddress").Attributes("value").Count() != 0)
                    ipds = kvpValue.Element("DSAccessInfo").Element("binding").Element("IPAddress").Attribute("value").Value;
                else
                    ipds = "127.0.0.1";

                if (kvpValue.Element("DSAccessInfo").Element("binding").Element("Port").Attributes("value").Count() != 0)
                    port = kvpValue.Element("DSAccessInfo").Element("binding").Element("Port").Attribute("value").Value;
                else
                    port = "8732";

                #region инициализация wcf
                IPAddress ipa;
                string endPointAddr = "";

                if (!IPAddress.TryParse(ipds, out ipa))
                    throw new Exception(string.Format("Некорректный ip-адрес - {0}", ipds));

                endPointAddr = string.Format(@"net.tcp://{0}:{1}/WcfDataServer_Lib.WcfDataServer", ipds, port);

                NetTcpBinding tcpBinding = new NetTcpBinding();
                //tcpBinding.MaxReceivedMessageSize = int.MaxValue;//150000000;
                //tcpBinding.MaxBufferSize = int.MaxValue;//1500000;
                //tcpBinding.MaxBufferPoolSize = int.MaxValue;
                //tcpBinding.ReaderQuotas.MaxArrayLength = int.MaxValue;// 1500000;
                tcpBinding.ReceiveTimeout = new TimeSpan(0, 1, 0);
                tcpBinding.SendTimeout = new TimeSpan(0, 1, 0);
                //tcpBinding.Security.Mode = SecurityMode.None;
                tcpBinding.MaxReceivedMessageSize = Int32.MaxValue;                

                //tcpBinding.TransactionFlow = false;
                //tcpBinding.Security.Transport.ProtectionLevel = System.Net.Security.ProtectionLevel.EncryptAndSign;
                //tcpBinding.Security.Transport.ClientCredentialType = TcpClientCredentialType.Windows;
                //tcpBinding.Security.Mode = SecurityMode.None;

                EndpointAddress endpointAddress = new EndpointAddress(endPointAddr);

                DSServiceCallback cbh = new DSServiceCallback();
                cbh.OnNewError += new NewError(cbh_OnNewError);
                cbh.OnNewTagValues += new NewTagValues(cbh_OnNewTagValues);
                cbh.OnCMDExecuted += new CMDExecuted(cbh_OnCMDExecuted);

                InstanceContext ic = new InstanceContext(cbh);

                wcfDataServer = new WcfDataServerClient(ic, tcpBinding, endpointAddress);

                wcfDataServer.RegisterForErrorEvent(kvpValue.Attribute("UniDS_GUID").Value);
                #endregion

                #region восстанавливаем подписку если соединение прерывалось только между dsr и ds
                // общий список подписанных тегов по клиентам
                SortedDictionary<string, Tuple<DSRouterTagValue, bool>> lstfull = new SortedDictionary<string, Tuple<DSRouterTagValue, bool>>();

                foreach (KeyValuePair<string, Tuple<DSRouterTagValue, bool>> tag in GetSubscribeTags())
                    if (!lstfull.ContainsKey(tag.Key))
                        lstfull.Add(tag.Key, new Tuple<DSRouterTagValue, bool>(tag.Value.Item1, tag.Value.Item2));

                if (lstfull.Count > 0)
                {
                    /*
                         * для ускорения процесса разработки
                         * считаем что DS один и все затачиваем под него
                         * если ds несколько, то перебираем запрос и формируем списки
                         * поотдельности для каждого ds
                         */
                    wcfDataServer.SubscribeRTUTags(lstfull.Keys.ToArray());
                }
                #endregion
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1857, ex.Message);
                Utilities.LogTrace("DSRouterService.CreateWCFClient() : Исключение : " + ex.Message);

                wcfDataServer = null;
            }

            Utilities.LogTrace("DSRouterService : CreateWCFClient()");

            return wcfDataServer;
        }

        #endregion

        #region Работа с DS

        /// <summary>
        /// Пересоздание соединения с конкретным DS
        /// </summary>
        private void CreateDSConnection(UInt16 numds)
        {
            Utilities.LogTrace("DSRouterService : CreateDSConnection()");
            try
            {
                // пытаемся перезапустить конкретный DS
                foreach (KeyValuePair<UInt16, XElement> kvp in dsList)
                {
                    if (kvp.Key == numds)
                        CreateWCFCL(kvp);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.CreateDSConnection() : Исключение : " + ex.Message);
            }
        }

        #endregion

        #region Вспомогательные методы для работы роутера

        /// <summary>
        /// удаление тегов из списка
        /// запрашиваемых тегов
        /// </summary>
        /// <param name="lstTags"></param>
        public void RemoveTags(List<string> lstTags)
        {
            try
            {
                lock (lockKey)
                {
                    foreach (string strid in lstTags)
                        if (lstSubscribeTagsInHMIContext.ContainsKey(strid))
                            lstSubscribeTagsInHMIContext.Remove(strid);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        /// <summary>
        /// получить список подписанных тегов
        /// </summary>
        /// <returns></returns>
        public SortedDictionary<string, Tuple<DSRouterTagValue, bool>> GetSubscribeTags()
        {
            return lstSubscribeTagsInHMIContext;
        }

        /// <summary>
        /// получить значение тега
        /// </summary>
        /// <returns></returns>
        public DSRouterTagValue GetDSTagValue(string tagid)
        {
            DSRouterTagValue rez = null;
            try
            {
                lock (lockKey)
                {
                    if (lstSubscribeTagsInHMIContext.ContainsKey(tagid))
                    {
                        rez = lstSubscribeTagsInHMIContext[tagid].Item1;
                        // сбрасываем признак новизны
                        lstSubscribeTagsInHMIContext[tagid] = new Tuple<DSRouterTagValue, bool>(lstSubscribeTagsInHMIContext[tagid].Item1, false);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
            return rez;
        }

        /// <summary>
        /// сбросить флог обновления
        /// </summary>
        /// <param name="tgkey"></param>
        public void ResetReNewFlag(string tgkey)
        {
            try
            {
                lock (lockKey)
                {
                    if (lstSubscribeTagsInHMIContext.ContainsKey(tgkey))
                        lstSubscribeTagsInHMIContext[tgkey] = new Tuple<DSRouterTagValue, bool>(lstSubscribeTagsInHMIContext[tgkey].Item1, false);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public bool IsContainsTag(string idtag)
        {
            bool rez = false;

            try
            {
                if (lstSubscribeTagsInHMIContext.ContainsKey(idtag))
                    rez = true;
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
            return rez;
        }

        /// <summary>
        /// установить значение тега
        /// </summary>
        /// <returns></returns>
        public void SetDSTagValue(string tagid, DSRouterTagValue dst)
        {
            try
            {
                lock (lockKey)
                {
                    if (lstSubscribeTagsInHMIContext.ContainsKey(tagid))
                        lstSubscribeTagsInHMIContext[tagid] = new Tuple<DSRouterTagValue, bool>(dst, true);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
        public void AddTags(List<string> lstTags)
        {
            try
            {
                lock (lockKey)
                {
                    foreach (string strid in lstTags)
                        if (!lstSubscribeTagsInHMIContext.ContainsKey(strid))
                            lstSubscribeTagsInHMIContext.Add(strid, new Tuple<DSRouterTagValue, bool>(new DSRouterTagValue(), false));
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        /// <summary>
        /// Установить флаг обновления
        /// </summary>
        /// <param name="tgkey"></param>
        public void SetReNewFlag(string tgkey)
        {
            try
            {
                lock (lockKey)
                {
                    if (lstSubscribeTagsInHMIContext.ContainsKey(tgkey))
                        lstSubscribeTagsInHMIContext[tgkey] = new Tuple<DSRouterTagValue, bool>(lstSubscribeTagsInHMIContext[tgkey].Item1, true);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        /// <summary>
        /// Установить плохое качество тега
        /// </summary>
        /// <param name="tgkey"></param>
        public void SetBadQuality(string tgkey)
        {
            try
            {
                lock (lockKey)
                {
                    if (lstSubscribeTagsInHMIContext.ContainsKey(tgkey))
                    {
                        lstSubscribeTagsInHMIContext[tgkey].Item1.VarQuality = 0;
                        lstSubscribeTagsInHMIContext[tgkey] = new Tuple<DSRouterTagValue, bool>(lstSubscribeTagsInHMIContext[tgkey].Item1, true);
                    }
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        /// <summary>
        /// установить плохое качество тегам
        /// определенного DS по причине того, что 
        /// он перестал пинговаться
        /// </summary>
        /// <param name="numds"></param>
        private void SetBadQulity4DSTags(ushort numds)
        {
            /*
             * для всех тегов отвалившегося DS
             * нужно поставить плохое качество 
             * и установить признак обновления
             */
            Dictionary<string, DSRouterTagValue> tagsDataUpdated = new Dictionary<string, DSRouterTagValue>();

            try
            {
                var sessionID = CurrentСontext.SessionId;

                try
                {
                    lock (lockKey)
                    {
                        for (int i = 0; i < GetSubscribeTags().Count; i++)
                        {
                            KeyValuePair<string, Tuple<DSRouterTagValue, bool>> kvp = GetSubscribeTags().ElementAt(i);
                            // выделим номер DS
                            if (ushort.Parse(kvp.Key.Split(new char[] { '.' })[0]) == numds)
                            {
                                SetReNewFlag(kvp.Key);
                                SetBadQuality(kvp.Key);
                                tagsDataUpdated.Add(kvp.Key, GetDSTagValue(kvp.Key));

                            }
                        }
                    }
                }
                catch (Exception exx)
                {
                    TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(exx);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
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
                EventSourceName = "Сервер данных",
                EventText = eventText,
                EventSourceComment = "DSRouter"
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
        /// Закрывает сеанс передачи данных
        /// </summary>
        void CloseFileUploadSession()
        {
            if (_lockFileUploadSessionId != null)
            {
                if (dWCFClientsList.ContainsKey(_dsGuidFileTransfer))
                {
                    var dsProxy = dWCFClientsList[_dsGuidFileTransfer].wcfDataServer;

                    try
                    {
                        lock (dsProxy)
                        {
                            dsProxy.TerminateUploadFileSession();
                        }
                    }
                    catch (Exception ex)
                    {
                        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                        Utilities.LogTrace("DSRouterService.GetTagsValue() : Исключение : " + ex.Message);
                    }
                }

                _lockFileUploadSessionId = null;
            }
        }

        #endregion

        #endregion

        #region Handlers

        #region Обработчики событий, связанные с DataServer'ом

        #region Обработчики событий потери связи и ошибок

        /// <summary>
        /// событие пропадания связи с DS
        /// </summary>
        /// <param name="numds"></param>
        void dss_OnDSSessionCancelled(ushort numds)
        {
            Utilities.LogTrace("DSRouterService : dss_OnDSSessionCancelled() - событие пропадания связи с DS");

            try
            {
                // пытаемся перезапустить DS
                dWCFClientsList[numds].ConnectionState = false;
                /*
                 * установить плохое качество тегам
                 * определенного DS по причине того, что 
                 * он перестал пинговаться
                 */
                SetBadQulity4DSTags(numds);

                CreateDSConnection(numds);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.dss_OnDSSessionCancelled() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// вызывается когда от DS приходит ошибка
        /// </summary>
        /// <param name="strerror"></param>
        void cbh_OnNewError(string strerror)
        {
            Utilities.LogTrace("DSRouterService : cbh_OnNewError() - вызывается когда от DS приходит ошибка");

            try
            {
                currClient.NewErrorEvent(strerror);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.cbh_OnNewError() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// Разъединение c DataServer'ом
        /// </summary>
        public void Disconnect(object sender, EventArgs e)
        {
            Utilities.LogTrace("DSRouterService : Disconnect()");

            lock (lockKey)
            {
                try
                {
                    #region отписаться от тегов
                    // список подписанных тегов
                    List<string> lstSubscrTags = new List<string>();

                    for (int i = 0; i < GetSubscribeTags().Count; i++)
                    {
                        KeyValuePair<string, Tuple<DSRouterTagValue, bool>> kvp = GetSubscribeTags().ElementAt(i);
                        lstSubscrTags.Add(kvp.Key);
                    }

                    // удалим теги запроса
                    RemoveTags(lstSubscrTags);

                    /*
                     * для ускорения процесса разработки
                     * считаем что DS один и все затачиваем под него
                     * если ds несколько, то перебираем запрос и формируем списки
                     * поотдельности для каждого ds
                    */
                    IWcfDataServer iwds = dWCFClientsList.ElementAt(0).Value.wcfDataServer;
                    lock (lockKey)
                    {
                        iwds.UnscribeRTUTags(lstSubscrTags.ToArray());
                    }
                    #endregion отписаться от тегов

                    if (CurrentСontext != null)
                    {
                        Utilities.LogTrace("DSRouterService.Disconnect() : Клиент отсоединился");
                        TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 93, string.Format("{0} : {1} : {2} : Клиент отсоединился.", DateTime.Now.ToString(), @"X:\Projects\00_DataServer\WCFDSService\WCFDSService\WcfDataServer.cs", "Disconnect()()"));
                    }
                    else
                    {
                        //Utilities.LogTrace(" - OperationContext.Current=null Видимо уже убили...");
                    }
                }
                catch (Exception ex)
                {
                    //TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 184, string.Format("{0} : {1} : {2} : ОШИБКА: {3}", DateTime.Now.ToString(), @"X:\Projects\00_DataServer\WCFDSService\WCFDSService\WcfDataServer.cs", "Disconnect()()", ex.Message));
                    Utilities.LogTrace("DSRouterService.Disconnect() : Исключение : " + ex.Message);
                }
            }
        }

        /// <summary>
        /// таймер для перезапуска отвалившихся DS
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void tmrDSRestart_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                tmrDSRestart.Stop();

                Utilities.LogTrace("DSRouterService : tmrDSRestart_Elapsed()");

                /*
                 * здесь неточность:
                 * пока DS один - все нормально, 
                 * когда DS будет несколько, нужно будет добавить механизм
                 * выборочного перезапуска
                 */

                #region создание dsList при запуске DSR
                if (dWCFClientsList.Count == 0)
                    CreateDSClientsList();
                #endregion

                List<DSService> lstDS4Restart = new List<DSService>();

                foreach (KeyValuePair<UInt16, DSService> kvp in dWCFClientsList)
                    if (kvp.Value.ConnectionState == false)
                    {
                        lstDS4Restart.Add(kvp.Value);
                    }

                foreach (DSService dss in lstDS4Restart)
                    CreateDSConnection(dss.dsUID);//kvp.Key
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.tmrDSRestart_Elapsed() : Исключение : " + ex.Message);
            }
        }

        #endregion

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
                Utilities.LogTrace("DSRouterService.cbh_OnCMDExecuted() : Исключение : " + ex.Message);
            }
        }

        /// <summary>
        /// вызывается по приходу обновленных 
        /// тегов от DS
        /// </summary>
        /// <param name="strerror"></param>
        void cbh_OnNewTagValues(Dictionary<string, DSTagValue> lstrenewtags)
        {
            Utilities.LogTrace("DSRouterService : cbh_OnNewTagValues()");

            try
            {
                /*
                 * обновление работает на два механизма подписки - старый и новый:
                 * для старого механизма:
                 *      анализируем какие клиенты подписаны 
                 *      на эти теги, формируем списки и отправлем
                 *      обновленные теги клиентам
                 * для нового механизма:
                 *      в контексте сессии запоминаем значение тега и
                 *      выставляем признак того, что он был обновлен
                 */
                Dictionary<string, DSRouterTagValue> lsttags4Subscr = new Dictionary<string, DSRouterTagValue>();

                foreach (KeyValuePair<string, DSTagValue> kvptv in lstrenewtags)
                    if (IsContainsTag(kvptv.Key))
                    {
                        DSRouterTagValue dstv = new DSRouterTagValue();
                        dstv.VarQuality = kvptv.Value.VarQuality;
                        dstv.VarValueAsObject = kvptv.Value.VarValueAsObject;

                        lsttags4Subscr.Add(kvptv.Key, dstv);

                        // для нового механизма
                        lock (lockKey)
                        {
                            SetDSTagValue(kvptv.Key, dstv);
                        }
                    }

                lock (lockKey)
                {
                    currClient.NotifyChangedTags(lsttags4Subscr);
                    Utilities.LogTrace("DSRouterService.cbh_OnNewTagValues() : 2 :" + CurrentСontext.Channel.State.ToString());
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
                Utilities.LogTrace("DSRouterService.cbh_OnNewTagValues() : Исключение : " + ex.Message);
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

            //UnsubscribeTagsFromDs();

            
        }

        #endregion


        #endregion
    }

}
