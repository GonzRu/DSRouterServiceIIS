using System.Diagnostics;

namespace DSRouterService
{

    public static class Log
    {
        #region CONSTS

        private const string TRACE_SOURCE_NAME = "DSRouterService.DSRouterSource";

        private const string SOURCE_SWITCH_NAME = "DSRouterService.Switch";

        #endregion

        public static TraceSource Source = new TraceSource(TRACE_SOURCE_NAME);

        #region Constructor

        static Log()
        {
            Source.Switch = new SourceSwitch(SOURCE_SWITCH_NAME);
        }

        #endregion        

        public static void LogTrace(string Message)
        {
            Source.Switch = new SourceSwitch(SOURCE_SWITCH_NAME);
            Source.TraceInformation(Message);
        }

        /// <summary>
        /// Вывести отладочное сообщение
        /// </summary>
        public static void WriteDebugMessage(string message)
        {
            #if (DEBUG)
            {              
                Source.TraceEvent(TraceEventType.Verbose, 0, message);
            }
            #endif
        }

        /// <summary>
        /// Вывести предупреждающее сообщение
        /// </summary>
        public static void WriteErrorMessage(string message)
        {
            Source.TraceEvent(TraceEventType.Error, 0, message);
        }

        /// <summary>
        /// Вывести сообщение об ошибке
        /// </summary>
        public static void WriteWarningMessage(string message)
        {
            Source.TraceEvent(TraceEventType.Warning, 0, message);
        }

        /// <summary>
        /// Вывести сообщение о критической ошибке
        /// </summary>
        public static void WriteCriticalMessage(string message)
        {
            Source.TraceEvent(TraceEventType.Critical, 0, message);
        }
    }
}

