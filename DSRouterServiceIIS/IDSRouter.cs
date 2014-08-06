using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.ServiceModel;
using DSRouterServiceIIS.DSServiceReference;

namespace DSRouterServiceIIS
{
    [ServiceContract(CallbackContract = typeof(IDSRouterCallback), SessionMode = SessionMode.Required)]
    //[ServiceKnownType(typeof(EnumerationCommandStates))]    
    public interface IDSRouter
    {
        #region Старые функции

        #region поддержка функционирования старого арма - обмен пакетами

        /// <summary>
        /// запрос данных в виде пакета
        /// </summary>
        [OperationContract]
        byte[] GetDSValueAsByteBuffer(UInt16 DSGuid, byte[] arr);

        /// <summary>
        /// запрос осциллограммы по ее 
        /// id в базе
        /// </summary>
        [OperationContract]
        byte[] GetDSOscByIdInBD(UInt16 DSGuid, byte[] arr);

        /// <summary>
        /// запрос архивной по ее 
        /// id в базе
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void SetReq2ArhivInfo(UInt16 DSGuid, byte[] arr);

        /// <summary>
        /// выполнить команду
        /// </summary>
        [OperationContract]
        byte[] RunCMDMOA(UInt16 DSGuid, byte[] pq);

        /// <summary>
        /// выполнить команду по ее TagGUID
        /// </summary>
        /// <param name="dsdevTagGUID">ид команды в формате ds.dev.tagguid</param>
        /// <param name="pq">массив параметров</param>
        /// <returns>успешность запуска команды</returns>
        [OperationContract]
        bool RunCMD(string dsdevTagGUID, byte[] pq);

        #endregion

        #region Получение данных о конфигурации

        /// <summary>
        /// информация о конфигурации DataServer
        /// в виде файла
        /// </summary>       
        [OperationContract]
        Stream GetDSConfigFile(UInt16 DSGuid);

        /// <summary>
        /// Информация об DataServer’ах
        /// </summary>
        [OperationContract]
        string GetDSGUIDs();

        /// <summary>
        /// информация об имени DataServer 
        /// </summary>
        [OperationContract]
        string GetDSINFO(UInt16 DSGuid);

        /// <summary>
        /// список идентификаторов источников
        /// указанного DataServer 
        /// </summary>
        [OperationContract]
        string GetSourceGUIDs(UInt16 DSGuid);

        /// <summary>
        /// возвращает имя указанного 
        /// источника указанного DataServer
        /// </summary>
        [OperationContract]
        string GetSourceName(UInt16 DSGuid, UInt16 SrcGuid);

        /// <summary>
        /// список идентификаторов контроллеров 
        /// источника SrcGuid DataServer DSGuid 
        /// в формате EcuGuid; EcuGuid; EcuGuid
        /// </summary>
        [OperationContract]
        string GetECUGUIDs(UInt16 DSGuid, UInt16 SrcGuid);

        /// <summary>
        /// возвращает имя контролллера EcuGuid 
        /// источника SrcGuid для  DataServer DSGuid
        /// </summary>
        [OperationContract]
        string GetECUName(UInt16 DSGuid, UInt16 SrcGuid, UInt16 EcuGuid);

        /// <summary>
        /// список идентификаторов устройств 
        /// контроллера EcuGuid источника SrcGuid DataServer DSGuid 
        /// в формате RtuGuid; RtuGuid; RtuGuid…
        /// </summary>
        [OperationContract]
        string GetSrcEcuRTUGUIDs(UInt16 DSGuid, UInt16 SrcGuid, UInt16 EcuGuid);

        /// <summary>
        /// список идентификаторов устройств DataServer
        /// DSGuid в формате RtuGuid; RtuGuid; RtuGuid…
        /// </summary>
        [OperationContract]
        string GetRTUGUIDs(UInt16 DSGuid);

        /// <summary>
        /// признак доступности устройства для обработки
        /// </summary>
        [OperationContract]
        bool IsRTUEnable(UInt16 DSGuid, UInt32 RtuGuid);

        /// <summary>
        /// строка описания устройства
        /// </summary>
        [OperationContract]
        string GetRTUDescription(UInt16 DSGuid, UInt32 RtuGuid);

