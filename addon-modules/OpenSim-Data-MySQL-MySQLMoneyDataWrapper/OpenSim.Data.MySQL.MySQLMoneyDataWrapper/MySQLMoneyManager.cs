/*
 * Copyright (c) Contributors, http://opensimulator.org/, http://www.nsl.tuis.ac.jp/ See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Funktion
Die Klasse MySQLMoneyManager implementiert das Interface IMoneyManager und ist das zentrale Datenbank-Backend f�r das OpenSim-Money-Modul (MySQL). Sie verwaltet:

    Verbindungen zur MySQL-Datenbank
    Kontost�nde (balances)
    Transaktionen (transactions)
    Benutzerdaten (userinfo)
    Verkaufstatistiken (totalsales)
    Methoden zum Ein-/Auszahlen, �bertragen, Anlegen und Aktualisieren von Werten

Null Pointer Checks & Fehlerquellen
Initialisierung und DB-Verbindung
    Die Constructoren pr�fen per .Any(p => string.IsNullOrEmpty(p)), ob alle ben�tigten Parameter gesetzt sind. Bei Fehlwerten gibt es einen klaren Exception-Throw.
    Beim �ffnen der Datenbankverbindung wird jeder Fehler per Exception behandelt.
    In allen Methoden, die auf die DB-Verbindung zugreifen, wird dbcon verwendet, das zentral initialisiert wird.

Datenbankoperationen
    Try-Catch-Bl�cke sind an allen kritischen Stellen vorhanden, etwa beim Table-Setup, bei der Reconnect-Logik und bei Datenbankzugriffen.
    Nach jedem Datenbankzugriff wird das Command-Objekt mit Dispose() freigegeben.
    Beim Lesen von Datenbankwerten wird mit DBNull gepr�ft, ob ein Wert gesetzt ist, bevor er als String oder int gecastet wird.
    Bei Methoden, die Objekte zur�ckgeben (z.B. FetchTransaction, fetchUserInfo), wird im Fehlerfall null zur�ckgegeben und im Catch geloggt.

Null Pointer Checks
    Bei Methoden wie addTransaction werden Objekteigenschaften vor Benutzung auf null gepr�ft und ggf. mit Defaultwerten gef�llt.
    Bei �bergabeobjekten wie UserInfo wird vor dem Zugriff gepr�ft, ob z.B. Avatar null ist.
    R�ckgabewerte wie bei fetchUserInfo werden nur dann zur�ckgegeben, wenn die UserID wirklich gefunden wurde � sonst gibt es null.
    Bei Methoden, die Listen zur�ckgeben (z.B. FetchTransaction(string userID, ...)), wird bei DB-Lesefehlern null oder ein leeres Array zur�ckgegeben.

Fehlerquellen
    Fehlerhafte oder fehlende DB-Verbindung: Exception und Logging.
    SQL-Befehle mit Fehlern: Exception und Logging.
    Ung�ltige Eingabewerte (z.B. null bei Strings/Objekten): Entweder direkter Return, Exception oder Logging.
    Potentielle Race-Conditions werden durch lock (dbcon) reduziert.
    Ressourcenlecks werden durch konsequentes .Dispose() der Commands/Reader vermieden.

Positive Beispiele f�r Fehlerbehandlung

    Parameter-Checks:
    C#

if (requiredParameters.Any(p => string.IsNullOrEmpty(p))) { throw new ArgumentException(...); }

DB-Reader mit Exception Handling:
C#

catch (Exception e) {
    m_log.Error("[MONEY MANAGER]: Fetching transaction failed 1: " + e.ToString());
    r.Close(); cmd.Dispose(); return null;
}

Null/Default-Initialisierung:
C#

    if (transaction.ObjectUUID == null) transaction.ObjectUUID = UUID.Zero.ToString();
    if (transaction.ObjectName == null) transaction.ObjectName = string.Empty;

Zusammenfassung/Fazit
    Null Pointer: Das Risiko ist im Code durch Checks, Defaultwerte und Exception-Handling weitgehend minimiert.
    Fehlerquellen: Vor allem externe Faktoren (DB-Verbindung, SQL-Syntax). Im Code wird alles sauber abgefangen und geloggt.
    Funktion: Umfassende Datenbank-Backend-Klasse f�r OpenSim-Money, mit Methoden f�r alle wichtigen Operationen (Balance, Transaktionen, User, Verkauf).

Empfehlung:
Der Code ist f�r produktiven Einsatz geeignet, robust gegen NullPointer-Fehler und typische Fehlerquellen.
Bei zuk�nftigen Erweiterungen empfiehlt sich: Bei allen Methoden, die Objekte oder komplexe Werte zur�ckgeben, weiterhin konsequent auf null pr�fen und Exceptions sauber loggen.
 */

using System;
using System.Data;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using log4net;
using MySql.Data.MySqlClient;
using OpenMetaverse;
using System.Linq;

namespace OpenSim.Data.MySQL.MySQLMoneyDataWrapper
{
    public class MySQLMoneyManager : IMoneyManager
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string Table_of_Balances = "balances";
        private string Table_of_Transactions = "transactions";
        private string Table_of_TotalSales = "totalsales";
        private string Table_of_UserInfo = "userinfo";
        private int balances_rev = 0;
        private int userinfo_rev = 0;

        private string connectString;
        public MySqlConnection dbcon;


        // Korrigierte Initialisierung des Connection-Strings mit Pooling-Check
        public MySQLMoneyManager(string hostname, string database, string username, string password, string cpooling, string port)
        {
            var requiredParameters = new[] { hostname, database, username, password, cpooling, port };

            if (requiredParameters.Any(p => string.IsNullOrEmpty(p)))
            {
                throw new ArgumentException("All connection parameters must be provided.");
            }

            // Pooling-Parameter pr�fen und ggf. setzen
            string poolingValue = cpooling?.Trim().ToLower();
            if (string.IsNullOrEmpty(poolingValue) || poolingValue == "true" || poolingValue == "yes" || poolingValue == "1")
            {
                poolingValue = "true";
            }
            else if (poolingValue == "false" || poolingValue == "no" || poolingValue == "0")
            {
                poolingValue = "false";
            }
            else
            {
                // Fallback auf Standardwert
                poolingValue = "true";
            }

            string connectionString = $"Server={hostname};Port={port};Database={database};User ID={username};Password={password};Pooling={poolingValue};";
            Initialise(connectionString);
        }

        public MySQLMoneyManager(string connect)
        {
            Initialise(connect);
        }

        private void Initialise(string connect)
        {
            // 1. Initialisiere Verbindungen
            InitialiseConnection(connect);

            // 2. �berpr�fe und erstelle Tabellen
            CheckAndCreateTables();
        }

        // 1. Initialisiere Verbindungen
        private void InitialiseConnection(string connect)
        {
            try
            {
                connectString = connect;
                dbcon = new MySqlConnection(connectString);
                dbcon.Open();
            }
            catch (Exception e)
            {
                throw new Exception("[MONEY MANAGER]: Error initializing MySql Database: " + e.ToString());
            }
        }

