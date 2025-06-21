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
Die Klasse MoneyDBService implementiert ein Datenbank-Interface für einen Währungsserver (IMoneyDBService). 
Sie verwaltet u.a. Benutzerkonten, Transaktionen, Transfers, Guthaben und Fehlerprotokollierung über eine MySQL-Datenbank.
Null-Pointer-Checks & Fehlerquellen

1. Konstruktor und Initialisierung
    Es gibt einen string-Parameter-Konstruktor und einen parameterlosen Konstruktor.
    Der Initialisierungsprozess prüft, ob der Connection-String leer ist (if (connectionString != string.Empty)), aber nicht explizit auf null.
    → Ein null-Wert könnte zu einem Fehler führen, bevor Zeile 74 erreicht wird.

2. DB-Verbindungsmanagement
    GetLockedConnection() prüft, ob die Verbindungsanzahl korrekt gesetzt ist und ob ein Connection-Objekt im Pool existiert.
    Bei fehlender Verbindung wird ein Fehler geloggt und eine Exception geworfen (KeyNotFoundException).
    Die Methode ist robust gegen NullPointer, solange die Initialisierung korrekt verlief.

3. Methoden für Datenbankoperationen
    Fast alle Datenbankmethoden sind in try-catch-finally-Blöcke gekapselt:
        Nach MySQL-Fehlern wird ein Reconnect versucht und die Operation wiederholt.
        Bei anderen Exceptions wird ein Fehler geloggt und ein sinnvoller Rückgabewert geliefert (z.B. 0, false, oder leere Objekte).
        Ressourcenfreigabe (dbm.Release()) geschieht immer im finally-Block, was Memory Leaks vorbeugt.
    Rückgabewerte von Methoden wie FetchTransaction werden auf null geprüft, bevor sie weiterverwendet werden.
    Methoden, die Objekte aus der DB holen, geben bei Fehlern null oder leere Listen zurück.

4. SQL- und Parameterhandling
    Vor SQL-Operationen werden die Parameter korrekt gesetzt.
    Es gibt keine offensichtlichen SQL-Injection-Lücken, da überall Parameter verwendet werden.

5. Typische Fehlerquellen
    Fehlende Null-Checks bei Eingaben: Die meisten Methoden prüfen nicht explizit, ob string-Parameter wie userID oder agentId null oder leer sind. Das könnte zu Fehlern führen, wenn solche Werte übergeben werden.
    Release von Verbindungen: In einigen Methoden (z.B. DoTransfer, DoAddMoney) wird die Verbindung teilweise vorzeitig bei Fehlern freigegeben, aber insgesamt ist das Ressourcenmanagement solide.
    Fehlende Existenzprüfungen: Manche Methoden gehen davon aus, dass DB-Objekte wie TransactionData oder UserInfo existieren und korrekt initialisiert sind.

6. Sonstige Fehlerbehandlung
    Fehler werden fast immer geloggt (m_log.ErrorFormat, m_log.Error).
    In kritischen Fällen werden Exceptions weitergeworfen, in anderen Fällen gibt es Fallback-Rückgaben ohne Exception.

Zusammenfassung
    Funktion:
    Verwaltung des Währungsverkehrs (Konten, Transaktionen, Guthaben) für OpenSim über eine MySQL-Datenbank.
    NullPointer/Fehlerquellen:
        Fast überall sauber gegen NullPointer und Datenbankfehler abgesichert (try-catch-finally).
        Mögliche Risiken bei Methodenparametern, die nicht explizit auf null geprüft werden.
        Bei fehlerhafter Initialisierung oder fehlerhaftem Connection-String kann es zu Start-Exceptions kommen.
    Verbesserungspotential:
        Zusätzliche Null-Checks und Plausibilitätsprüfungen für alle Methodenparameter.
        Noch mehr Logging in Ausnahmefällen.
 */

using log4net;
using MySql.Data.MySqlClient;