        /// <summary>
        /// список групп первого уровня 
        /// в формате GroupGuid; GroupGuid; GroupGuid…
        /// </summary>
        [OperationContract]
        string GetGroupGUIDs(UInt16 DSGuid, UInt32 RtuGuid);

        /// <summary>
        /// признак доступности группы для обработки.
        /// </summary>
        [OperationContract]
        bool IsGroupEnable(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid);

        /// <summary>
        /// имя группы GroupGuid
        /// устройства RtuGuid
        /// </summary>
        [OperationContract]
        string GetRTUGroupName(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid);

        /// <summary>
        /// список подгрупп группы(подгруппы) 
        /// (Sub)GroupGuid в формате 
        /// SubGroupGuid; SubGroupGuid; SubGroupGuid…
        /// </summary>
        [OperationContract]
        string GetSubGroupGUIDsInGroup(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid);

        /// <summary>
        /// список тегов группы GroupGuid 
        /// устройства RtuGuid в формате 
        /// TagGuid; TagGuid; TagGuid…
        /// </summary>
        [OperationContract]
        string GetRtuGroupTagGUIDs(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid);

        /// <summary>
        /// информация о имени и типе тега 
        /// в формате имя_тега;тип_тега
        /// </summary>
        [OperationContract]
        string GetRTUTagName(UInt16 DSGuid, UInt32 RtuGuid, UInt32 GroupGuid, UInt32 TagGUID);

        #endregion

        #region функции общего назначения

        /// <summary>
        /// получить список последних ошибок при обмене с DS.
        /// Формат кода ошибки: codeerror@timestamp 
        /// </summary>
        [OperationContract]
        LstError GetDSLastErrorsGUID(UInt16 DSGuid);

        /// <summary>
        /// получить код последней ошибки при обмене с DS
        /// </summary>
        [OperationContract]
        string GetDSLastErrorGUID(UInt16 DSGuid);

        /// <summary>
        /// получить код последней ошибки 
        /// при обмене с DS по ее коду
        /// </summary>
        [OperationContract]
        string GetDSErrorTextByErrorGUID(UInt16 DSGuid, string errorGUID);

        /// <summary>
        /// квитировать (очистить) стек ошибок
        /// </summary>
        /// <param name="DSGuid"></param>
        [OperationContract(IsOneWay = true)]
        void AcknowledgementOfErrors(UInt16 DSGuid);

        /// <summary>
        /// регистрация клиента для механизма
        /// callback оповещения о новой ошибке
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void RegisterForErrorEvent(string keyticker);

        /// <summary>
        /// Пустой запрос для поддержания активности
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void Ping();

        #endregion

        #endregion

        #region Текущие данные

        /// <summary>
        /// Запрос всех тегов и одновременно подписка на изменения тегов
        /// </summary>
        [OperationContract]
        Dictionary<string, DSRouterTagValue> GetTagsValue(List<string> ATagIDsList);

        /// <summary>
        /// Запрос на получение тегов, чьи значения изменились
        /// </summary>
        [OperationContract]
        Dictionary<string, DSRouterTagValue> GetTagsValuesUpdated();

        #endregion

        #region Работа с пользователем

        /// <summary>
        /// Метод авторизации пользователя
        /// </summary>
        [OperationContract]
        RouterAuthResult Authorization(string userName, string userPassword, Boolean isFirstEnter);

        /// <summary>
        /// Получить список пользователей
        /// </summary>
        [OperationContract]
        List<DSRouterUser> GetUsersList();

        /// <summary>
        /// Получить список групп пользователей
        /// </summary>
        [OperationContract]
        List<DSRouterUserGroup> GetUserGroupsList();

        /// <summary>
        /// Создание группы пользователей
        /// </summary>
        [OperationContract]
        Boolean CreateUserGroup(string groupName, string groupComment, string groupRight);

        /// <summary>
        /// Создание пользователя
        /// </summary>
        [OperationContract]
        Boolean CreateUser(string userName, string userPassword, string userComment, Int32 userGroupID);

        #endregion

        #region Работа с событиями

        #region Запрос событий