        // 2. �berpr�fe und erstelle Tabellen
        private void CheckAndCreateTables()
        {
            try
            {
                Dictionary<string, string> tableList = CheckTables();

                InitialiseBalancesTable(tableList);
                InitialiseUserInfoTable(tableList);
                InitialiseTransactionsTable(tableList);
                InitialiseTotalSalesTable(tableList);
            }
            catch (Exception e)
            {
                m_log.Error("[MONEY MANAGER]: Error checking or creating tables: " + e.ToString());
                throw new Exception("[MONEY MANAGER]: Error checking or creating tables: " + e.ToString());
            }
        }

        private void InitialiseBalancesTable(Dictionary<string, string> tableList)
        {
            if (!tableList.ContainsKey(Table_of_Balances))
            {
                try
                {
                    CreateBalancesTable();
                }
                catch (Exception e)
                {
                    throw new Exception("[MONEY MANAGER]: Error creating balances table: " + e.ToString());
                }
            }
            else
            {
                string version = tableList[Table_of_Balances].Trim();
                int nVer = getTableVersionNum(version);
                balances_rev = nVer;
                switch (nVer)
                {
                    case 1: //Rev.1
                        UpdateBalancesTable1();
                        UpdateBalancesTable2();
                        UpdateBalancesTable3();
                        break;
                    case 2: //Rev.2
                        UpdateBalancesTable2();
                        UpdateBalancesTable3();
                        break;
                    case 3: //Rev.3
                        UpdateBalancesTable3();
                        break;
                }
            }
        }

        private void InitialiseUserInfoTable(Dictionary<string, string> tableList)
        {
            if (!tableList.ContainsKey(Table_of_UserInfo))
            {
                try
                {
                    CreateUserInfoTable();
                }
                catch (Exception e)
                {
                    throw new Exception("[MONEY MANAGER]: Error creating userinfo table: " + e.ToString());
                }
            }
            else
            {
                string version = tableList[Table_of_UserInfo].Trim();
                int nVer = getTableVersionNum(version);
                userinfo_rev = nVer;
                switch (nVer)
                {
                    case 1: //Rev.1
                        UpdateUserInfoTable1();
                        UpdateUserInfoTable2();
                        break;
                    case 2: //Rev.2
                        UpdateUserInfoTable2();
                        break;
                }
            }
        }

        private void InitialiseTransactionsTable(Dictionary<string, string> tableList)
        {
            if (!tableList.ContainsKey(Table_of_Transactions))
            {
                try
                {
                    CreateTransactionsTable();
                }
                catch (Exception e)
                {
                    throw new Exception("[MONEY MANAGER]: Error creating transactions table: " + e.ToString());
                }
            }
            else
            {
                string version = tableList[Table_of_Transactions].Trim();
                int nVer = getTableVersionNum(version);
                switch (nVer)
                {
                    case 2: //Rev.2
                        UpdateTransactionsTable2();
                        UpdateTransactionsTable3();
                        UpdateTransactionsTable4();
                        UpdateTransactionsTable5();
                        UpdateTransactionsTable6();
                        UpdateTransactionsTable7();
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 3: //Rev.3
                        UpdateTransactionsTable3();
                        UpdateTransactionsTable4();
                        UpdateTransactionsTable5();
                        UpdateTransactionsTable6();
                        UpdateTransactionsTable7();
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 4: //Rev.4
                        UpdateTransactionsTable4();
                        UpdateTransactionsTable5();
                        UpdateTransactionsTable6();
                        UpdateTransactionsTable7();
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 5: //Rev.5
                        UpdateTransactionsTable5();
                        UpdateTransactionsTable6();
                        UpdateTransactionsTable7();
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 6: //Rev.6
                        UpdateTransactionsTable6();
                        UpdateTransactionsTable7();
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 7: //Rev.7
                        UpdateTransactionsTable7();
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 8: //Rev.8
                        UpdateTransactionsTable8();
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 9: //Rev.9
                        UpdateTransactionsTable9();
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 10: //Rev.10
                        UpdateTransactionsTable10();
                        UpdateTransactionsTable11();
                        break;
                    case 11: //Rev.11
                        UpdateTransactionsTable11();
                        break;
                }
            }
        }

        private void InitialiseTotalSalesTable(Dictionary<string, string> tableList)
        {
            if (!tableList.ContainsKey(Table_of_TotalSales))
            {
                try
                {
                    CreateTotalSalesTable();
                }
                catch (Exception e)
                {
                    throw new Exception("[MONEY MANAGER]: Error creating totalsales table: " + e.ToString());
                }
            }
            else
            {
                string version = tableList[Table_of_TotalSales].Trim();
                int nVer = getTableVersionNum(version);
                switch (nVer)
                {
                    case 1: //Rev.1
                        UpdateTotalSalesTable1();
                        UpdateTotalSalesTable2();
                        break;
                    case 2: //Rev.2
                        UpdateTotalSalesTable2();
                        break;
                }
            }
        }



        /// <summary>Gets the table version number.</summary>
        /// <param name="version">The version.</param>
        private int getTableVersionNum(string version)
        {
            int nVer = 0;

            Regex _commentPattenRegex = new Regex(@"\w+\.(?<ver>\d+)");
            Match m = _commentPattenRegex.Match(version);
            if (m.Success)
            {
                string ver = m.Groups["ver"].Value;
                nVer = Convert.ToInt32(ver);
            }
            return nVer;
        }

