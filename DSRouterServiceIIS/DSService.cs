using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.Timers;
using DSFakeService.DSServiceReference;

namespace DSRouterService
{
    /// <summary>
    /// класс представляющий DataServer в списке 
    /// DataServer'ов с которыми работает данный
    /// DSRouter
    /// </summary>
    public class DSService
    {
        #region Events

        public event Action<Dictionary<string, DSRouterTagValue>> TagsValuesUpdated;

        #endregion

        #region Public-Fields

        /// <summary>
        /// уник номер DS
        /// </summary>
        public UInt16 dsUID { get; set; }

        /// <summary>
        /// ссылка на proxy DS
        /// </summary>
        public IWcfDataServer wcfDataServer { get; set; }

        /// <summary>
        /// состояние связи с DataServer
        /// </summary>
        public bool ConnectionState { get; set; }

        #endregion

        #region Private-Readonly

        /// <summary>
        /// Адрес DS
        /// </summary>
        private readonly string _ipAddress;

        /// <summary>
        /// Порт WCF службы DS
        /// </summary>
        private readonly string _port;

        /// <summary>
        /// таймер связи
        /// </summary>
        private readonly Timer pingPongWithDsTimer = new Timer();

        /// <summary>
        /// Таймер для переодических попыток создания соединения с DS
        /// </summary>
        private readonly Timer createDsConnectionTimer = new Timer();

        #endregion

        #region Private-Fields

        /// <summary>
        /// Список тегов, запрошенных у данного DS
        /// </summary>
        private List<string> _requestedTagsList;

        #endregion

        #region Constructor

        public DSService(UInt16 dsGuid, string ipAddress, string port)
        {
            dsUID = dsGuid;
            _ipAddress = ipAddress;
            _port = port;            

            #region иницализация таймера связи

            pingPongWithDsTimer.Interval = 5000;

            pingPongWithDsTimer.Elapsed += PingPongWithDsTimerElapsed;
            pingPongWithDsTimer.Stop();

            #endregion

            #region Таймер для установления связи с DS

            createDsConnectionTimer.Interval = 3000;

            createDsConnectionTimer.Elapsed += CreateDsConnectionTimerOnElapsed;
            createDsConnectionTimer.Stop();

            #endregion

            CreateConnectionWithDs();
        }

        #endregion

        #region Public-metods