        /// <summary>
        /// Получение событий
        /// </summary>
        [OperationContract]        
        List<DSRouterEventValue> GetEvents(DateTime dateTimeFrom, DateTime dateTimeTo, bool needSystemEvents, bool needUserEvents, bool needTerminalEvents, List<Tuple<UInt16, UInt32>> requestDevicesList);

        #endregion

        #region Получение данных событий

        /// <summary>
        /// Получить ссылку на осциллограмму по её номеру
        /// </summary>
        [OperationContract]
        string GetOscillogramAsUrlByID(UInt16 dsGuid, Int32 eventDataID);

        /// <summary>
        /// Получить zip архив с осциллограммами как кортеж массива байтов и имени архива
        /// </summary>
        [OperationContract]
        Tuple<byte [], string> GetOscillogramAsByteArray(UInt16 dsGuid, Int32 eventDataID);

        /// <summary>
        /// Получить архивную информацию (аварии, уставки и т.д.) как словарь значений
        /// </summary>
        [OperationContract]
        Dictionary<string, DSRouterTagValue> GetHistoricalDataByID(UInt16 dsGuid, Int32 dataID);

        #endregion

        #region Работа с квитированием

        #region По конкретному DS
        // Убрано до лучших времен

        ///// <summary>
        ///// Проверяет есть ли не квитированные сообщения на конкретном DS
        ///// </summary>
        //[OperationContract]
        //Boolean IsNotReceiptedEventsExistAtDS(UInt16 dsGuid);

        ///// <summary>
        ///// Получить не квитированные сообщения от конкретного DS
        ///// </summary>
        //[OperationContract]
        //List<DSRouterEventValue> GetNotReceiptedEvents(UInt16 dsGuid);

        ///// <summary>
        ///// Квитировать все собщения конкретного DS
        ///// </summary>
        //[OperationContract]
        //void ReceiptAllEventsAtDS(UInt16 dsGuid, String receiptComment);

        #endregion

        #region По всем DS

        /// <summary>
        /// Проверяет есть ли не квитированные сообщения
        /// </summary>
        [OperationContract]
        Boolean IsNotReceiptedEventsExist();

        /// <summary>
        /// Получить все не квитированные сообщения
        /// </summary>
        [OperationContract]
        List<DSRouterEventValue> GetNotReceiptedEvents();

        /// <summary>
        /// Квитировать все собщения
        /// </summary>
        [OperationContract]
        void ReceiptAllEvents(String receiptComment);

        /// <summary>
        /// Квитировать сообщения
        /// </summary>
        [OperationContract]
        void ReceiptEvents(List<DSRouterEventValue> eventValues, String receiptComment);

        #endregion

        #endregion

        #endregion

        #region Уставки

        /// <summary>
        /// Получение списка архивных записей уставок для устройства
        /// </summary>
        [OperationContract]
        List<DSRouterSettingsSet> GetSettingsSetsList(UInt16 dsGuid, UInt32 devGuid);

        /// <summary>
        /// Получение значений для указанных тегов из конкретного архивного набора уставок
        /// </summary>
        [OperationContract]
        Dictionary<String, DSRouterTagValue> GetValuesFromSettingsSet(UInt16 dsGuid, Int32 settingsSetID);

        /// <summary>
        /// Запись набора уставкок в устройство
        /// </summary>
        [OperationContract]
        void SaveSettingsToDevice(UInt16 dsGuid, UInt32 devGuid, Dictionary<string, DSRouterTagValue> tagsValues);

        #endregion

        #region Комманды

        /// <summary>
        /// Запрос на запуск команды на устройстве
        /// </summary>
        [OperationContract]
        void CommandRun(UInt16 dsGuid, UInt32 devGuid, string commandID, Object[] parameters);

        #endregion

        #region Работа с документами

        #region Методы для работы с существующими документами

        /// <summary>
        /// Получить список документов терминала
        /// </summary>
        [OperationContract]
        List<DSRouterDocumentDataValue> GetDocumentsList(UInt16 dsGuid, Int32 devGuid);

        /// <summary>
        /// Получить ссылку на документ
        /// </summary>
        [OperationContract]
        string GetDocumentByID(UInt16 dsGuid, Int32 documentId);