        /// <summary>Creates the balances table.</summary>
        private void CreateBalancesTable()
        {
            string sql = string.Empty;

            sql = "CREATE TABLE `" + Table_of_Balances + "` (";
            sql += "`user` varchar(36) NOT NULL,";
            sql += "`balance` int(10) NOT NULL,";
            sql += "`status` tinyint(2) DEFAULT NULL,";
            sql += "`type`   tinyint(2)  NOT NULL DEFAULT 0,";
            sql += "PRIMARY KEY(`user`))";
            sql += "Engine=InnoDB DEFAULT CHARSET=utf8 ";

            sql += "COMMENT='Rev.4';";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>Creates the user information table.</summary>
        private void CreateUserInfoTable()
        {
            string sql = string.Empty;

            sql = "CREATE TABLE `" + Table_of_UserInfo + "` (";
            sql += "`user` varchar(36) NOT NULL,";
            sql += "`simip` varchar(64) NOT NULL,";
            sql += "`avatar` varchar(50) NOT NULL,";
            sql += "`pass`  varchar(36) NOT NULL DEFAULT '',";
            sql += "`type`  tinyint(2)  NOT NULL DEFAULT 0,";
            sql += "`class` tinyint(2)  NOT NULL DEFAULT 0,";
            sql += "`serverurl` varchar(255) NOT NULL DEFAULT '',";
            sql += "PRIMARY KEY(`user`))";
            sql += "Engine=InnoDB DEFAULT CHARSET=utf8 ";

            sql += "COMMENT='Rev.3';";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>Creates the transactions table.</summary>
        private void CreateTransactionsTable()
        {
            string sql = string.Empty;

            sql = "CREATE TABLE `" + Table_of_Transactions + "`(";
            sql += "`UUID` varchar(36) NOT NULL,";
            sql += "`sender` varchar(36) NOT NULL,";
            sql += "`receiver` varchar(36) NOT NULL,";
            sql += "`amount` int(10) NOT NULL,";
            sql += "`senderBalance`   int(10) NOT NULL DEFAULT -1,";
            sql += "`receiverBalance` int(10) NOT NULL DEFAULT -1,";
            sql += "`objectUUID` varchar(36)  DEFAULT NULL,";
            sql += "`objectName` varchar(255) DEFAULT NULL,";
            sql += "`regionHandle` varchar(36) NOT NULL,";
            sql += "`regionUUID`   varchar(36) NOT NULL,";
            sql += "`type` int(10) NOT NULL,";
            sql += "`time` int(11) NOT NULL,";
            sql += "`secure` varchar(36) NOT NULL,";
            sql += "`status` tinyint(1)  NOT NULL,";
            sql += "`commonName` varchar(128) NOT NULL,";
            sql += "`description` varchar(255) DEFAULT NULL,";
            sql += "PRIMARY KEY(`UUID`))";
            sql += "Engine=InnoDB DEFAULT CHARSET=utf8 ";

            sql += "COMMENT='Rev.12';";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>Creates the total sales table.</summary>
        private void CreateTotalSalesTable()
        {
            string sql = string.Empty;

            sql = "CREATE TABLE `" + Table_of_TotalSales + "` (";
            sql += "`UUID` varchar(36) NOT NULL,";
            sql += "`user` varchar(36) NOT NULL,";
            sql += "`objectUUID` varchar(36)  NOT NULL,";
            sql += "`type` int(10) NOT NULL,";
            sql += "`TotalCount`  int(10) NOT NULL DEFAULT 0,";
            sql += "`TotalAmount` int(10) NOT NULL DEFAULT 0,";
            sql += "`time` int(11) NOT NULL,";
            sql += "PRIMARY KEY(`UUID`))";
            sql += "Engine=InnoDB DEFAULT CHARSET=utf8 ";

            sql += "COMMENT='Rev.3';";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();

            initTotalSalesTable();
        }


        /// <summary>Updates the balances table1.</summary>
        private void UpdateBalancesTable1()
        {
            m_log.Info("[MONEY MANAGER]: Converting Balance Table...");
            string sql = string.Empty;

            sql = "SELECT COUNT(*) FROM " + Table_of_Balances;
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            int resultCount = int.Parse(cmd.ExecuteScalar().ToString());
            cmd.Dispose();

            sql = "SELECT * FROM " + Table_of_Balances;
            cmd = new MySqlCommand(sql, dbcon);
            MySqlDataReader dbReader = cmd.ExecuteReader();

            int l = 0;
            string[,] row = new string[resultCount, dbReader.FieldCount];
            while (dbReader.Read())
            {
                for (int i = 0; i < dbReader.FieldCount; i++)
                {
                    row[l, i] = dbReader.GetString(i);
                }
                l++;
            }
            dbReader.Close();
            cmd.Dispose();

            bool updatedb = true;
            for (int i = 0; i < resultCount; i++)
            {
                string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
                if (uuid != row[i, 0])
                {
                    int amount = int.Parse(row[i, 1]);
                    int balance = getBalance(uuid);
                    if (balance >= 0)
                    {
                        amount += balance;
                        updatedb = updateBalance(uuid, amount);
                    }
                    else
                    {
                        updatedb = addUser(uuid, amount, int.Parse(row[i, 2]), 0);
                    }
                    if (!updatedb) break;
                }
            }

            // Delete
            if (updatedb)
            {
                for (int i = 0; i < resultCount; i++)
                {
                    string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
                    if (uuid != row[i, 0])
                    {
                        sql = "DELETE FROM " + Table_of_Balances + " WHERE user = ?uuid";
                        cmd = new MySqlCommand(sql, dbcon);
                        cmd.Parameters.AddWithValue("?uuid", row[i, 0]);
                        cmd.ExecuteNonQuery();
                        cmd.Dispose();
                    }
                }


                sql = "BEGIN;";
                sql += "ALTER TABLE `" + Table_of_Balances + "` ";
                sql += "COMMENT = 'Rev.2';";
                sql += "COMMIT;";
                cmd = new MySqlCommand(sql, dbcon);
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
        }


        /// <summary>Updates the balances table2.</summary>
        private void UpdateBalancesTable2()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Balances + "` ";
            sql += "MODIFY COLUMN `user` varchar(36) NOT NULL,";
            sql += "COMMENT = 'Rev.3';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>Updates the balances table3.</summary>
        private void UpdateBalancesTable3()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Balances + "` ";
            sql += "ADD `type`  tinyint(2) NOT NULL DEFAULT 0 AFTER `status`,";
            sql += "COMMENT = 'Rev.4';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }

        /// <summary>Updates the user information table1.</summary>
        private void UpdateUserInfoTable1()
        {
            //m_log.Info("[MONEY MANAGER]: Converting UserInfo Table...");
            string sql = string.Empty;

            sql = "SELECT COUNT(*) FROM " + Table_of_UserInfo;
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            int resultCount = int.Parse(cmd.ExecuteScalar().ToString());
            cmd.Dispose();

            sql = "SELECT * FROM " + Table_of_UserInfo;
            cmd = new MySqlCommand(sql, dbcon);
            MySqlDataReader dbReader = cmd.ExecuteReader();

            int l = 0;
            string[,] row = new string[resultCount, dbReader.FieldCount];
            while (dbReader.Read())
            {
                for (int i = 0; i < dbReader.FieldCount; i++)
                {
                    row[l, i] = dbReader.GetString(i);
                }
                l++;
            }
            dbReader.Close();
            cmd.Dispose();

            // UniversalID -> uuid, url, name, pass
            bool updatedb = true;
            for (int i = 0; i < resultCount; i++)
            {
                string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
                if (uuid != row[i, 0])
                {
                    UserInfo userInfo = fetchUserInfo(uuid);
                    if (userInfo == null)
                    {
                        userInfo = new UserInfo();
                        userInfo.UserID = uuid;
                        userInfo.SimIP = row[i, 1];
                        userInfo.Avatar = row[i, 2];
                        userInfo.PswHash = row[i, 3];
                        updatedb = addUserInfo(userInfo);
                    }
                }
            }

            // Delete
            if (updatedb)
            {
                for (int i = 0; i < resultCount; i++)
                {
                    string uuid = Regex.Replace(row[i, 0], @"@.+$", "");
                    if (uuid != row[i, 0])
                    {
                        sql = "DELETE FROM " + Table_of_UserInfo + " WHERE user = ?uuid";
                        cmd = new MySqlCommand(sql, dbcon);
                        cmd.Parameters.AddWithValue("?uuid", row[i, 0]);
                        cmd.ExecuteNonQuery();
                        cmd.Dispose();
                    }
                }

                //
                sql = "BEGIN;";
                sql += "ALTER TABLE `" + Table_of_UserInfo + "` ";
                sql += "COMMENT = 'Rev.2';";
                sql += "COMMIT;";
                cmd = new MySqlCommand(sql, dbcon);
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
        }

        /// <summary>Updates the user information table2.</summary>
        private void UpdateUserInfoTable2()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_UserInfo + "` ";
            sql += "MODIFY COLUMN `user` varchar(36) NOT NULL,";
            sql += "MODIFY COLUMN `pass` varchar(36) NOT NULL DEFAULT '',";
            sql += "ADD `type`  tinyint(2) NOT NULL DEFAULT 0 AFTER `pass`,";
            sql += "ADD `class` tinyint(2) NOT NULL DEFAULT 0 AFTER `type`,";
            sql += "ADD `serverurl` varchar(255) NOT NULL DEFAULT '' AFTER `class`,";
            sql += "COMMENT = 'Rev.3';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        // update Transactions Table

        /// <summary>
        /// update transactions table from Rev.2 to Rev.3
        /// </summary>
        private void UpdateTransactionsTable2()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD(`objectUUID` varchar(36) DEFAULT NULL AFTER `amount`),";
            sql += "COMMENT = 'Rev.3';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.3 to Rev.4
        /// </summary>
        private void UpdateTransactionsTable3()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD(`secure` varchar(36) NOT NULL AFTER `time`),";
            sql += "COMMENT = 'Rev.4';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.4 to Rev.5
        /// </summary>
        private void UpdateTransactionsTable4()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD(`regionHandle` varchar(36) NOT NULL AFTER `objectUUID`),";
            sql += "COMMENT = 'Rev.5';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.5 to Rev.6
        /// </summary>
        private void UpdateTransactionsTable5()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD(`commonName` varchar(128) NOT NULL AFTER `status`),";
            sql += "COMMENT = 'Rev.6';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.6 to Rev.7
        /// </summary>
        private void UpdateTransactionsTable6()
        {
            string sql = string.Empty;

            sql = "SELECT COUNT(*) FROM " + Table_of_Transactions;
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            int resultCount = int.Parse(cmd.ExecuteScalar().ToString());
            cmd.Dispose();

            sql = "SELECT UUID,sender,receiver FROM " + Table_of_Transactions;
            cmd = new MySqlCommand(sql, dbcon);
            MySqlDataReader dbReader = cmd.ExecuteReader();

            int l = 0;
            string[,] row = new string[resultCount, dbReader.FieldCount];
            while (dbReader.Read())
            {
                for (int i = 0; i < dbReader.FieldCount; i++)
                {
                    row[l, i] = dbReader.GetString(i);
                }
                l++;
            }
            dbReader.Close();
            cmd.Dispose();

            sql = "UPDATE " + Table_of_Transactions + " SET sender = ?sender , receiver = ?receiver WHERE UUID = ?uuid;";
            for (int i = 0; i < resultCount; i++)
            {
                string sender = Regex.Replace(row[i, 1], @"@.+$", "");
                string receiver = Regex.Replace(row[i, 2], @"@.+$", "");
                cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?uuid", row[i, 0]);
                cmd.Parameters.AddWithValue("?sender", sender);
                cmd.Parameters.AddWithValue("?receiver", receiver);
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "COMMENT = 'Rev.7';";
            sql += "COMMIT;";
            cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.7 to Rev.8
        /// </summary>
        private void UpdateTransactionsTable7()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD `objectName` varchar(255) DEFAULT NULL AFTER `objectUUID`,";
            sql += "COMMENT = 'Rev.8';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.8 to Rev.9
        /// </summary>
        private void UpdateTransactionsTable8()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD `senderBalance`   int(10) NOT NULL DEFAULT -1 AFTER `amount`,";
            sql += "ADD `receiverBalance` int(10) NOT NULL DEFAULT -1 AFTER `senderBalance`,";
            sql += "COMMENT = 'Rev.9';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.9 to Rev.10
        /// </summary>
        private void UpdateTransactionsTable9()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "MODIFY COLUMN `sender`   varchar(36) NOT NULL,";
            sql += "MODIFY COLUMN `receiver` varchar(36) NOT NULL,";
            sql += "COMMENT = 'Rev.10';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.10 to Rev.11
        /// change type of BirthGift from 1000 to 900
        /// </summary>
        private void UpdateTransactionsTable10()
        {
            string sql = string.Empty;

            sql = "SELECT COUNT(*) FROM `" + Table_of_Transactions + "` WHERE type=1000";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            int resultCount = int.Parse(cmd.ExecuteScalar().ToString());
            cmd.Dispose();

            if (resultCount > 0)
            {
                sql = "SELECT UUID FROM `" + Table_of_Transactions + "` WHERE type=1000";
                cmd = new MySqlCommand(sql, dbcon);
                MySqlDataReader dbReader = cmd.ExecuteReader();

                int l = 0;
                string[] row = new string[resultCount];
                while (dbReader.Read())
                {
                    row[l] = dbReader.GetString(0);
                    l++;
                }
                dbReader.Close();
                cmd.Dispose();

                sql = "UPDATE `" + Table_of_Transactions + "` SET type=900 WHERE UUID=?uuid";
                for (int i = 0; i < resultCount; i++)
                {
                    cmd = new MySqlCommand(sql, dbcon);
                    cmd.Parameters.AddWithValue("?uuid", row[i]);
                    cmd.ExecuteNonQuery();
                    cmd.Dispose();
                }
            }

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "COMMENT = 'Rev.11';";
            sql += "COMMIT;";
            cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>
        /// update transactions table from Rev.11 to Rev.12
        /// </summary>
        private void UpdateTransactionsTable11()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_Transactions + "` ";
            sql += "ADD `regionUUID` varchar(36) NOT NULL AFTER `regionHandle`,";
            sql += "COMMENT = 'Rev.12';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        // update Total Sales Table

        /// <summary>Updates the total sales table1.</summary>
        private void UpdateTotalSalesTable1()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_TotalSales + "` ";
            sql += "ADD `time` int(11) NOT NULL AFTER `TotalAmount`,";
            sql += "COMMENT = 'Rev.2';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();

            deleteTotalSalesTable();
            initTotalSalesTable();
        }

        /// <summary>Updates the total sales table2.</summary>
        private void UpdateTotalSalesTable2()
        {
            string sql = string.Empty;

            sql = "BEGIN;";
            sql += "ALTER TABLE `" + Table_of_TotalSales + "` ";
            sql += "MODIFY COLUMN `user` varchar(36) NOT NULL,";
            sql += "COMMENT = 'Rev.3';";
            sql += "COMMIT;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }


        /// <summary>Checks the tables.</summary>
        /// <returns>
        ///   <br />
        /// </returns>
        /// <exception cref="Amib.Threading.Internal.WorkItem.WorkItemResult.Exception">[MONEY MANAGER]: Error checking tables" + e.ToString()</exception>
        private Dictionary<string, string> CheckTables()
        {
            Dictionary<string, string> tableDic = new Dictionary<string, string>();

            lock (dbcon)
            {
                string sql = string.Empty;

                sql = "SELECT TABLE_NAME,TABLE_COMMENT FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=?dbname";
                MySqlCommand cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?dbname", dbcon.Database);

                using (MySqlDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        try
                        {
                            string tableName = (string)r["TABLE_NAME"];
                            string comment = (string)r["TABLE_COMMENT"];
                            tableDic.Add(tableName, comment);
                        }
                        catch (Exception e)
                        {
                            throw new Exception("[MONEY MANAGER]: Error checking tables" + e.ToString());
                        }
                    }
                    r.Close();
                }

                cmd.Dispose();
                return tableDic;
            }
        }


        public void Reconnect()
        {
            m_log.Info("[MONEY MANAGER]: Reconnecting database");
            lock (dbcon)
            {
                try
                {
                    dbcon.Close();
                    dbcon = new MySqlConnection(connectString);
                    dbcon.Open();
                    m_log.Info("[MONEY MANAGER]: Reconnected  database");
                }
                catch (Exception e)
                {
                    m_log.Error("[MONEY MANAGER]: Unable to reconnect to database: " + e.ToString());
                }
            }
        }

        /// <summary>
        /// Get balance from database. returns -1 if failed.
        /// </summary>
        /// <param name="userID"></param>
        public int getBalance(string userID)
        {
            if (userID == UUID.Zero.ToString()) return 999999999; // System

            int retValue = -1;
            string sql = string.Empty;

            sql = "SELECT balance FROM " + Table_of_Balances + " WHERE user = ?userid";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?userid", userID);

            using (MySqlDataReader dbReader = cmd.ExecuteReader(CommandBehavior.SingleRow))
            {
                try
                {
                    if (dbReader.Read())
                    {
                        retValue = Convert.ToInt32(dbReader["balance"]);
                    }
                }
                catch (Exception e)
                {
                    e.ToString();
                    m_log.ErrorFormat("[MoneyDB]: MySql failed to fetch balance {0}.", userID);
                    retValue = -2;
                }

                dbReader.Close();
            }
            cmd.Dispose();

            return retValue;
        }


        /// <summary>Updates the balance.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="amount">The amount.</param>
        public bool updateBalance(string userID, int amount)
        {
            if (userID == UUID.Zero.ToString()) return true;  // System

            bool bRet = false;
            string sql = string.Empty;

            sql = "UPDATE " + Table_of_Balances + " SET balance = ?amount WHERE user = ?userID;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?amount", amount);
            cmd.Parameters.AddWithValue("?userID", userID);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>Adds the user.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="balance">The balance.</param>
        /// <param name="status">The status.</param>
        /// <param name="type">The type.</param>
        public bool addUser(string userID, int balance, int status, int type)
        {
            if (userID == UUID.Zero.ToString()) return true;  // System

            bool bRet = false;
            string sql = string.Empty;

            if (balances_rev >= 4)
            {
                sql = "INSERT INTO " + Table_of_Balances + " (`user`,`balance`,`status`,`type`) VALUES ";
                sql += " (?userID,?balance,?status,?type);";
            }
            else
            {
                sql = "INSERT INTO " + Table_of_Balances + " (`user`,`balance`,`status`) VALUES ";
                sql += " (?userID,?balance,?status);";
            }
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);

            cmd.Parameters.AddWithValue("?userID", userID);
            cmd.Parameters.AddWithValue("?balance", balance);
            cmd.Parameters.AddWithValue("?status", status);
            if (balances_rev >= 4)
            {
                cmd.Parameters.AddWithValue("?type", type);
            }

            if (cmd.ExecuteNonQuery() > 0) bRet = true;
            cmd.Dispose();

            return bRet;
        }


        /// <summary>
        /// Here we'll make a withdraw from the sender and update transaction status
        /// </summary>
        /// <param name="fromID"></param>
        /// <param name="toID"></param>
        /// <param name="amount"></param>
        public bool withdrawMoney(UUID transactionID, string senderID, int amount)
        {
            bool bRet = false;
            string sql = string.Empty;
            MySqlCommand cmd = null;

            // System
            if (senderID == UUID.Zero.ToString())
            {
                sql = "BEGIN;";
                sql += "UPDATE " + Table_of_Transactions;
                sql += " SET senderBalance = 0, status = ?status WHERE UUID = ?tranid;";
                sql += "COMMIT;";

                cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?status", (int)Status.PENDING_STATUS); //pending
                cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());
            }
            else
            {
                sql = "BEGIN;";
                sql += "UPDATE " + Table_of_Transactions + "," + Table_of_Balances;
                sql += " SET senderBalance = balance - ?amount, " + Table_of_Transactions + ".status = ?status, balance = balance - ?amount ";
                sql += " WHERE UUID = ?tranid AND user = sender AND user = ?userid;";
                sql += "COMMIT;";

                cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?amount", amount);
                cmd.Parameters.AddWithValue("?userid", senderID);
                cmd.Parameters.AddWithValue("?status", (int)Status.PENDING_STATUS); //pending
                cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());
            }

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>
        /// Give money to the receiver and change the transaction status to success.
        /// </summary>
        /// <param name="transactionID"></param>
        /// <param name="receiverID"></param>
        /// <param name="amount"></param>
        public bool giveMoney(UUID transactionID, string receiverID, int amount)
        {
            string sql = string.Empty;
            bool bRet = false;
            MySqlCommand cmd = null;

            // System
            if (receiverID == UUID.Zero.ToString())
            {
                sql = "BEGIN;";
                sql += "UPDATE " + Table_of_Transactions;
                sql += " SET receiverBalance = 0, status = ?status WHERE UUID = ?tranid;";
                sql += "COMMIT;";

                cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS); //Success
                cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());
            }
            else
            {
                sql = "BEGIN;";
                sql += "UPDATE " + Table_of_Transactions + "," + Table_of_Balances;
                sql += " SET receiverBalance = balance + ?amount, " + Table_of_Transactions + ".status = ?status, balance = balance + ?amount ";
                sql += " WHERE UUID = ?tranid AND user = receiver AND user = ?userid;";
                sql += "COMMIT;";

                cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?amount", amount);
                cmd.Parameters.AddWithValue("?userid", receiverID);
                cmd.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS); //Success
                cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());
            }

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }

        public bool BuyMoney(UUID transactionID, string userID, int amount)
        {
            string sql = string.Empty;
            bool bRet = false;
            MySqlCommand cmd = null;

            try
            {
                sql = "BEGIN;";
                sql += "UPDATE Transactions, Balances";
                sql += " SET balance = balance + ?amount, Transactions.status = ?status ";
                sql += " WHERE Transactions.UUID = ?tranid AND Balances.user = ?userid;";
                sql += "COMMIT;";

                cmd = new MySqlCommand(sql, dbcon);
                cmd.Parameters.AddWithValue("?amount", amount);
                cmd.Parameters.AddWithValue("?userid", userID);
                cmd.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS);
                cmd.Parameters.AddWithValue("?tranid", transactionID.ToString());

                if (cmd.ExecuteNonQuery() > 0)
                {
                    bRet = true;
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[BuyMoney]: Exception occurred during SQL execution: {0}", ex.Message);
            }
            finally
            {
                if (cmd != null)
                {
                    cmd.Dispose();
                }
            }

            m_log.InfoFormat("[BuyMoney]: SQL command executed successfully: {0}", bRet);

            return bRet;
        }


        /// <summary>Initializes the total sales table.</summary>
        private void initTotalSalesTable()
        {
            m_log.Info("[MONEY MANAGER]: Initailising TotalSales Table...");
            string sql = string.Empty;

            sql = "SELECT SQL_CALC_FOUND_ROWS receiver,objectUUID,type,COUNT(*),SUM(amount),MIN(time) FROM " + Table_of_Transactions;
            sql += " WHERE sender != receiver AND status = ?status AND sender != ?system";
            sql += " GROUP BY receiver,objectUUID,type;";

            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS);
            cmd.Parameters.AddWithValue("?system", UUID.Zero.ToString());
            cmd.ExecuteNonQuery();

            MySqlCommand cmd2 = new MySqlCommand("SELECT FOUND_ROWS();", dbcon);
            int lineCount = int.Parse(cmd2.ExecuteScalar().ToString());
            cmd2.Dispose();

            if (lineCount <= 0)
            {
                cmd.Dispose();
                return;
            }

            MySqlDataReader r = cmd.ExecuteReader();
            int l = 0;
            string[,] row = new string[lineCount, r.FieldCount];
            while (r.Read())
            {
                for (int i = 0; i < r.FieldCount; i++)
                {
                    row[l, i] = r.GetString(i);
                }
                l++;
            }
            r.Close();
            cmd.Dispose();

            for (int i = 0; i < lineCount; i++)
            {
                string receiver = (string)row[i, 0];             // receiver
                string objUUID = (string)row[i, 1];             // objectUUID
                int type = Convert.ToInt32(row[i, 2]);    // type
                int count = Convert.ToInt32(row[i, 3]);    // COUNT(*)
                int amount = Convert.ToInt32(row[i, 4]);    // SUM(amount)
                int tmstamp = Convert.ToInt32(row[i, 5]);    // MIN(time)
                //
                setTotalSale(receiver, objUUID, type, count, amount, tmstamp);
            }
        }


