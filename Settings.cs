using System;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Xml;
using MySql.Data.MySqlClient;

namespace DSRouterService
{
    /// <summary>
    /// Настройки приложения.
    /// </summary>
    public class Settings
    {
        #region свойства
        private string configfilename;
        /// <summary>
        /// Имя конфигурационного файла.
        /// </summary>
        [Description("Имя конфигурационного файла."), Category("Настройки приложения.")]
        public string ConfigFileName
        {
            get { return configfilename; }
            set { configfilename = value; }
        }

        /// <summary>
        /// Строка подключения к БД.
        /// </summary>
        [Description("Строка подключения к БД."), Category("Настройки приложения.")]
        public string ConnectionString
        {
            get
            {
                string strConn = "";
                if (!OpenStringKey("ConnectionString", ref strConn))
                    throw new Exception("Не задан параметр 'ConnectionString' в файле конфигурации");
                if (strConn == "")
                    throw new Exception("Не задана строка инициализации в файле конфигурации");

                return strConn;
            }
            set
            {
                SaveCDataKey("ConnectionString", value);

                if (connection != null)
                {
                    if (connection.State == ConnectionState.Open)
                        connection.Close();

                    connection.Dispose();
                    connection = new MySqlConnection(value);
                }
            }
        }

        /// <summary>
        /// Текущая версия конфигурации SL-клиента
        /// </summary>
        [Description("Текущая версия конфигурации SL-клиента."), Category("Настройки приложения.")]
        public string CurrentSLVersion
        {
            get
            {
                string sLVersion = "";
                if (!OpenStringKey("CurrentSLVersion", ref sLVersion))
                    throw new Exception("Не задана версия конфигурации SL-клиента в файле конфигурации");
               
                return sLVersion;
            }
            set
            {
                SaveStringKey("CurrentSLVersion", value);
            }
        }

        /// <summary>
        /// Интервал таймера опроса
        /// </summary>
        [Description("Интервал таймера опроса."), Category("Настройки приложения.")]
        public int TimerInterval
        {
            get
            {
                string strTimerInterval = "";
                if (!OpenStringKey("TimerInterval", ref strTimerInterval))
                    throw new Exception("Не задан интервал таймера опроса в файле конфигурации");
                int timerInterval = 0;
                if (int.TryParse(strTimerInterval, out timerInterval))
                    return timerInterval;
                else
                    throw new Exception("Не верный формат интервала таймера опроса в файле конфигурации");
            }
            set
            {
                SaveStringKey("TimerInterval", value.ToString());
            }
        }

        /// <summary>
        /// IP-адрес DataServer-а
        /// </summary>
        [Description("IP-адрес DataServer."), Category("Настройки приложения.")]
        public string DataServerIP
        {
            get
            {
                string dataServerIP = "";
                if (!OpenStringKey("DataServerIP", ref dataServerIP))
                    throw new Exception("Не задан IP-адрес DataServer-а в файле конфигурации");

                return dataServerIP;
            }
            set
            {
                SaveStringKey("DataServerIP", value);
            }
        }

        #endregion свойства

        /// <summary>
        /// Настройки приложения по умолчанию. Имя конфигурационного файла Application.config.
        /// </summary>
        public Settings()
        {
            configfilename = AppDomain.CurrentDomain.BaseDirectory + "Application.config";
        }

        /// <summary>
        /// Настройки приложения с указанием конфигурационного файла.
        /// </summary>
        /// <param name="strConfigFName">Имя конфигурационного файла.</param>
        public Settings(string strConfigFName)
        {
            configfilename = strConfigFName;
        }

        #region Connection
        private MySqlConnection connection;
        /// <summary>
        /// SQL-подключение к БД.
        /// </summary>
        [Description("SQL-подключение к БД."), Category("Настройки приложения.")]
        public MySqlConnection Connection
        {
            get
            {
                if (connection == null)
                    connection = new MySqlConnection(ConnectionString);

                return connection;
            }
        }
        #endregion Connection

        #region Выбор параметра конфигурации по имени
        /// <summary>
        /// Читает из файла конфигурации значение параметра по его имени.
        /// </summary>
        /// <param name="strKey">Имя параметра.</param>
        /// <param name="strValue">Сюда будет записано значение параметра, если он есть в файле.</param>
        /// <returns>True если параметр найден, False если указанного параметра нет.</returns>
        public bool OpenStringKey(string strKey, ref string strValue)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(ConfigFileName);
            XmlNodeList keys = doc.SelectNodes("/appSettings/Key");
            foreach (XmlNode key in keys)
                if (key.SelectSingleNode("Name").InnerText == strKey)
                {
                    strValue = key.SelectSingleNode("Value").InnerText;
                    return true;
                }
            return false;
        }
        #endregion Выбор параметра конфигурации по имени