        #endregion

        #region Методы для загрузки документов

        /// <summary>
        /// Иницилизировать передачу файлов
        /// </summary>
        [OperationContract]
        bool InitUploadFileSession(UInt16 dsGuid, Int32 devGuid, string fileName, string comment);

        /// <summary>
        /// Загрузить кусок файла
        /// </summary>
        [OperationContract]
        bool UploadFileChunk(byte[] fileChunkBytes);

        /// <summary>
        /// Сохранить загруженный файл
        /// </summary>
        [OperationContract]
        string SaveUploadedFile();

        /// <summary>
        /// Сбрасывает передачу файла
        /// </summary>
        [OperationContract]
        void TerminateUploadFileSession();

        #endregion

        #endregion

        #region Время

        /// <summary>
        /// Получить текущее время роутера
        /// </summary>
        [OperationContract]
        DateTime GetCurrentDateTime();

        #endregion

        #region Ручной ввод данных

        #region Ручной ввод значений тегов

        /// <summary>
        /// Установить значение тега 
        /// с уровня HMI через тип object
        /// (качество тега vqHandled)
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void SetTagValueFromHMI(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid, object valinobject);

        /// <summary>
        /// восстановить процесс естесвенного обновления тега
        /// (качество тега vqGood или по факту)
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void ReSetTagValueFromHMI(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid);

        #endregion

        #region Ручной ввод преобразовывающих коэффициентов

        /// <summary>
        /// Получить коэффициент преобразования для тега
        /// </summary>
        [OperationContract]
        Object GetTagAnalogTransformationRatio(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid);

        /// <summary>
        /// Установить коэффициент преобразования
        /// </summary>
        [OperationContract]
        void SetTagAnalogTransformationRatio(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid, Object transformationRatio);

        /// <summary>
        /// Сбросить коэффициент преобразования
        /// </summary>
        [OperationContract]
        void ReSetTagAnalogTransformationRatio(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid);

        /// <summary>
        /// Возвращает true, если значение дискретного тега инверсируется
        /// </summary>
        [OperationContract]
        bool? IsInverseDiscretTag(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid);

        /// <summary>
        /// Инверсирует значение дискретного тега
        /// </summary>
        [OperationContract]
        void InverseDiscretTag(UInt16 dsGuid, UInt32 devGuid, UInt32 tagGuid, Boolean newInverseValue);

        #endregion

        #endregion

        #region Тренды

        /// <summary>
        /// Получить список тегов, у которых включена запись значений
        /// </summary>
        [OperationContract]
        List<string> GetTagsListWithEnabledTrendSave();

        /// <summary>
        /// Получить доступные диапозоны значений тренда
        /// </summary>
        [OperationContract]
        List<Tuple<DateTime, DateTime>> GetTrendDateTimeRanges(ushort dsGuid, uint devGuid, uint tagGuid);

            /// <summary>
        /// Получить тренд единым списком
        /// </summary>
        [OperationContract]
        List<Tuple<DateTime, object>> GetTagTrend(ushort dsGuid, uint devGuid, uint tagGuid, DateTime startDateTime, DateTime endDateTime);

        /// <summary>
        /// Получить настройки режима работы записи тренда
        /// </summary>
        [OperationContract]
        DSRouterTrendSettings GetTrendSettings(ushort dsGuid, uint devGuid, uint tagGuid);

        /// <summary>
        /// Установить настройки режима работы записи тренда
        /// </summary>
        [OperationContract]
        void SetTrendSettings(ushort dsGuid, uint devGuid, uint tagGuid, DSRouterTrendSettings trendSettings);

        #endregion
    }

    #region DataContracts

    #region Tags

    /// <summary>
    /// контракт данных
    /// </summary>
    [DataContract]
    public class DSRouterTagValue
    {
        /// <summary>
        /// качество тега
        /// </summary>
        [DataMember]
        public uint VarQuality
        {
            get { return varquality; }
            set { varquality = value; }
        }
        uint varquality = 0;

        /// <summary>
        /// значение тега в Object
        /// </summary>
        [DataMember]
        public object VarValueAsObject
        {
            get { return varvalueasobject; }
            set { varvalueasobject = value; }
        }
        object varvalueasobject = 0;

