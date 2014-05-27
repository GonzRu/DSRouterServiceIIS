﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using DSFakeService.DSServiceReference;
using DSRouterService;
using HMI_MT_Settings;

namespace DSFakeService.DataSources
{
    public class DataServersCollector : IDataSource
    {
        #region CONSTS
        #endregion

        #region Public Fields

        /// <summary>
        /// Возвращает список DSService's для совместимости
        /// </summary>
        public Dictionary<UInt16, DSService> dWCFClientsList
        {
            get { return _dsServiceDictionary; }
        }

        #endregion

        #region Private Fields

        /// <summary>
        /// Список DSService
        /// </summary>
        private Dictionary<UInt16, DSService> _dsServiceDictionary = null;

        /// <summary>
        /// Список запрашиваемых тегов по каждому клиенту, разбитых по DS
        /// </summary>
        private Dictionary<string, Dictionary<UInt16, List<string>>> _tagsToSubscribe = null;

        /// <summary>
        /// Словарь актуальных значений тегов, на которые подписаны
        /// </summary>
        private Dictionary<string, DSRouterTagValue> _subscribedTagsValue = null;

        /// <summary>
        /// Словарь по пользователям, показывающий обновлено ли значение тега или нет, на который он подписан
        /// </summary>
        private Dictionary<string, Dictionary<string, bool>> _subscribedUsersTagsState = null;

        #endregion

        #region Constructor

        public DataServersCollector()
        {
            InitDataServers();

            _tagsToSubscribe = new Dictionary<string, Dictionary<ushort, List<string>>>();
            _subscribedTagsValue = new Dictionary<string, DSRouterTagValue>();
            _subscribedUsersTagsState = new Dictionary<string, Dictionary<string, bool>>();
        }

        #endregion

        #region IDataSource

        /// <summary>
        /// Получить значения тегов и при этом подписаться на обновления.
        /// Идентификация пользователя происходит по Id сессии.
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDataSource.GetTagsValue(string sessionId, List<string> tagsList)
        {
            if (tagsList == null)
                throw new ArgumentException();

            var tagsByDs = ConvertTagsListToTagsDictionatyByDS(tagsList);

            /* 
             * Вместо того, чтобы делать полный запрос по всем DS
             * делаем запрос только по тем ds, в которые нужно
             * (так как по остальным ds список запрашиваемых тегов не изменился)
             */

            // Создаем словарь состояний тегов для этого пользователя
            if (!_subscribedUsersTagsState.ContainsKey(sessionId))
                _subscribedUsersTagsState.Add(sessionId, null);
            _subscribedUsersTagsState[sessionId] = CreateTagsStateDictionary(tagsList);

            // Делаем полный запрос ко всем DS (плохой вариант)
            if (!_tagsToSubscribe.ContainsKey(sessionId))
                _tagsToSubscribe.Add(sessionId, new Dictionary<ushort, List<string>>());

            _tagsToSubscribe[sessionId] = tagsByDs;
            DoFullRequest();

            // Подготавливаем ответ на основе словаря актуальных значений тегов
            var result = new Dictionary<string, DSRouterTagValue>();
            foreach (var tagIdAsStr in tagsList)
                result.Add(tagIdAsStr, _subscribedTagsValue[tagIdAsStr]);

            return result;
        }