using Mysqlx.Crud;
using Mysqlx.Notice;
using OpenMetaverse;
using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using OpenSim.Modules.Currency;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace OpenSim.Grid.MoneyServer
{
    class MoneyDBService : IMoneyDBService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private string m_connect;
        private long TicksToEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        // DB manager pool
        protected Dictionary<int, MySQLSuperManager> m_dbconnections = new Dictionary<int, MySQLSuperManager>();	// with Lock
        private int m_maxConnections;

        public int m_lastConnect = 0;

        public MoneyDBService(string connect)
        {
            m_connect = connect;
            Initialise(m_connect, 10);
        }

        public MoneyDBService()
        {
        }

        public readonly object connectionLock = new object();

        public void Initialise(string connectionString, int maxDBConnections)
        {
            lock (connectionLock)
            {
                if (maxDBConnections <= 0)
                {
                    throw new ArgumentException("maxDBConnections must be greater than zero", nameof(maxDBConnections));
                }

                //m_log.InfoFormat("[Initialise]: Setting m_maxConnections to {0}", maxDBConnections);
                m_connect = connectionString;
                m_maxConnections = maxDBConnections;

                //m_log.InfoFormat("[Initialise]: m_maxConnections is now {0}", m_maxConnections);

                if (connectionString != string.Empty)
                {
                    for (int i = 0; i < m_maxConnections; i++)
                    {
                        //m_log.Info("Connecting to DB... [" + i + "]");
                        MySQLSuperManager msm = new MySQLSuperManager(connectionString);
                        m_dbconnections.Add(i, msm);
                    }
                }
                else
                {
                    m_log.Error("[MONEY DB]: Connection string is null, initialise database failed");
                    throw new Exception("Failed to initialise MySql database");
                }
            }
        }
        public MySQLSuperManager GetLockedConnection()
        {
            lock (connectionLock)
            {
                //m_log.InfoFormat("[GetLockedConnection]: m_maxConnections is {0}", m_maxConnections);
                if (m_maxConnections == 0)
                {
                    throw new InvalidOperationException("m_maxConnections cannot be zero.");
                }

                int lockedCons = 0;
                while (true)
                {
                    m_lastConnect++;
                    //m_log.DebugFormat("GetLockedConnection: m_lastConnect incremented to {0}", m_lastConnect);

                    if (m_lastConnect == int.MaxValue)
                    {
                        m_lastConnect = 0;
                        //m_log.Debug("GetLockedConnection: m_lastConnect overflow, resetting to 0");
                    }

                    int index = m_lastConnect % m_maxConnections;
                    if (!m_dbconnections.ContainsKey(index))
                    {
                        m_log.ErrorFormat("GetLockedConnection: Invalid connection index {0}", index);
                        throw new KeyNotFoundException($"The given key '{index}' was not present in the dictionary");
                    }

                    MySQLSuperManager msm = m_dbconnections[index];
                    //m_log.DebugFormat("GetLockedConnection: Checking connection {0} for lock", msm);
                    if (!msm.Locked)
                    {
                        msm.GetLock();
                        //m_log.DebugFormat("GetLockedConnection: Connection {0} locked successfully", msm);
                        return msm;
                    }

                    lockedCons++;
                    //m_log.DebugFormat("GetLockedConnection: Connection {0} is locked, trying next one. lockedCons: {1}", msm, lockedCons);

                    if (lockedCons > m_maxConnections)
                    {
                        lockedCons = 0;
                        System.Threading.Thread.Sleep(2000);
                        m_log.Warn("GetLockedConnection: All connections are in use. Probable cause: Something didn't release a mutex properly, or high volume of requests inbound.");
                    }
                }
            }
        }

        public void Reconnect()
        {
            //m_log.Debug("Reconnect attempt started.");

            if (m_maxConnections <= 0)
            {
                throw new InvalidOperationException("m_maxConnections must be greater than zero.");
            }

            for (int i = 0; i < m_maxConnections; i++)
            {
                MySQLSuperManager msm = m_dbconnections[i];
                try
                {
                    msm.Manager.Reconnect();
                    //m_log.DebugFormat("Reconnected to database connection {0} successfully.", i);
                }
                catch (Exception ex)
                {
                    m_log.ErrorFormat("Failed to reconnect to database connection {0}: {1}", i, ex.Message);
                }
            }
            //m_log.Debug("Reconnect attempt completed.");
        }
         
        public int getBalance(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.getBalance(userID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.getBalance(userID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return 0;
            }
            finally
            {
                dbm.Release();
            }
        }
               
        public int CheckMaximumMoney(string userID, int m_CurrencyMaximum)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            // Beispielwert, sollte aus der Konfigurationsdatei MoneyServer.ini geladen werden
            m_log.InfoFormat("[CHECK MAXIMUM MONEY]: Currency Maximum: {0}", m_CurrencyMaximum);
            if (m_CurrencyMaximum <= 0)
            {
                m_CurrencyMaximum = 1000;
            }

            try
            {
                // Ausnahmen f�r SYSTEM und BANKER
                if (userID == "SYSTEM" || userID == "BANKER")
                {
                    return 0; // Keine Begrenzung f�r diese Benutzer
                }

                // Abrufen des aktuellen Guthabens des Benutzers
                string sql = "SELECT balance FROM balances WHERE user = ?userID";
                int currentBalance = 0;

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            currentBalance = reader.GetInt32("balance");
                        }
                    }
                }

                // �berpr�fen, ob das Guthaben �ber dem Maximum liegt und ggf. abziehen
                if (currentBalance > m_CurrencyMaximum)
                {
                    int excessAmount = currentBalance - m_CurrencyMaximum;

                    // Guthaben auf das Maximum reduzieren
                    sql = "UPDATE balances SET balance = ?newBalance WHERE user = ?userID";
                    using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                    {
                        cmd.Parameters.AddWithValue("?newBalance", m_CurrencyMaximum);
                        cmd.Parameters.AddWithValue("?userID", userID);
                        cmd.ExecuteNonQuery();
                    }

                    m_log.InfoFormat("[CheckMaximumMoney]: Reduced balance for user {0} by {1} to enforce maximum limit of {2}", userID, excessAmount, m_CurrencyMaximum);
                    return excessAmount; // R�ckgabe des abgezogenen Betrags
                }

                return 0; // Keine �nderung, falls das Guthaben innerhalb des Limits liegt
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[CheckMaximumMoney]: Error checking and updating user balance: {0}", ex.Message);
                throw;
            }
            finally
            {
                dbm.Release();
            }
        }




        /// <summary>Withdraws the money.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="senderID">The sender identifier.</param>
        /// <param name="amount">The amount.</param>
        public bool withdrawMoney(UUID transactionID, string senderID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.withdrawMoney(transactionID, senderID, amount);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.withdrawMoney(transactionID, senderID, amount);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }
        public bool giveMoney(UUID transactionID, string receiverID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            //m_log.Debug("giveMoney: Got locked connection");

            //m_log.DebugFormat("giveMoney: Trying to give {0} units to {1} with transaction ID {2}", amount, receiverID, transactionID);          

            try
            {
                //m_log.Debug("giveMoney: Trying to execute giveMoney on database");
                bool result = dbm.Manager.giveMoney(transactionID, receiverID, amount);
                //m_log.DebugFormat("giveMoney: giveMoney on database returned {0}", result);
                return result;
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                m_log.ErrorFormat("giveMoney: MySqlException caught: {0}", e.Message);
                dbm.Manager.Reconnect();
                //m_log.Debug("giveMoney: Reconnected to database");
                return dbm.Manager.giveMoney(transactionID, receiverID, amount);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("giveMoney: Exception caught: {0}", e.Message);
                return false;
            }
            finally
            {
                dbm.Release();
                //m_log.Debug("giveMoney: Released connection");
            }
        }

        public bool BuyMoney(UUID transactionID, string userID, int amount)
        {
            //m_log.DebugFormat("[BuyMoney]: Start - transactionID: {0}, userID: {1}, amount: {2}", transactionID, userID, amount);

            MySQLSuperManager dbm = GetLockedConnection();
            string sql = "UPDATE balances SET balance = balance + ?amount WHERE user = ?userID";

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?userID", userID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(transactionID, userID, amount);
                    }

                    //m_log.DebugFormat("[BuyMoney]: End - transactionID: {0}, userID: {1}, amount: {2}, result: {3}", transactionID, userID, amount, result);
                    return result;
                }
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                m_log.ErrorFormat("[BuyMoney]: SQL Exception - {0}", e.Message);
                dbm.Manager.Reconnect();

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?userID", userID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(transactionID, userID, amount);
                    }

                    //m_log.DebugFormat("[BuyMoney]: End after Reconnect - transactionID: {0}, userID: {1}, amount: {2}, result: {3}", transactionID, userID, amount, result);
                    return result;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[BuyMoney]: General Exception - {0}", e.Message);
                return false;
            }
            finally
            {
                dbm.Release();
                //m_log.Debug("[BuyMoney]: Connection released");
            }
        }

        public void LogTransaction(UUID transactionID, string userID, int amount)
        {
            //string sql = "INSERT INTO transactions (transactionID, userID, amount, timestamp) VALUES (?transactionID, ?userID, ?amount, ?timestamp)";
            string sql = "INSERT INTO transactions (userID, amount, timestamp) VALUES (?userID, ?amount, ?timestamp)";

            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    //cmd.Parameters.AddWithValue("?transactionID", transactionID.ToString());
                    cmd.Parameters.AddWithValue("?userID", userID);
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?timestamp", DateTime.UtcNow);

                    cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool BuyCurrency(string userID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData transaction = new TransactionData();
            transaction.TransUUID = UUID.Random();
            transaction.Sender = UUID.Zero.ToString();  // System sender
            transaction.Receiver = userID;
            transaction.Amount = amount;
            transaction.ObjectUUID = UUID.Zero.ToString();
            transaction.ObjectName = string.Empty;
            transaction.RegionHandle = string.Empty;
            transaction.Type = (int)TransactionType.BuyMoney; // Angenommen, BuyMoney ist ein g�ltiger Transaktionstyp ???
            transaction.Time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);
            transaction.Status = (int)Status.PENDING_STATUS;
            transaction.SecureCode = UUID.Random().ToString();
            transaction.CommonName = string.Empty;
            transaction.Description = "BuyCurrency " + DateTime.UtcNow.ToString();

            bool ret = addTransaction(transaction);
            if (!ret)
            {
                dbm.Release();
                return false;
            }

            try
            {
                // F�ge Geld dem Benutzerkonto hinzu
                ret = giveMoney(transaction.TransUUID, userID, amount);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                m_log.Error("[BuyCurrency]: SQL Exception - " + e.ToString());
                dbm.Manager.Reconnect();
                ret = giveMoney(transaction.TransUUID, userID, amount);
            }
            catch (Exception e)
            {
                m_log.Error("[BuyCurrency]: Exception - " + e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }

            if (ret)
            {
                m_log.InfoFormat("[BuyCurrency]: Successfully bought currency for user {0} in amount {1}", userID, amount);
            }
            return ret;
        }

        /// <summary>Sets the total sale.</summary>
        /// <param name="transaction">The transaction.</param>
        public bool setTotalSale(TransactionData transaction)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            if (transaction.Receiver == transaction.Sender) return false;
            if (transaction.Sender == UUID.Zero.ToString()) return false;            

            int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);
            try
            {
                return dbm.Manager.setTotalSale(transaction.Receiver, transaction.ObjectUUID, transaction.Type, 1, transaction.Amount, time);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.setTotalSale(transaction.Receiver, transaction.ObjectUUID, transaction.Type, 1, transaction.Amount, time);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Adds the transaction.</summary>
        /// <param name="transaction">The transaction.</param>
        public bool addTransaction(TransactionData transaction)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.addTransaction(transaction);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.addTransaction(transaction);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Adds the user.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="balance">The balance.</param>
        /// <param name="status">The status.</param>
        /// <param name="type">The type.</param>
        public bool addUser(string userID, int balance, int status, int type)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData transaction = new TransactionData();
            transaction.TransUUID = UUID.Random();
            transaction.Sender = UUID.Zero.ToString();
            transaction.Receiver = userID;
            transaction.Amount = balance;
            transaction.ObjectUUID = UUID.Zero.ToString();
            transaction.ObjectName = string.Empty;
            transaction.RegionHandle = string.Empty;
            transaction.Type = (int)TransactionType.BirthGift;
            transaction.Time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000); ;
            transaction.Status = (int)Status.PENDING_STATUS;
            transaction.SecureCode = UUID.Random().ToString();
            transaction.CommonName = string.Empty;
            transaction.Description = "addUser " + DateTime.UtcNow.ToString();

            bool ret = addTransaction(transaction);
            if (!ret)
            {
                dbm.Release();
                return false;
            }

            try
            {
                ret = dbm.Manager.addUser(userID, 0, status, type);		// make Balance Table
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                ret = dbm.Manager.addUser(userID, 0, status, type);     // make Balance Table
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }

            //
            if (ret) ret = giveMoney(transaction.TransUUID, userID, balance);
            return ret;
        }

        /// <summary>Updates the transaction status.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="status">The status.</param>
        /// <param name="description">The description.</param>
        public bool updateTransactionStatus(UUID transactionID, int status, string description)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.updateTransactionStatus(transactionID, status, description);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.updateTransactionStatus(transactionID, status, description);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Sets the trans expired.</summary>
        /// <param name="deadTime">The dead time.</param>
        public bool SetTransExpired(int deadTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.SetTransExpired(deadTime);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.SetTransExpired(deadTime);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Validates the transfer.</summary>
        /// <param name="secureCode">The secure code.</param>
        /// <param name="transactionID">The transaction identifier.</param>
        public bool ValidateTransfer(string secureCode, UUID transactionID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.ValidateTransfer(secureCode, transactionID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.ValidateTransfer(secureCode, transactionID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Fetches the transaction.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        public TransactionData FetchTransaction(UUID transactionID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.FetchTransaction(transactionID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.FetchTransaction(transactionID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Fetches the transaction.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="lastIndex">The last index.</param>
        public TransactionData FetchTransaction(string userID, int startTime, int endTime, int lastIndex)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData[] arrTransaction;

            uint index = 0;
            if (lastIndex >= 0) index = Convert.ToUInt32(lastIndex) + 1;

            try
            {
                arrTransaction = dbm.Manager.FetchTransaction(userID, startTime, endTime, index, 1);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                arrTransaction = dbm.Manager.FetchTransaction(userID, startTime, endTime, index, 1);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return null;
            }
            finally
            {
                dbm.Release();
            }

            //
            if (arrTransaction.Length > 0)
            {
                return arrTransaction[0];
            }
            else
            {
                return null;
            }
        }

        /// <summary>Does the transfer.</summary>
        /// <param name="transactionUUID">The transaction UUID.</param>
        public bool DoTransfer(UUID transactionUUID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            bool do_trans = false;

            TransactionData transaction = new TransactionData();
            transaction = FetchTransaction(transactionUUID);

            if (transaction != null && transaction.Status == (int)Status.PENDING_STATUS)
            {
                int balance = getBalance(transaction.Sender);

                //check the amount
                if (transaction.Amount >= 0 && balance >= transaction.Amount)
                {
                    if (withdrawMoney(transactionUUID, transaction.Sender, transaction.Amount))
                    {
                        //If receiver not found, add it to DB.
                        if (getBalance(transaction.Receiver) == -1)
                        {
                            m_log.ErrorFormat("[MONEY DB]: DoTransfer: Receiver not found in balances table. {0}", transaction.Receiver);
                            dbm.Release();
                            return false;
                        }

                        if (giveMoney(transactionUUID, transaction.Receiver, transaction.Amount))
                        {
                            do_trans = true;
                        }
                        else
                        {	// give money to receiver failed. Refund Processing
                            m_log.ErrorFormat("[MONEY DB]: Give money to receiver {0} failed", transaction.Receiver);
                            //Return money to sender
                            if (giveMoney(transactionUUID, transaction.Sender, transaction.Amount))
                            {
                                m_log.ErrorFormat("[MONEY DB]: give money to receiver {0} failed but return it to sender {1} successfully", transaction.Receiver, transaction.Sender);
                                updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS, "give money to receiver failed but return it to sender successfully");
                            }
                            else
                            {
                                m_log.ErrorFormat("[MONEY DB]: FATAL ERROR: Money withdrawn from sender: {0}, but failed to be given to receiver {1}",
                                                        transaction.Sender, transaction.Receiver);
                                updateTransactionStatus(transactionUUID, (int)Status.ERROR_STATUS, "give money to receiver failed, and return it to sender unsuccessfully!!!");
                            }
                        }
                    }
                    else
                    {	// withdraw money failed
                        m_log.ErrorFormat("[MONEY DB]: Withdraw money from sender {0} failed", transaction.Sender);
                    }
                }
                else
                {	// not enough balance to finish the transaction
                    m_log.ErrorFormat("[MONEY DB]: Not enough balance for user: {0} to apply the transaction.", transaction.Sender);
                }
            }
            else
            {	// Can not fetch the transaction or it has expired
                m_log.ErrorFormat("[MONEY DB]: The transaction:{0} has expired", transactionUUID.ToString());
            }

            //
            if (do_trans)
            {
                setTotalSale(transaction);
            }

            dbm.Release();

            return do_trans;
        }

        // by Fumi.Iseki
        /// <summary>Does the add money.</summary>
        /// <param name="transactionUUID">The transaction UUID.</param>
        public bool DoAddMoney(UUID transactionUUID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData transaction = new TransactionData();
            transaction = FetchTransaction(transactionUUID);

            if (transaction != null && transaction.Status == (int)Status.PENDING_STATUS)
            {
                //If receiver not found, add it to DB.
                if (getBalance(transaction.Receiver) == -1)
                {
                    m_log.ErrorFormat("[MONEY DB]: DoAddMoney: Receiver not found in balances table. {0}", transaction.Receiver);
                    dbm.Release();
                    return false;
                }
                //
                if (giveMoney(transactionUUID, transaction.Receiver, transaction.Amount))
                {
                    setTotalSale(transaction);
                    dbm.Release();
                    return true;
                }
                else
                {	// give money to receiver failed.
                    m_log.ErrorFormat("[MONEY DB]: Add money to receiver {0} failed", transaction.Receiver);
                    updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS, "add money to receiver failed");
                }
            }
            else
            {	// Can not fetch the transaction or it has expired
                m_log.ErrorFormat("[MONEY DB]: The transaction:{0} has expired", transactionUUID.ToString());
            }

            dbm.Release();

            return false;
        }

        /// <summary>Tries the add user information.</summary>
        /// <param name="user">The user.</param>
        public bool TryAddUserInfo(UserInfo user)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            UserInfo userInfo = null;

            try
            {
                userInfo = dbm.Manager.fetchUserInfo(user.UserID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                userInfo = dbm.Manager.fetchUserInfo(user.UserID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                dbm.Release();
                return false;
            }

            try
            {
                if (userInfo != null)
                {
                    //m_log.InfoFormat("[MONEY DB]: Found user \"{0}\", now update information", user.Avatar);
                    if (dbm.Manager.updateUserInfo(user)) return true;
                }
                else if (dbm.Manager.addUserInfo(user))
                {
                    //m_log.InfoFormat("[MONEY DB]: Unable to find user \"{0}\", add it to DB successfully", user.Avatar);
                    return true;
                }
                m_log.InfoFormat("[MONEY DB]: WARNNING: TryAddUserInfo: Unable to TryAddUserInfo.");
                return false;
            }
            catch (Exception e)
            {
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Fetches the user information.</summary>
        /// <param name="userID">The user identifier.</param>
        public UserInfo FetchUserInfo(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            UserInfo userInfo = null;

            try
            {
                userInfo = dbm.Manager.fetchUserInfo(userID);
                return userInfo;
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                userInfo = dbm.Manager.fetchUserInfo(userID);
                return userInfo;
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Gets the transaction number.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        public int getTransactionNum(string userID, int startTime, int endTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.getTransactionNum(userID, startTime, endTime);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.getTransactionNum(userID, startTime, endTime);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return -1;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool UserExists(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.UserExists(userID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.UserExists(userID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool UpdateUserInfo(string userID, UserInfo updatedInfo)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.UpdateUserInfo(userID, updatedInfo);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.UpdateUserInfo(userID, updatedInfo);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool DeleteUser(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.DeleteUser(userID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.DeleteUser(userID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public void LogTransactionError(UUID transactionID, string errorMessage)
        {
            m_log.ErrorFormat("[MONEY DB]: Transaction {0} failed with error: {1}", transactionID, errorMessage);
        }

        public IEnumerable<TransactionData> GetTransactionHistory(string userID, int startTime, int endTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.GetTransactionHistory(userID, startTime, endTime);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.GetTransactionHistory(userID, startTime, endTime);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return new List<TransactionData>(); // Return an empty list instead of throwing an exception
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool PerformMoneyTransfer(string senderID, string receiverID, int amount)
        {
            m_log.InfoFormat("[MONEY TRANSFER]: Transferring {0} from {1} to {2}.", amount, senderID, receiverID);

            // Beispielhafte Implementierung: F�hre den Geldtransfer durch
            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "UPDATE balances SET balance = balance - ?amount WHERE user = ?senderID; " +
                             "UPDATE balances SET balance = balance + ?amount WHERE user = ?receiverID";

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?senderID", senderID);
                    cmd.Parameters.AddWithValue("?receiverID", receiverID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(UUID.Random(), senderID, -amount);
                        LogTransaction(UUID.Random(), receiverID, amount);
                    }

                    dbm.Release();

                    return result;
                }
            }
            catch (Exception)
            {
                //m_log.ErrorFormat("[MONEY TRANSFER]: Error transferring money: {0}", ex.Message);
                return false;
            }
        }

        public void InitializeUserCurrency(string agentId)
        {
            m_log.InfoFormat("[INITIALIZE USER CURRENCY]: Initializing currency for new user: {0}", agentId);
            int realMoney = 1000; // Beispielwert oder aus der MoneyServer.ini geladen
            int gameMoney = 10000; // Beispielwert oder aus der MoneyServer.ini geladen

            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "INSERT INTO balances (user, balance) VALUES (?agentId, ?realMoney), (?agentId, ?gameMoney)";
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);
                    cmd.Parameters.AddWithValue("?realMoney", realMoney);
                    cmd.Parameters.AddWithValue("?gameMoney", gameMoney);

                    cmd.ExecuteNonQuery();
                }
                m_log.InfoFormat("[INITIALIZE USER CURRENCY]: User {0} initialized with {1}� and {2}L$.", agentId, realMoney, gameMoney);
                dbm.Release();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[INITIALIZE USER CURRENCY]: Error initializing user currency: {0}", ex.Message);
            }

        }

        public Hashtable ApplyFallbackCredit(string agentId)
        {
            m_log.WarnFormat("[FALLBACK CREDIT]: Applying fallback credit for user {0}", agentId);

            // Beispielhafte Implementierung: Fallback-Gutschrift in die Datenbank eintragen
            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "UPDATE balances SET balance = balance + 100 WHERE user = ?agentId";
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);

                    cmd.ExecuteNonQuery();
                }
                dbm.Release();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[FALLBACK CREDIT]: Error applying fallback credit: {0}", ex.Message);
            }

            return new Hashtable
            {
                { "success", true },
                { "creditedAmount", 100 },
                { "message", "Fallback credit applied due to transaction failure." }
            };

        }

    }
}