        /// <summary>
        /// Конструктор по-умолчанию
        /// </summary>
        public DSRouterTagValue()
        {
        }

        /// <summary>
        /// Конструктор
        /// </summary>
        public DSRouterTagValue(DSTagValue dsTagValue)
        {
            VarQuality = dsTagValue.VarQuality;
            VarValueAsObject = dsTagValue.VarValueAsObject;
        }
    }

    #endregion

    #region События

    /// <summary>
    /// Тип данных, которые могут быть привязаны к событию
    /// </summary>
    [DataContract]
    public enum DSRouterEventDataType
    {
        [EnumMember]
        None = -1,
        [EnumMember]
        Ustavki = 1,
        [EnumMember]
        Alarm = 2,
        [EnumMember]
        Oscillogram = 3
    }

    /// <summary>
    /// Класс, описывающий событие
    /// </summary>
    [DataContract]
    public class DSRouterEventValue
    {
        /// <summary>
        /// Номер DS, которому принадлежит данное событие
        /// </summary>
        [DataMember]
        public UInt16 DsGuid { get; set; }

        /// <summary>
        /// Номер устройства, которому принадлежит событие. Для не терминальных событий = -1
        /// </summary>
        [DataMember]
        public UInt32 DevGuid { get; set; }

        /// <summary>
        /// Идентификатор события
        /// </summary>
        [DataMember]
        public Int32 EventID { get; set; }

        /// <summary>
        /// Имя источника события
        /// </summary>
        [DataMember]
        public String EventSourceName { get; set; }

        /// <summary>
        /// Комментарий источника события
        /// </summary>
        [DataMember]
        public String EventSourceComment { get; set; }

        /// <summary>
        /// Текст события
        /// </summary>
        [DataMember]
        public String EventText { get; set; }

        /// <summary>
        /// Время события
        /// </summary>
        [DataMember]
        public DateTime EventTime { get; set; }

        /// <summary>
        /// Идентификатор данных, привязанных к событию
        /// </summary>
        [DataMember]
        public Int32 EventDataID { get; set; }

        /// <summary>
        /// Тип данных, привязанных к событию
        /// </summary>
        [DataMember]
        public DSRouterEventDataType EventDataType { get; set; }

        /// <summary>
        /// Нужно ли квитирование событие
        /// </summary>
        [DataMember]
        public Boolean IsNeedReceipt { get; set; }

        /// <summary>
        /// Квитировано ли событие
        /// </summary>
        [DataMember]
        public Boolean IsReceipted { get; set; }

        /// <summary>
        /// Сообщение квитирования
        /// </summary>
        [DataMember]
        public String ReceiptMessage { get; set; }

        /// <summary>
        /// Имя пользователя, квитировавшего сообщение
        /// </summary>
        [DataMember]
        public String ReceiptUser { get; set; }

        /// <summary>
        /// Время квитирования сообщения
        /// </summary>
        [DataMember]
        public DateTime ReceiptTime { get; set; }

        /// <summary>
        /// Конструктор
        /// </summary>
        public DSRouterEventValue()
        {
        }

        /// <summary>
        /// Конструктор
        /// </summary>
        public DSRouterEventValue(UInt16 dsGuid, DSEventValue dsEventValue)
        {
            DsGuid = dsGuid;
            DevGuid = dsEventValue.DevGuid;
            EventID = dsEventValue.EventID;
            EventSourceName = dsEventValue.EventSourceName;
            EventSourceComment = dsEventValue.EventSourceComment;
            EventText = dsEventValue.EventText;
            EventTime = dsEventValue.EventTime;
            EventDataID = dsEventValue.EventDataID;
            IsNeedReceipt = dsEventValue.IsNeedReceipt;
            IsReceipted = dsEventValue.IsReceipted;
            ReceiptMessage = dsEventValue.ReceiptMessage;
            ReceiptUser = dsEventValue.ReceiptUser;
            ReceiptTime = dsEventValue.ReceiptTime;

            if (dsEventValue.EventDataType == DSEventDataType.None)
                EventDataType = DSRouterEventDataType.None;
            else if (dsEventValue.EventDataType == DSEventDataType.Alarm)
                EventDataType = DSRouterEventDataType.Alarm;
            else if (dsEventValue.EventDataType == DSEventDataType.Ustavki)
                EventDataType = DSRouterEventDataType.Ustavki;
            else
                EventDataType = DSRouterEventDataType.Oscillogram;
        }
    }
    #endregion