        #region Сохранение параметра конфигурации в обычный блок
        /// <summary>
        /// Сохраненяет параметр конфигурации в обычный блок. Для записи значений, не содержащих служебных символов (например разметки).
        /// </summary>
        /// <param name="strKey">Имя параметра.</param>
        /// <param name="strValue">Значение параметра.</param>
        public void SaveStringKey(string strKey, string strValue)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(ConfigFileName);

            try
            {
                XmlNodeList keys = doc.SelectNodes("/appSettings/Key");
                foreach (XmlNode key in keys)
                    if (key.SelectSingleNode("Name").InnerText == strKey)
                    {
                        key.SelectSingleNode("Value").InnerText = strValue;
                        return;
                    }
                // если элемент не найден - добавляем его
                XmlNode root = doc.SelectSingleNode("/appSettings");
                XmlElement elem = doc.CreateElement("Key");
                root = root.AppendChild(elem);

                elem = doc.CreateElement("Name");
                elem.InnerText = strKey;
                root.AppendChild(elem);
                elem = doc.CreateElement("Value");
                elem.InnerText = strValue;
                root.AppendChild(elem);
            }
            finally
            {
                doc.Save(ConfigFileName);
            }
        }
        #endregion Сохранение параметра конфигурации в обычный блок

        #region Сохранение параметра конфигурации в блок CData
        /// <summary>
        /// Сохраненяет параметр конфигурации в блок CDATA. Для записи значений, содержащих служебные символы (например строка подключения).
        /// </summary>
        /// <param name="strKey">Имя параметра.</param>
        /// <param name="strValue">Значение параметра.</param>
        public void SaveCDataKey(string strKey, string strValue)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(ConfigFileName);

            try
            {
                XmlNodeList keys = doc.SelectNodes("/appSettings/Key");
                foreach (XmlNode key in keys)
                    if (key.SelectSingleNode("Name").InnerText == strKey)
                    {
                        key.SelectSingleNode("Value").RemoveAll();
                        XmlCDataSection CData = doc.CreateCDataSection(strValue);
                        key.SelectSingleNode("Value").AppendChild(CData);
                        return;
                    }
                // если элемент не найден - добавляем его
                XmlNode root = doc.SelectSingleNode("/appSettings");
                XmlElement elem = doc.CreateElement("Key");
                root = root.AppendChild(elem);

                elem = doc.CreateElement("Name");
                elem.InnerText = strKey;
                root.AppendChild(elem);
                elem = doc.CreateElement("Value");
                XmlCDataSection CDataSection = doc.CreateCDataSection(strValue);
                root.AppendChild(elem);
                elem.AppendChild(CDataSection);
            }
            finally
            {
                doc.Save(ConfigFileName);
            }
        }
        #endregion Сохранение параметра конфигурации в блок CData

        #region Положение формы на экране
        /// <summary>
        /// Читает из файла конфигурации положение и размеры экранной формы.
        /// </summary>
        /// <param name="strPrefixName">Название формы.</param>
        /// <param name="rect">Сюда записываются параметры экранной формы.</param>
        /// <returns>True если параметры экранной формы считаны успешно. False если данных нет.</returns>
        public bool OpenRectKey(string strPrefixName, ref Rectangle rect)
        {
            int CntKey = 0;

            XmlDocument doc = new XmlDocument();
            doc.Load(ConfigFileName);
            XmlNodeList keys = doc.SelectNodes("/appSettings/Key");
            foreach (XmlNode key in keys)
            {
                if (key.SelectSingleNode("Name").InnerText == strPrefixName + "X")
                {
                    rect.X = System.Convert.ToInt32(key.SelectSingleNode("Value").InnerText);
                    CntKey++;
                }
                if (key.SelectSingleNode("Name").InnerText == strPrefixName + "Y")
                {
                    rect.Y = System.Convert.ToInt32(key.SelectSingleNode("Value").InnerText);
                    CntKey++;
                }
                if (key.SelectSingleNode("Name").InnerText == strPrefixName + "Width")
                {
                    rect.Width = System.Convert.ToInt32(key.SelectSingleNode("Value").InnerText);
                    CntKey++;
                }
                if (key.SelectSingleNode("Name").InnerText == strPrefixName + "Height")
                {
                    rect.Height = System.Convert.ToInt32(key.SelectSingleNode("Value").InnerText);
                    CntKey++;
                }
                if (CntKey == 4)
                    return true;
            }
            return false;
        }
        /// <summary>
        /// Сохраняет в файле конфигурации положение и размеры экранной формы.
        /// </summary>
        /// <param name="strPrefixName">Название экранной формы.</param>
        /// <param name="rect">Параметры экранной формы.</param>
        public void SaveRectKey(string strPrefixName, Rectangle rect)
        {
            SaveStringKey(strPrefixName + "X", rect.X.ToString());
            SaveStringKey(strPrefixName + "Y", rect.Y.ToString());
            SaveStringKey(strPrefixName + "Width", rect.Width.ToString());
            SaveStringKey(strPrefixName + "Height", rect.Height.ToString());

        }
        #endregion Положение формы на экране
    }
}