        /// <summary>
        /// Получить обновления, которые произошли с момента последнего запроса
        /// </summary>
        Dictionary<string, DSRouterTagValue> IDataSource.GetTagsValuesUpdated(string sessionId)
        {
            if (!_subscribedUsersTagsState.ContainsKey(sessionId))
                return null;

            var result = new Dictionary<string, DSRouterTagValue>();

            var usersSubscribedTagsValueState = _subscribedUsersTagsState[sessionId];
            foreach (var tagAsStr in usersSubscribedTagsValueState.Keys)
            {
                if (usersSubscribedTagsValueState[tagAsStr])
                {
                    result.Add(tagAsStr, _subscribedTagsValue[tagAsStr]);
                    usersSubscribedTagsValueState[tagAsStr] = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Отменить подписку тегов
        /// </summary>
        void IDataSource.UnsubscribeTags(string sessionId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Получить прокси DS по его номеру
        /// </summary>
        IWcfDataServer IDataSource.GetDsProxy(UInt16 dsGuid)
        {
            return _dsServiceDictionary.ContainsKey(dsGuid) ? _dsServiceDictionary[dsGuid].wcfDataServer : null;
        }

        #endregion

        #region Private metods

        #region Вспомогательные методы для иницилизациии класса

        /// <summary>
        /// Иницилизируем список DSService'ов беря информацию из файла Configuration.cfg
        /// </summary>
        void InitDataServers()
        {
            _dsServiceDictionary = new Dictionary<ushort, DSService>();

            XDocument configurationFileXDocument = XDocument.Load(HMI_Settings.PathToConfigurationFile);
            var dsConfigXElementList = configurationFileXDocument.Element("Project").Element("Configuration").Elements("Object");

            foreach (var dsConfigXElement in dsConfigXElementList)
            {
                try
                {
                    UInt16 dsGuid = UInt16.Parse(dsConfigXElement.Attribute("UniDS_GUID").Value);

                    string ipAddress = dsConfigXElement.Element("DSAccessInfo").Element("binding").Element("IPAddress").Value;
                    string port = dsConfigXElement.Element("DSAccessInfo").Element("binding").Element("Port").Value;

                    var dsService = new DSService(dsGuid, "127.0.0.1", "8732");
                    dsService.TagValuesUpdated += TagValuesUpdated;
                    _dsServiceDictionary.Add(dsGuid, dsService);
                }
                catch (Exception)
                {
                }
            }
        }

        #endregion

        #region Вспомогательные методы для работы класса

        /// <summary>
        /// Преобразовывает список тегов в словарь тегов по номерам DS
        /// </summary>
        private Dictionary<UInt16, List<string>> ConvertTagsListToTagsDictionatyByDS(List<string> tagsList)
        {
            var result = new Dictionary<UInt16, List<string>>();

            foreach (var tagIdAsStr in tagsList)
            {
                var c = tagIdAsStr.Split('.');

                UInt16 dsGuid = UInt16.Parse(c[0]);
                if (!result.ContainsKey(dsGuid))
                    result.Add(dsGuid, new List<string>());

                result[dsGuid].Add(tagIdAsStr);
            }

            return result;
        }

        /// <summary>
        /// Сформировать и сделать полный запрос к серверам
        /// </summary>
        private void DoFullRequest()
        {
            var tagsForRequest = CreateTagsRequestForAllDs();
            _subscribedTagsValue.Clear();

            foreach (var dsGuid in tagsForRequest.Keys)
            {
                var resultFromDs = _dsServiceDictionary[dsGuid].GetTagsValue(tagsForRequest[dsGuid]);

                // Конвертируем пришедший результат в общий список значений
                foreach (var tagAsStr in resultFromDs.Keys)
                {
                    _subscribedTagsValue[tagAsStr] = resultFromDs[tagAsStr];
                }
            }
        }

        /// <summary>
        /// Формирует списки запросов к каждому DS
        /// </summary>
        private Dictionary<UInt16, List<string>> CreateTagsRequestForAllDs()
        {
            var result = new Dictionary<UInt16, List<string>>();

            foreach (var userTagsRequestDictionary in _tagsToSubscribe.Values)
            {
                foreach (var dsGuid in userTagsRequestDictionary.Keys)
                {
                    if (!result.ContainsKey(dsGuid))
                        result.Add(dsGuid, new List<string>());

                    result[dsGuid].AddRange(userTagsRequestDictionary[dsGuid]);
                }
            }

            return result;
        }

        /// <summary>
        /// Создает словарь состояний запрошенных тегов
        /// </summary>
        private Dictionary<string, bool> CreateTagsStateDictionary(List<string> tagsList)
        {
            return tagsList.ToDictionary(s => s, s => false);
        }

        #endregion

        #endregion

        #region Handlers

        /// <summary>
        /// Обработчик события пришедших обновлений от DS
        /// </summary>
        private void TagValuesUpdated(Dictionary<string, DSRouterTagValue> tv)
        {
            foreach (var dsTagAsStr in tv.Keys)
            {
                if (_subscribedTagsValue.ContainsKey(dsTagAsStr))
                {
                    // Обновляем значение тега в списке актуальных значений тегов
                    _subscribedTagsValue[dsTagAsStr] = tv[dsTagAsStr];

                    // Проходимся по списку подписанных тегов каждого пользователя и
                    // если этот тег там есть, отмечаем его как обновленный
                    foreach (var userSubscribedTags in _subscribedUsersTagsState.Values)
                    {
                        if (userSubscribedTags.ContainsKey(dsTagAsStr))
                            userSubscribedTags[dsTagAsStr] = true;
                    }
                }
            }
        }

        #endregion
    }
}