    #region Уставки
    /// <summary>
    /// Класс, представляющий собой описание дампа уставок
    /// </summary>
    [DataContract]
    public class DSRouterSettingsSet
    {
        /// <summary>
        /// Идентификатор набора уставок
        /// </summary>
        [DataMember]
        public Int32 SettingsSetID { get; set; }

        /// <summary>
        /// Комментарий к набору уставок
        /// </summary>
        [DataMember]
        public String SettingsSetComment { get; set; }

        /// <summary>
        /// Дата записи набора уставок
        /// </summary>
        [DataMember]
        public DateTime SettingsSetDateTime { get; set; }
    }
    #endregion

    #region Пользователь
    /// <summary>
    /// Класс пользователя
    /// </summary>
    [DataContract]
    public class DSRouterUser
    {
        /// <summary>
        /// Идентификатор пользователя
        /// </summary>
        [DataMember]
        public Int32 UserID { get; set; }

        /// <summary>
        /// Имя пользователя
        /// </summary>
        [DataMember]
        public string UserName { get; set; }

        /// <summary>
        /// Комментарий к пользователю
        /// </summary>
        public string UserComment { get; set; }

        /// <summary>
        /// Группа, в которой состоит пользователь
        /// </summary>
        [DataMember]
        public DSRouterUserGroup DsRouterUserGroup { get; set; }

        /// <summary>
        /// Дата создания пользователя
        /// </summary>
        [DataMember]
        public DateTime CreateDateTime { get; set; }

        /// <summary>
        /// Дата изменения пользователя
        /// </summary>
        [DataMember]
        public DateTime EditDateTime { get; set; }

        public DSRouterUser(DSUser dsUser)
        {
            UserID = dsUser.UserID;
            UserName = dsUser.UserName;
            UserComment = dsUser.UserComment;
            DsRouterUserGroup = new DSRouterUserGroup(dsUser.DsUserGroup);
            CreateDateTime = dsUser.CreateDateTime;
            EditDateTime = dsUser.EditDateTime;
        }
    }

    /// <summary>
    /// Класс группы пользователей
    /// </summary>
    [DataContract]
    public class DSRouterUserGroup
    {
        /// <summary>
        /// Идентификатор группы
        /// </summary>
        [DataMember]
        public Int32 GroupID { get; set; }

        /// <summary>
        /// Имя группы
        /// </summary>
        [DataMember]
        public string GroupName { get; set; }

        /// <summary>
        /// Комментарий группы
        /// </summary>
        [DataMember]
        public string GroupComment { get; set; }

        /// <summary>
        /// Права группы
        /// </summary>
        [DataMember]
        public string GroupRight { get; set; }

        /// <summary>
        /// Дата создания группы
        /// </summary>
        [DataMember]
        public DateTime CreateDateTime { get; set; }

        /// <summary>
        /// Дата изменения группы
        /// </summary>
        [DataMember]
        public DateTime EditDateTime { get; set; }

        public DSRouterUserGroup(DSUserGroup dsUserGroup)
        {
            GroupID = dsUserGroup.GroupID;
            GroupName = dsUserGroup.GroupName;
            GroupComment = dsUserGroup.GroupComment;
            GroupRight = dsUserGroup.GroupRight;
            CreateDateTime = dsUserGroup.CreateDateTime;
            EditDateTime = dsUserGroup.EditDateTime;
        }
    }
    #endregion

    #region Документы

    [DataContract]
    public class DSRouterDocumentDataValue
    {
        [DataMember] //(Name = "Идентификатор")]
        public int DocumentID
        {
            get { return documentID; }
            set { documentID = value; }
        }

        private int documentID = -1;

        [DataMember] //(Name = "Время добавления")]
        public DateTime DocumentAddDate
        {
            get { return documentAddDate; }
            set { documentAddDate = value; }
        }