        /// <summary>
        /// Запросить теги 
        /// </summary>
        public Dictionary<string, DSRouterTagValue> GetTagsValue(List<string> tagsRequestList)
        {
            Log.WriteDebugMessage(String.Format("DSService:GetTagsValue() : к DS-{0} отправлен запрос на {1} тегов", dsUID, tagsRequestList.Count));

            _requestedTagsList = new List<string>(tagsRequestList);

            try
            {
                lock (wcfDataServer)
                {
                    wcfDataServer.SubscribeRTUTags(tagsRequestList.ToArray());

                    return ConvertDsTagsDictionaryToDsRouterTagsDictionary(wcfDataServer.GetTagsValue(tagsRequestList.ToArray()));
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage("DSService.GetTagsValue() : Исключение : " + ex.Message);
            }

            return CreateLostConnectionResponse();
        }

        /// <summary>
        /// Отписываемся от обновлений прошлого запроса
        /// </summary>
        public void UnsubscribeFromLastRequest()
        {
            if (_requestedTagsList != null && _requestedTagsList.Count != 0)
                wcfDataServer.UnscribeRTUTags(_requestedTagsList.ToArray());
        }

        #endregion

        #region Private-metods

        #region Методы для создания канала связи с DS

        /// <summary>
        /// Производит первоначальное подключение к DS
        /// Если подключиться не удалось, то запускает таймер подключения
        /// </summary>
        private void CreateConnectionWithDs()
        {
            try
            {
                createDsConnectionTimer.Stop();

                CreateDsProxy();

                pingPongWithDsTimer.Start();

                Log.WriteDebugMessage(String.Format("DSService: с DS-{0} установлена связь", dsUID));
            }
            catch (Exception)
            {
                createDsConnectionTimer.Start();
            }
        }

        /// <summary>
        /// Запускает таймер для установления связи с DS
        /// </summary>
        private void StartTimerToRecreateDsConnection()
        {
            if (!createDsConnectionTimer.Enabled)
            {
                Log.WriteDebugMessage(String.Format("DSService: с DS-{0} не удалось установить связь", dsUID));

                createDsConnectionTimer.Start();
            }
        }

        /// <summary>
        /// Создает соединение с DS
        /// </summary>
        private void CreateDsProxy()
        {
            NetTcpBinding tcpBinding = new NetTcpBinding();
            tcpBinding.ReceiveTimeout = new TimeSpan(0, 1, 0);
            tcpBinding.SendTimeout = new TimeSpan(0, 1, 0);
            tcpBinding.MaxReceivedMessageSize = Int32.MaxValue;

            EndpointAddress endpointAddress = new EndpointAddress(String.Format(@"net.tcp://{0}:{1}/WcfDataServer_Lib.WcfDataServer", _ipAddress, _port));

            DSServiceCallback dsServiceCallback = new DSServiceCallback();
            dsServiceCallback.OnNewTagValues += TagsValuesUpdatedHandler;

            wcfDataServer = new WcfDataServerClient(new InstanceContext(dsServiceCallback), tcpBinding, endpointAddress);
            (wcfDataServer as WcfDataServerClient).Open();
            (wcfDataServer as WcfDataServerClient).InnerChannel.Closed += DsDisconnected;
            (wcfDataServer as WcfDataServerClient).InnerChannel.Faulted += DsDisconnected;

            wcfDataServer.RegisterForErrorEvent(dsUID.ToString());
        }

        #endregion

        #region Вспомогательные методы для работы класса

        /// <summary>
        /// Посылает обновления тегов, с качеством потери связи с DS
        /// </summary>
        private void SendLostConnectionTagsValue()
        {
            OnTagsValuesUpdated(CreateLostConnectionResponse());
        }

        #endregion

        #region Вспомогательные методы для работы с данными

        /// <summary>
        /// Преобразоввывает словарь значений тегов из формата DS в формат ротуера
        /// </summary>
        private Dictionary<string, DSRouterTagValue> ConvertDsTagsDictionaryToDsRouterTagsDictionary(Dictionary<string, DSTagValue> dsTagsDictionary)
        {
            var result = new Dictionary<string, DSRouterTagValue>();

            foreach (var tagIdAsStr in dsTagsDictionary.Keys)
                result[tagIdAsStr] = new DSRouterTagValue(dsTagsDictionary[tagIdAsStr]);

            return result;
        }

        /// <summary>
        /// Создает словарь значений запрошенных тегов с качеством потери связи с DS
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, DSRouterTagValue> CreateLostConnectionResponse()
        {
            if (_requestedTagsList == null)
                return new Dictionary<string, DSRouterTagValue>();

            return _requestedTagsList.ToDictionary(
                s => s,
                s => new DSRouterTagValue {VarQuality = 10, VarValueAsObject = null}
                );
        }

        #endregion

        #endregion

        #region Handlers

        #region Обработчики событий таймеров для поддержания связи с DS

        void PingPongWithDsTimerElapsed(object sender, ElapsedEventArgs e)
        {
            pingPongWithDsTimer.Stop();

            try
            {
                wcfDataServer.Ping();
                
            }
            catch (Exception ex)
            {
                Log.LogTrace("DSRouterService.PingPongWithDsTimerElapsed() : Исключение : " + ex.Message);
            }

            pingPongWithDsTimer.Start();
        }

        /// <summary>
        /// Обработчик таймера для установления соединения с DS
        /// </summary>
        private void CreateDsConnectionTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            pingPongWithDsTimer.Stop();
            createDsConnectionTimer.Stop();

            try
            {
                CreateDsProxy();

                createDsConnectionTimer.Stop();
                pingPongWithDsTimer.Start();

                // При восстановлении связи - запрашиваем все теги, на которые мы подписаны и возвращаем их значения
                // с помощью механизма обновлений
                if (_requestedTagsList != null && _requestedTagsList.Count != 0)
                    OnTagsValuesUpdated(GetTagsValue(_requestedTagsList));

                Log.WriteDebugMessage(String.Format("DSService: с DS-{0} установлена связь", dsUID));
            }
            catch (Exception ex)
            {
                createDsConnectionTimer.Start();
            }
        }

        /// <summary>
        /// Обработчик события потери соединения с DS
        /// </summary>
        private void DsDisconnected(object sender, EventArgs eventArgs)
        {
            pingPongWithDsTimer.Stop();

            StartTimerToRecreateDsConnection();

            SendLostConnectionTagsValue();
        }

        /// <summary>
        /// Вызвать событие TagsValuesUpdated
        /// </summary>
        private void OnTagsValuesUpdated(Dictionary<string, DSRouterTagValue> tagsValues)
        {
            if (TagsValuesUpdated != null)
                TagsValuesUpdated(tagsValues);
        }

        #endregion

        #region Обработчики событий wcf сервиса DS

        /// <summary>
        /// Обработчик события появления обновлений тегов
        /// </summary>
        private void TagsValuesUpdatedHandler(Dictionary<string, DSTagValue> tv)
        {
            OnTagsValuesUpdated(ConvertDsTagsDictionaryToDsRouterTagsDictionary(tv));
        }

        #endregion

        #endregion
    }
}