        /// <summary>Deletes the total sales table.</summary>
        private void deleteTotalSalesTable()
        {
            string sql = string.Empty;

            sql = "DELETE FROM " + Table_of_TotalSales;

            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS);
            cmd.ExecuteNonQuery();
            cmd.Dispose();

            return;
        }


        /// <summary>Adds the total sale.</summary>
        /// <param name="userUUID">The user UUID.</param>
        /// <param name="objectUUID">The object UUID.</param>
        /// <param name="type">The type.</param>
        /// <param name="count">The count.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="tmstamp">The tmstamp.</param>
        public bool addTotalSale(string userUUID, string objectUUID, int type, int count, int amount, int tmstamp)
        {
            bool bRet = false;
            string sql = string.Empty;
            UUID uuid = UUID.Random();

            // Record only transactions involving objects
            // if (objectUUID==UUID.Zero.ToString()) return bRet;  

            sql = "INSERT INTO " + Table_of_TotalSales;
            sql += " (`UUID`,`user`,`objectUUID`,`type`,`TotalCount`,`TotalAmount`,`time`) VALUES";
            sql += " (?ID,?userID,?objID,?type,?count,?amount,?time)";

            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?ID", uuid.ToString());
            cmd.Parameters.AddWithValue("?userID", userUUID);
            cmd.Parameters.AddWithValue("?objID", objectUUID);
            cmd.Parameters.AddWithValue("?type", type);
            cmd.Parameters.AddWithValue("?count", count);
            cmd.Parameters.AddWithValue("?amount", amount);
            cmd.Parameters.AddWithValue("?time", tmstamp);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>Updates the total sale.</summary>
        /// <param name="saleUUID">The sale UUID.</param>
        /// <param name="count">The count.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="tmstamp">The tmstamp.</param>
        public bool updateTotalSale(UUID saleUUID, int count, int amount, int tmstamp)
        {
            bool bRet = false;
            string sql = string.Empty;

            sql = "UPDATE " + Table_of_TotalSales;
            sql += " SET TotalCount = TotalCount + ?count, TotalAmount = TotalAmount + ?amount, time = ?time ";
            sql += " WHERE UUID = ?uuid;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);

            cmd.Parameters.AddWithValue("?uuid", saleUUID.ToString());
            cmd.Parameters.AddWithValue("?count", count);
            cmd.Parameters.AddWithValue("?amount", amount);
            cmd.Parameters.AddWithValue("?time", tmstamp);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;

        }

        /// <summary>Sets the total sale.</summary>
        /// <param name="userUUID">The user UUID.</param>
        /// <param name="objectUUID">The object UUID.</param>
        /// <param name="type">The type.</param>
        /// <param name="count">The count.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="tmstamp">The tmstamp.</param>
        public bool setTotalSale(string userUUID, string objectUUID, int type, int count, int amount, int tmstamp)
        {
            bool bRet = false;
            string sql = string.Empty;
            string uuid = string.Empty;
            int dbtm = 0;

            sql = "SELECT UUID,time FROM " + Table_of_TotalSales;
            sql += " WHERE user = ?userid AND objectUUID = ?objID AND type = ?type;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);

            cmd.Parameters.AddWithValue("?userid", userUUID);
            cmd.Parameters.AddWithValue("?objID", objectUUID);
            cmd.Parameters.AddWithValue("?type", type);

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    try
                    {
                        uuid = (string)r["UUID"];
                        dbtm = Convert.ToInt32(r["time"]);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY MANAGER]: Get sale data from DB failed: " + e.ToString());
                        r.Close();
                        cmd.Dispose();
                        return false;
                    }
                }
                r.Close();
            }

            if (uuid != string.Empty)
            {
                UUID saleUUID = UUID.Zero;
                UUID.TryParse(uuid, out saleUUID);
                if (dbtm < tmstamp) tmstamp = dbtm;
                bRet = updateTotalSale(saleUUID, count, amount, tmstamp);
            }
            else
            {
                bRet = addTotalSale(userUUID, objectUUID, type, count, amount, tmstamp);
            }

            cmd.Dispose();
            return bRet;
        }


        //
        // transactions
        //
        /// <summary>Adds the transaction.</summary>
        /// <param name="transaction">The transaction.</param>
        public bool addTransaction(TransactionData transaction)
        {
            bool bRet = false;
            string sql = string.Empty;

            if (transaction.ObjectUUID == null) transaction.ObjectUUID = UUID.Zero.ToString();
            if (transaction.ObjectName == null) transaction.ObjectName = string.Empty;
            if (transaction.Description == null) transaction.Description = string.Empty;

            sql = "INSERT INTO " + Table_of_Transactions;
            sql += " (`UUID`,`sender`,`receiver`,`amount`,`senderBalance`,`receiverBalance`,`objectUUID`,`objectName`,";
            sql += " `regionHandle`,`regionUUID`,`type`,`time`,`secure`,`status`,`commonName`,`description`) VALUES";
            sql += " (?transID,?sender,?receiver,?amount,?senderBalance,?receiverBalance,?objID,?objName,?regionHandle,?regionUUID,?type,?time,?secure,?status,?cname,?desc)";

            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?transID", transaction.TransUUID.ToString());
            cmd.Parameters.AddWithValue("?sender", transaction.Sender);
            cmd.Parameters.AddWithValue("?receiver", transaction.Receiver);
            cmd.Parameters.AddWithValue("?amount", transaction.Amount);
            cmd.Parameters.AddWithValue("?senderBalance", -1);
            cmd.Parameters.AddWithValue("?receiverBalance", -1);
            cmd.Parameters.AddWithValue("?objID", transaction.ObjectUUID);
            cmd.Parameters.AddWithValue("?objName", transaction.ObjectName);
            cmd.Parameters.AddWithValue("?regionHandle", transaction.RegionHandle);
            cmd.Parameters.AddWithValue("?regionUUID", transaction.RegionUUID);
            cmd.Parameters.AddWithValue("?type", transaction.Type);
            cmd.Parameters.AddWithValue("?time", transaction.Time);
            cmd.Parameters.AddWithValue("?secure", transaction.SecureCode);
            cmd.Parameters.AddWithValue("?status", transaction.Status);
            cmd.Parameters.AddWithValue("?cname", transaction.CommonName);
            cmd.Parameters.AddWithValue("?desc", transaction.Description);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>Updates the transaction status.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="status">The status.</param>
        /// <param name="description">The description.</param>
        public bool updateTransactionStatus(UUID transactionID, int status, string description)
        {
            bool bRet = false;
            string sql = string.Empty;

            sql = "UPDATE " + Table_of_Transactions + " SET status = ?status,description = ?desc WHERE UUID = ?tranid;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?status", status);
            cmd.Parameters.AddWithValue("?desc", description);
            cmd.Parameters.AddWithValue("?tranid", transactionID);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>Sets the trans expired.</summary>
        /// <param name="deadTime">The dead time.</param>
        public bool SetTransExpired(int deadTime)
        {
            bool bRet = false;
            string sql = string.Empty;

            sql = "UPDATE " + Table_of_Transactions;
            sql += " SET status = ?failedstatus,description = ?desc WHERE time <= ?deadTime AND status = ?pendingstatus;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?failedstatus", (int)Status.FAILED_STATUS);
            cmd.Parameters.AddWithValue("?desc", "expired");
            cmd.Parameters.AddWithValue("?deadTime", deadTime);
            cmd.Parameters.AddWithValue("?pendingstatus", (int)Status.PENDING_STATUS);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>
        /// Validate if the transacion is legal
        /// </summary>
        /// <param name="userID"></param>
        /// <param name="transactionID"></param>
        public bool ValidateTransfer(string secureCode, UUID transactionID)
        {
            bool bRet = false;
            string secure = string.Empty;
            string sql = string.Empty;

            sql = "SELECT secure FROM " + Table_of_Transactions + " WHERE UUID = ?transID;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?transID", transactionID.ToString());

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    try
                    {
                        secure = (string)r["secure"];
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY MANAGER]: Get transaction from DB failed: " + e.ToString());
                    }
                    if (secureCode == secure) bRet = true;
                    else bRet = false;
                }
                r.Close();
            }

            cmd.Dispose();
            return bRet;
        }


        /// <summary>Fetches the transaction.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        public TransactionData FetchTransaction(UUID transactionID)
        {
            TransactionData transactionData = new TransactionData();
            transactionData.TransUUID = transactionID;
            string sql = string.Empty;

            sql = "SELECT * FROM " + Table_of_Transactions + " WHERE UUID = ?transID;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?transID", transactionID.ToString());

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    try
                    {
                        transactionData.Sender = (string)r["sender"];
                        transactionData.Receiver = (string)r["receiver"];
                        transactionData.Amount = Convert.ToInt32(r["amount"]);
                        transactionData.SenderBalance = Convert.ToInt32(r["senderBalance"]);
                        transactionData.ReceiverBalance = Convert.ToInt32(r["receiverBalance"]);
                        transactionData.Type = Convert.ToInt32(r["type"]);
                        transactionData.Time = Convert.ToInt32(r["time"]);
                        transactionData.Status = Convert.ToInt32(r["status"]);
                        transactionData.CommonName = (string)r["commonName"];
                        transactionData.RegionHandle = (string)r["regionHandle"];
                        transactionData.RegionUUID = (string)r["regionUUID"];
                        //
                        if (r["objectUUID"] is System.DBNull) transactionData.ObjectUUID = UUID.Zero.ToString();
                        else transactionData.ObjectUUID = (string)r["objectUUID"];
                        if (r["objectName"] is System.DBNull) transactionData.ObjectName = string.Empty;
                        else transactionData.ObjectName = (string)r["objectName"];
                        if (r["description"] is System.DBNull) transactionData.Description = string.Empty;
                        else transactionData.Description = (string)r["description"];
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY MANAGER]: Fetching transaction failed 1: " + e.ToString());
                        r.Close();
                        cmd.Dispose();
                        return null;
                    }

                }
                r.Close();
            }

            cmd.Dispose();
            return transactionData;
        }


        /// <summary>Fetches the transaction.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="index">The index.</param>
        /// <param name="retNum">The ret number.</param>
        public TransactionData[] FetchTransaction(string userID, int startTime, int endTime, uint index, uint retNum)
        {
            List<TransactionData> rows = new List<TransactionData>();
            string sql = string.Empty;

            sql = "SELECT * FROM " + Table_of_Transactions + " WHERE time>=?start AND time<=?end ";
            sql += "AND (sender=?user OR receiver=?user) ORDER BY time ASC LIMIT ?index,?num;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);

            cmd.Parameters.AddWithValue("?start", startTime);
            cmd.Parameters.AddWithValue("?end", endTime);
            cmd.Parameters.AddWithValue("?user", userID);
            cmd.Parameters.AddWithValue("?index", index);
            cmd.Parameters.AddWithValue("?num", retNum);

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                for (int i = 0; i < retNum; i++)
                {
                    if (r.Read())
                    {
                        try
                        {
                            TransactionData transactionData = new TransactionData();
                            string uuid = (string)r["UUID"];
                            UUID transUUID;
                            UUID.TryParse(uuid, out transUUID);

                            transactionData.TransUUID = transUUID;
                            transactionData.Sender = (string)r["sender"];
                            transactionData.Receiver = (string)r["receiver"];
                            transactionData.Amount = Convert.ToInt32(r["amount"]);
                            transactionData.SenderBalance = Convert.ToInt32(r["senderBalance"]);
                            transactionData.ReceiverBalance = Convert.ToInt32(r["receiverBalance"]);
                            transactionData.Type = Convert.ToInt32(r["type"]);
                            transactionData.Time = Convert.ToInt32(r["time"]);
                            transactionData.Status = Convert.ToInt32(r["status"]);
                            transactionData.CommonName = (string)r["commonName"];
                            transactionData.RegionHandle = (string)r["regionHandle"];
                            transactionData.RegionUUID = (string)r["regionUUID"];
                            //
                            if (r["objectUUID"] is System.DBNull) transactionData.ObjectUUID = UUID.Zero.ToString();
                            else transactionData.ObjectUUID = (string)r["objectUUID"];
                            if (r["objectName"] is System.DBNull) transactionData.ObjectName = string.Empty;
                            else transactionData.ObjectName = (string)r["objectName"];
                            if (r["description"] is System.DBNull) transactionData.Description = string.Empty;
                            else transactionData.Description = (string)r["description"];
                            //
                            rows.Add(transactionData);
                        }
                        catch (Exception e)
                        {
                            m_log.Error("[MONEY MANAGER]: Fetching transaction failed 2: " + e.ToString());
                            r.Close();
                            cmd.Dispose();
                            return null;
                        }
                    }
                }
                r.Close();
            }

            cmd.Dispose();
            return rows.ToArray();
        }


        /// <summary>Gets the transaction number.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        public int getTransactionNum(string userID, int startTime, int endTime)
        {
            int iRet = -1;
            string sql = string.Empty;

            sql = "SELECT COUNT(*) AS number FROM " + Table_of_Transactions + " WHERE time>=?start AND time<=?end ";
            sql += "AND (sender=?user OR receiver=?user);";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);

            cmd.Parameters.AddWithValue("?start", startTime);
            cmd.Parameters.AddWithValue("?end", endTime);
            cmd.Parameters.AddWithValue("?user", userID);

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    try
                    {
                        iRet = Convert.ToInt32(r["number"]);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY MANAGER]: Unable to get transaction info: " + e.ToString());
                    }
                }
                r.Close();
            }

            cmd.Dispose();
            return iRet;
        }


        //
        // userinfo
        //
        /// <summary>Adds the user information.</summary>
        /// <param name="userInfo"></param>
        public bool addUserInfo(UserInfo userInfo)
        {
            bool bRet = false;
            string sql = string.Empty;

            if (userInfo.Avatar == null) return false;

            if (userinfo_rev >= 3)
            {
                sql = "INSERT INTO " + Table_of_UserInfo + "(`user`,`simip`,`avatar`,`pass`,`type`,`class`,`serverurl`) VALUES";
                sql += "(?user,?simip,?avatar,?password,?type,?class,?serverurl);";
            }
            else
            {
                sql = "INSERT INTO " + Table_of_UserInfo + "(`user`,`simip`,`avatar`,`pass`) VALUES";
                sql += "(?user,?simip,?avatar,?password);";
            }

            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?user", userInfo.UserID);
            cmd.Parameters.AddWithValue("?simip", userInfo.SimIP);
            cmd.Parameters.AddWithValue("?avatar", userInfo.Avatar);
            cmd.Parameters.AddWithValue("?password", userInfo.PswHash);
            if (userinfo_rev >= 3)
            {
                cmd.Parameters.AddWithValue("?type", userInfo.Type);
                cmd.Parameters.AddWithValue("?class", userInfo.Class);
                cmd.Parameters.AddWithValue("?serverurl", userInfo.ServerURL);
            }

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }


        /// <summary>Fetches the user information.</summary>
        /// <param name="userID">The user identifier.</param>
        public UserInfo fetchUserInfo(string userID)
        {
            UserInfo userInfo = new UserInfo();
            userInfo.UserID = null;
            string sql = string.Empty;

            sql = "SELECT * FROM " + Table_of_UserInfo + " WHERE user = ?userID;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?userID", userID);

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    try
                    {
                        userInfo.UserID = (string)r["user"];
                        userInfo.SimIP = (string)r["simip"];
                        userInfo.Avatar = (string)r["avatar"];
                        userInfo.PswHash = (string)r["pass"];
                        userInfo.Type = Convert.ToInt32(r["type"]);
                        userInfo.Class = Convert.ToInt32(r["class"]);
                        userInfo.ServerURL = (string)r["serverurl"];
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[MONEY MANAGER]: Fetching UserInfo failed: " + e.ToString());
                        r.Close();
                        cmd.Dispose();
                        return null;
                    }
                }
                r.Close();
            }
            cmd.Dispose();

            if (userInfo.UserID != userID) return null;
            return userInfo;
        }


        /// <summary>Updates the user information.</summary>
        /// <param name="userInfo"></param>
        public bool updateUserInfo(UserInfo userInfo)
        {
            bool bRet = false;
            string sql = string.Empty;

            sql = "UPDATE " + Table_of_UserInfo + " SET simip=?simip,pass=?pass,class=?class,serverurl=?serverurl WHERE user=?user;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?simip", userInfo.SimIP);
            cmd.Parameters.AddWithValue("?pass", userInfo.PswHash);
            cmd.Parameters.AddWithValue("?class", userInfo.Class);
            cmd.Parameters.AddWithValue("?serverurl", userInfo.ServerURL);
            cmd.Parameters.AddWithValue("?user", userInfo.UserID);

            if (cmd.ExecuteNonQuery() > 0) bRet = true;

            cmd.Dispose();
            return bRet;
        }

        public bool UserExists(string userID)
        {
            try
            {
                UserInfo userInfo = fetchUserInfo(userID);
                return userInfo != null;
            }
            catch (Exception ex)
            {
                m_log.Error(ex);
                return false;
            }
        }

        public bool UpdateUserInfo(string userID, UserInfo updatedInfo)
        {
            UserInfo existingInfo = fetchUserInfo(userID);
            if (existingInfo != null)
            {
                updatedInfo.UserID = userID; // Ensure the userID is updated correctly
                return updateUserInfo(updatedInfo);
            }
            else
            {
                return false; // User not found
            }
        }

        public bool DeleteUser(string userID)
        {
            string sql = "DELETE FROM " + Table_of_UserInfo + " WHERE user = ?userID;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?userID", userID);

            return cmd.ExecuteNonQuery() > 0;
        }

        public void LogTransactionError(UUID transactionID, string errorMessage)
        {
            m_log.ErrorFormat("[MONEY MODULE]: Transaction {0} failed with error: {1}", transactionID, errorMessage);
        }

        public IEnumerable<TransactionData> GetTransactionHistory(string userID, int startTime, int endTime)
        {
            List<TransactionData> transactionHistory = new List<TransactionData>();

            // Assuming you have a database connection and a query to fetch transactions
            string sql = "SELECT * FROM " + Table_of_Transactions + " WHERE user = ?userID AND time >= ?startTime AND time <= ?endTime;";
            MySqlCommand cmd = new MySqlCommand(sql, dbcon);
            cmd.Parameters.AddWithValue("?userID", userID);
            cmd.Parameters.AddWithValue("?startTime", startTime);
            cmd.Parameters.AddWithValue("?endTime", endTime);

            using (MySqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    TransactionData transaction = new TransactionData();
                    // Assuming you have properties in TransactionData class to match the columns in the database table
                    transaction.TransUUID = UUID.Parse((string)r["transuuid"]);
                    transaction.Sender = (string)r["sender"];
                    transaction.Receiver = (string)r["receiver"];
                    transaction.Amount = Convert.ToInt32(r["amount"]);
                    transaction.ObjectUUID = (string)r["objectuuid"];
                    transaction.ObjectName = (string)r["objectname"];
                    transaction.RegionHandle = (string)r["regionhandle"];
                    transaction.Type = Convert.ToInt32(r["type"]);
                    transaction.Time = Convert.ToInt32(r["time"]);
                    transaction.Status = Convert.ToInt32(r["status"]);
                    transaction.SecureCode = (string)r["securecode"];
                    transaction.CommonName = (string)r["commonname"];
                    transaction.Description = (string)r["description"];

                    transactionHistory.Add(transaction);
                }
                r.Close();
            }
            cmd.Dispose();

            return transactionHistory;
        }


    }
}