        private DateTime documentAddDate = DateTime.MinValue;

        [DataMember] //(Name = "Добавивший пользователь")]
        public string DocumentUserName
        {
            get { return documentUserName; }
            set { documentUserName = value; }
        }

        private string documentUserName = "Пользователь";

        [DataMember] //(Name = "Имя файла")]
        public string DocumentFileName
        {
            get { return documentFileName; }
            set { documentFileName = value; }
        }

        private string documentFileName = "Файл";

        [DataMember] //(Name = "Комментарий")]
        public string DocumentComment
        {
            get { return documentComment; }
            set { documentComment = value; }
        }

        private string documentComment = "Комментарий";

        public DSRouterDocumentDataValue()
        {
        }

        public DSRouterDocumentDataValue(DSDocumentDataValue dsDocument)
        {
            DocumentID = dsDocument.DocumentID;
            DocumentAddDate = dsDocument.DocumentAddDate;
            DocumentUserName = dsDocument.DocumentUserName;
            DocumentFileName = dsDocument.DocumentFileName;
            DocumentComment = dsDocument.DocumentComment;
        }
    }

    #endregion

    #region Результат Авторизации

    public enum AuthResult
    {
        None,                   // Данные не доступны
        Ok,                     // Авторизация прошла успешно
        WrongLoginOrPassword,   // Неверный логин и/или пароль
        NoConnectionToDb,       // Нет соединения с БД
        NoConnectionToDs,       // нет соединения с DS
        Unknown                 // Неизвестный результат
    }

    [DataContract]
    public class RouterAuthResult
    {
        [DataMember]
        public Dictionary<UInt16, DSRouterAuthResult> DSAuthResults { get; set; }

        public RouterAuthResult()
        {
            DSAuthResults = null;
        }
    }

    [DataContract]
    public class DSRouterAuthResult
    {
        [DataMember]
        public DSRouterUser User { get; set; }

        [DataMember]
        public AuthResult AuthResult { get; set; }

        /// <summary>
        /// Имя пользователя для внутренних нужд
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Пароль пользователя для внутренних нужд
        /// </summary>
        public string UserPassword { get; set; }

        public DSRouterAuthResult()
        {
            User = null;
            AuthResult = AuthResult.None;
        }

        public DSRouterAuthResult(DSAuthResult dsAuthResult)
        {
            User = dsAuthResult.DSUser == null ? null : new DSRouterUser(dsAuthResult.DSUser);
            AuthResult = (AuthResult) dsAuthResult.AuthResult;
        }
    }

    #endregion

    #region Тренды

    [DataContract]
    public class DSRouterTrendSettings
    {
        /// <summary>
        /// Включена ли запись тренда
        /// </summary>
        [DataMember]
        public bool Enable { get; set; }

        /// <summary>
        /// Интервал записи значений. 0 - запись по факту изменения
        /// </summary>
        [DataMember]
        public uint Sample { get; set; }

        /// <summary>
        /// Относительная погрешность изменения.
        /// Допустимый диапозон значений (0,1]
        /// </summary>
        [DataMember]
        public float? RelativeError { get; set; }

        /// <summary>
        /// Абсолютная погрешность изменения.
        /// </summary>
        [DataMember]
        public float? AbsoluteError { get; set; }

        /// <summary>
        /// Максимальное число значений, которое будет кешироваться до записи в БД
        /// </summary>
        [DataMember]
        public uint MaxCacheValuesCount { get; set; }

        /// <summary>
        /// Максимальное число минут для хранения закешированных данных
        /// </summary>
        [DataMember]
        public uint MaxCacheMinutes { get; set; }

        public DSRouterTrendSettings(DSTrendSettings dsTrendSettings)
        {
            Enable = dsTrendSettings.Enable;
            Sample = dsTrendSettings.Sample;
            AbsoluteError = dsTrendSettings.AbsoluteError;
            RelativeError = dsTrendSettings.RelativeError;
            MaxCacheMinutes = dsTrendSettings.MaxCacheMinutes;
            MaxCacheValuesCount = dsTrendSettings.MaxCacheValuesCount;
        }
    }

    #endregion

    #endregion
}
