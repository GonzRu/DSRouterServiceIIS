﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace DSRouterService
{
    [ServiceContract]
    public interface IDSRouterCallback
    {
        /// <summary>
        /// оповещение клиента о возникновении ошибки
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void NewErrorEvent(string codeDataTimeEvent);

        [OperationContract(IsOneWay = true)]
        void Pong();

        /// <summary>
        /// оповещение клиента об изменении тегов (по подписке)
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void NotifyChangedTags(Dictionary<string, DSRouterTagValue> lstChangedTags);

        /// <summary>
        /// извешение о выполнении команды
        /// </summary>
        [OperationContract(IsOneWay = true)]
        void NotifyCMDExecuted(byte[] cmdarray);
    }
}