/*#############################################################################
 *    Copyright (C) 2006-2011 Mehanotronika RA                            
 *    All rights reserved.                                                     
 *	~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
 *                                                                             
 *	Описание: класс общедоступных полей проекта DSRouterSolution
 *                                                                             
 *	Файл                     : X:\Projects\00_DataServer\DSRouterFolder\DSRouterSolution\HMI_MT_Settings\HMI_Settings.cs
 *	Тип конечного файла      :                                         
 *	версия ПО для разработки : С#, Framework 4.0                                
 *	Разработчик              : Юров В.И.                                        
 *	Дата начала разработки   : 14.03.2013
 *	Дата посл. корр-ровки    : xx.хх.201х
 *	Дата (v1.0)              :                                                  
 ******************************************************************************
* Особенности реализации:
 * Используется ...
 *#############################################################################*/

using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace HMI_MT_Settings
{
    public static class HMI_Settings
    {       
        /// <summary>
        /// Конфигурация текущего проекта
        /// </summary>
        public static SortedList<string, string> slGlobalListTagsType = new SortedList<string,string>();

        /// <summary>
        /// путь к файлу конфигурации клиента Configuration.cfg 
        /// </summary>
        public static string PathToConfigurationFile;

        /// <summary>
        /// xml-представление файла Configuration.cfg 
        /// </summary>
        public static XDocument XDoc4PathToConfigurationFile;

        /// <summary>
        /// путь к файлу проекта Project.cfg 
        /// </summary>
        public static string PathToPrjFile;

        /// <summary>
        /// xml-представление файла Project.cfg 
        /// </summary>
        public static XDocument XDoc4PathToPrjFile;

        /// <summary>
        /// список ошибок в формате:
        /// ид_ошибки + описание ошибки
        /// </summary>
        public static SortedList<string,string> SlListError = new SortedList<string,string>();

        /// <summary>
        /// код последней ошибки в формате: 
        /// codeerror@timestamp - 
        /// можно использовать для протоколирования на клиенте
        /// </summary>
        public static string LastErrorCode = "0@" + DateTime.MinValue.ToString();
        
        private static uint clientID = 1;
        /// <summary>
        /// предоставить свободный ид клиента
        /// для идентификации клиента в диспетчере запросов
        /// </summary>
        /// <returns></returns>
        public static uint GetClientID()
        {
            clientID++;
            return clientID;
        }
    }
}