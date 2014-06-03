using System;
using System.Collections.Generic;
using DSRouterServiceIIS.DSServiceReference;

namespace DSRouterServiceIIS.DataSources
{
    public interface IDataSource
    {
        /// <summary>
        /// Получить значения тегов и при этом подписаться на обновления.
        /// Идентификация пользователя происходит по Id сессии.
        /// </summary>
        Dictionary<string, DSRouterTagValue> GetTagsValue(string sessionId, List<string> tagsList);

        /// <summary>
        /// Получить обновления, которые произошли с момента последнего запроса
        /// </summary>
        Dictionary<string, DSRouterTagValue> GetTagsValuesUpdated(string sessionId);

        /// <summary>
        /// Отменить подписку тегов
        /// </summary>
        void UnsubscribeTags(string sessionId);

        /// <summary>
        /// Получить прокси DS по его номеру
        /// </summary>
        IWcfDataServer GetDsProxy(UInt16 dsGuid);
    }
}
