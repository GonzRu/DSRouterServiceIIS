using System;
using System.Diagnostics;
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

        public event DSSessionCancelled OnDSSessionCancelled;

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

        #region Private-Fields

        /// <summary>
        /// таймер связи
        /// </summary>
        System.Timers.Timer tmrpingpong = new System.Timers.Timer();

        /// <summary>
        /// таймер для обнаружения ошибки при 
        /// пинг-понге
        /// </summary>
        System.Timers.Timer tmrpingpongFault = new System.Timers.Timer();

        #endregion

        #region Constructor

        public DSService(UInt16 uid, IWcfDataServer wcfds)
        {
            try
            {
                dsUID = uid;
                wcfDataServer = wcfds;
                ConnectionState = false;

                #region иницализация таймера связи
                tmrpingpong.Interval = 5000;

                tmrpingpong.Elapsed += new System.Timers.ElapsedEventHandler(tmrpingpong_Elapsed);
                tmrpingpong.Start();
                #endregion

                #region иницализация таймера ошибки для пинг-понга
                tmrpingpongFault.Interval = 5000;

                tmrpingpongFault.Elapsed += new ElapsedEventHandler(tmrpingpongFault_Elapsed);
                tmrpingpongFault.Stop();
                #endregion
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1949, ex.Message);
                Utilities.LogTrace("DSRouterService.DSService() : Исключение : " + ex.Message);
            }
        }

        #endregion

        #region Handlers

        void tmrpingpong_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Utilities.LogTrace("DSRouterService : tmrpingpong_Elapsed()");
            try
            {
                tmrpingpong.Stop();
                tmrpingpongFault.Start();

                wcfDataServer.BeginPing(OnPingCompletion, null);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1965, ex.Message);
                Utilities.LogTrace("DSRouterService.tmrpingpong_Elapsed() : Исключение : " + ex.Message);
            }
        }

        void tmrpingpongFault_Elapsed(object sender, ElapsedEventArgs e)
        {
            Utilities.LogTrace("DSRouterService : tmrpingpongFault_Elapsed()");
            try
            {
                tmrpingpong.Stop();
                tmrpingpongFault.Stop();

                /*
                 *  извещаем DSRouter о том что DS 
                 *  не отвечает и соединение с ним 
                 *  нужно перезапустить
                 */
                if (OnDSSessionCancelled != null)
                {
                    Utilities.LogTrace("DSRouterService : tmrpingpongFault_Elapsed() : OnDSSessionCancelled != null");
                    OnDSSessionCancelled(dsUID);
                }
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 1987, ex.Message);
                Utilities.LogTrace("DSRouterService.tmrpingpongFault_Elapsed() : Исключение : " + ex.Message);
            }
        }

        void OnPingCompletion(IAsyncResult result)
        {
            Utilities.LogTrace("DSRouterService : OnPingCompletion()");
            try
            {
                wcfDataServer.EndPing(result);

                tmrpingpongFault.Stop();
                tmrpingpong.Start();
            }
            catch (Exception ex)
            {
                Utilities.LogTrace("DSRouterService.OnPingCompletion() : Исключение : " + ex.Message);
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 2002, ex.Message);
            }
        }

        #endregion
    }
}