using System;
using System.Collections.Generic;
using System.Diagnostics;
using DSRouterServiceIIS.DSServiceReference;

namespace DSRouterServiceIIS
{
    public delegate void NewError(string strerror);
    public delegate void NewTagValues(Dictionary<string, DSTagValue> tv);
    public delegate void CMDExecuted(byte[] cmdarray);

    public class DSServiceCallback : IWcfDataServerCallback
    {
        public event NewError OnNewError;
        public event NewTagValues OnNewTagValues;
        public event CMDExecuted OnCMDExecuted;

        public void NewErrorEvent(string codeDataTimeEvent)
        {
            try
            {
                //передаем выше, если есть куда
                if (OnNewError != null)
                    OnNewError(codeDataTimeEvent);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
        public System.IAsyncResult BeginNewErrorEvent(string codeDataTimeEvent, System.AsyncCallback callback, object asyncState)
        {
            System.IAsyncResult rez = null;
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
            return rez;
        }

        public void EndNewErrorEvent(System.IAsyncResult result)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
        public System.IAsyncResult BeginPong(System.AsyncCallback callback, object asyncState)
        {
            System.IAsyncResult rez = null;
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
            return rez;
        }

        public void EndPong(System.IAsyncResult result)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
        public void Pong()
        {
            try
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 84, string.Format("{0} : {1} : {2} : Pong.", DateTime.Now.ToString(), @"X:\Projects\00_DataServer\WcfDataServer_Lib\WcfDataServer_Lib\WcfDataServer.cs", "Pong()()"));
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(TraceEventType.Error, 89, string.Format("{0} : {1} : {2} : ОШИБКА: {3}", DateTime.Now.ToString(), @"X:\Projects\00_DataServer\WcfDataServer_Lib\WcfDataServer_Lib\WcfDataServer.cs", "Pong()()", ex.Message));
            }
        }
        public void NotifyChangedTags(System.Collections.Generic.Dictionary<string, DSTagValue> lstChangedTags)
        {
            try
            {
                if (OnNewTagValues != null)
                    OnNewTagValues(lstChangedTags);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
        public System.IAsyncResult BeginNotifyChangedTags(System.Collections.Generic.Dictionary<string, DSTagValue> lstChangedTags, System.AsyncCallback callback, object asyncState)
        {
            System.IAsyncResult rez = null;
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
            return rez;
        }

        public void EndNotifyChangedTags(System.IAsyncResult result)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }

        public void NotifyCMDExecuted(byte[] cmdarray)
        {
            try
            {
                //передаем выше, если есть куда
                if (OnCMDExecuted != null)
                    OnCMDExecuted(cmdarray);
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }


        public System.IAsyncResult BeginNotifyCMDExecuted(byte[] cmdarray, System.AsyncCallback callback, object asyncState)
        {
            System.IAsyncResult rez = null;
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
            return rez;
        }
        public void EndNotifyCMDExecuted(System.IAsyncResult result)
        {
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                TraceSourceLib.TraceSourceDiagMes.WriteDiagnosticMSG(ex);
            }
        }
    }
}