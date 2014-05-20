using System;
using System.Collections;
using System.Data;
using System.Diagnostics;
using MySql.Data.MySqlClient;

namespace DSRouterService
{

    /// <summary>
    /// Набор утилит для работы с БД.
    /// </summary>
    public class Utilities
    {
        public static TraceSource source1 = new TraceSource("MyProgram.Source1");

        public static void LogTrace(string Message)
        {
            #region Лог 
            try
            {
                // Пишем в trace (system.diagnistic)
                source1.Switch = new SourceSwitch("MyProgram.Switch1");
                source1.TraceInformation(Message);

                // И в консоль
                Console.Write(String.Format(DateTime.Now.ToString() + " - {0} \r\n ", Message));
            }
            catch (Exception ex)
            {
                Console.Write("Exception !!!!!" + ex.Message);                
            }
            finally
            {
                //writer.Close();
            }
            #endregion Лог 
        }
    }
}

