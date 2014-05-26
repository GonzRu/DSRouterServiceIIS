﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceModel;
using System.Timers;
using DSFakeService.DSServiceReference;

namespace DSRouterService
{
    public delegate void DSSessionCancelled(UInt16 numds);

    /// <summary>
    /// класс представляющий DataServer в списке 
    /// DataServer'ов с которыми работает данный
    /// DSRouter
    /// </summary>
    public class DSService
    {
        #region Events

        public event Action<Dictionary<string, DSRouterTagValue>> TagsValueUpdated;

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

            TryToCreateConnection();
        }

        #endregion

        #region Public-metods

        /// <summary>
        /// Запросить теги 
        /// </summary>
        public Dictionary<string, DSRouterTagValue> GetTagsValue(List<string> tagsRequestList)
        {
            try
            {
                lock (wcfDataServer)
                {
                    return ConvertDsTagsDictionaryToDsRouterTagsDictionary(wcfDataServer.GetTagsValue(tagsRequestList.ToArray()));
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1743, ex.Message);
                Utilities.LogTrace("DSRouterService.CreateWCFCL() : Исключение : " + ex.Message);
            }

            return null;
        }

        #endregion

        #region Private-metods

        #region Методы для создания канала связи с DS

        /// <summary>
        /// Запускает таймер для установления связи с DS
        /// </summary>
        private void TryToCreateConnection()
        {
            createDsConnectionTimer.Start();
        }

        /// <summary>
        /// Создает соединение с DS
        /// </summary>
        private void CreateConnectionWithDs()
        {
            NetTcpBinding tcpBinding = new NetTcpBinding();
            tcpBinding.ReceiveTimeout = new TimeSpan(0, 1, 0);
            tcpBinding.SendTimeout = new TimeSpan(0, 1, 0);
            tcpBinding.MaxReceivedMessageSize = Int32.MaxValue;

            EndpointAddress endpointAddress = new EndpointAddress(String.Format(@"net.tcp://{0}:{1}/WcfDataServer_Lib.WcfDataServer", _ipAddress, _port));

            DSServiceCallback dsServiceCallback = new DSServiceCallback();            
            dsServiceCallback.OnNewTagValues += TagValuesUpdated;

            wcfDataServer = new WcfDataServerClient(new InstanceContext(dsServiceCallback), tcpBinding, endpointAddress);
            (wcfDataServer as WcfDataServerClient).ChannelFactory.Closed += DsDisconnected;
            (wcfDataServer as WcfDataServerClient).ChannelFactory.Faulted += DsDisconnected;
        }

        #endregion

        #region Вспомогательные методы для работы с данными

        /// <summary>
        /// Преобразоввывает словарь значений тегов из формата DS в формат ротуера
        /// </summary>
        /// <param name="dsTagsDictionary"></param>
        /// <returns></returns>
        private Dictionary<string, DSRouterTagValue> ConvertDsTagsDictionaryToDsRouterTagsDictionary(Dictionary<string, DSTagValue> dsTagsDictionary)
        {
            var result = new Dictionary<string, DSRouterTagValue>();

            foreach (var tagIdAsStr in dsTagsDictionary.Keys)
                result[tagIdAsStr] = new DSRouterTagValue(dsTagsDictionary[tagIdAsStr]);

            return result;
        }

        #endregion

        #endregion

        #region Handlers

        #region Обработчики событий таймеров для поддержания связи с DS

        void PingPongWithDsTimerElapsed(object sender, ElapsedEventArgs e)
        {
            Utilities.LogTrace("DSRouterService : tmrpingpong_Elapsed()");
            pingPongWithDsTimer.Stop();

            try
            {
                wcfDataServer.Ping();
                
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1965, ex.Message);
                Utilities.LogTrace("DSRouterService.tmrpingpong_Elapsed() : Исключение : " + ex.Message);
            }

            pingPongWithDsTimer.Start();
        }

        /// <summary>
        /// Обработчик таймера для установления соединения с DS
        /// </summary>
        private void CreateDsConnectionTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            pingPongWithDsTimer.Stop();

            try
            {
                CreateConnectionWithDs();

                createDsConnectionTimer.Stop();
                pingPongWithDsTimer.Start();
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Обработчик события потери соединения с DS
        /// </summary>
        private void DsDisconnected(object sender, EventArgs eventArgs)
        {
            pingPongWithDsTimer.Stop();

            TryToCreateConnection();
        }

        #endregion

        /// <summary>
        /// Обработчик события появления обновлений тегов
        /// </summary>
        /// <param name="tv"></param>
        private void TagValuesUpdated(System.Collections.Generic.Dictionary<string, DSTagValue> tv)
        {
            if (TagsValueUpdated != null)
                TagsValueUpdated(ConvertDsTagsDictionaryToDsRouterTagsDictionary(tv));
        }

        #endregion
    }
}