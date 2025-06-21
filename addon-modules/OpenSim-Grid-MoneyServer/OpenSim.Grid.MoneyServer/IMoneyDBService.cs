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

IMoneyDBService ist ein Interface für Datenbankoperationen rund um ein Währungssystem, typischerweise für virtuelle Ökonomien. Es definiert Methoden für:

    Nutzerverwaltung (z.B. addUser, DeleteUser, UserExists, UpdateUserInfo)
    Kontostand- und Geldtransaktionen (getBalance, withdrawMoney, giveMoney, BuyMoney, BuyCurrency, PerformMoneyTransfer)
    Transaktionsmanagement (addTransaction, updateTransactionStatus, FetchTransaction, GetTransactionHistory, SetTransExpired)
    Fehlerprotokollierung (LogTransactionError)
    Authentifizierungen und Validierungen (ValidateTransfer)
    Verbindungsmanagement (GetLockedConnection)

Die Methoden sind so gestaltet, dass sie in einer konkreten Implementierung mit einer Datenbank (z.B. MySQL) interagieren.
 */


using OpenMetaverse;

using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;

using System;
using System.Collections;
using System.Collections.Generic;

namespace OpenSim.Grid.MoneyServer
{
    /// <summary>
    /// IMoney DB Service
    /// </summary>
    public interface IMoneyDBService
    {
        int CheckMaximumMoney(string userID, int m_CurrencyMaximum);
        Hashtable ApplyFallbackCredit(string agentId);

        void InitializeUserCurrency(string agentId);

        bool PerformMoneyTransfer(string senderID, string receiverID, int amount);

        /// <summary>Ruft den Kontostand ab.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        int getBalance(string userID);

        /// <summary>Zieht Geld ab.</summary>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        /// <param name="senderID">Die Absender-ID.</param>
        /// <param name="amount">Der Betrag.</param>
        bool withdrawMoney(UUID transactionID, string senderID, int amount);

        /// <summary>Gibt Geld.</summary>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        /// <param name="receiverID">Die Empfänger-ID.</param>
        /// <param name="amount">Der Betrag.</param>
        bool giveMoney(UUID transactionID, string receiverID, int amount);

        /// <summary>Kauft Geld für einen Benutzer.</summary>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        /// <param name="userID">Die Benutzer-ID.</param>
        /// <param name="amount">Der zu kaufende Betrag.</param>
        /// <returns>True, wenn der Kauf erfolgreich war, andernfalls false.</returns>
        bool BuyMoney(UUID transactionID, string userID, int amount);

        bool BuyCurrency(string userID, int amount);

        /// <summary>Fügt die Transaktion hinzu.</summary>
        /// <param name="transaction">Die Transaktion.</param>
        bool addTransaction(TransactionData transaction);

        /// <summary>Fügt den Benutzer hinzu.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        /// <param name="balance">Der Kontostand.</param>
        /// <param name="status">Der Status.</param>
        /// <param name="type">Der Typ.</param>
        bool addUser(string userID, int balance, int status, int type);

        /// <summary>Aktualisiert den Transaktionsstatus.</summary>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        /// <param name="status">Der Status.</param>
        /// <param name="description">Die Beschreibung.</param>
        bool updateTransactionStatus(UUID transactionID, int status, string description);

        /// <summary>Setzt die Transaktion als abgelaufen.</summary>
        /// <param name="deadTime">Die Ablaufzeit.</param>
        bool SetTransExpired(int deadTime);

        /// <summary>Validiert die Überweisung.</summary>
        /// <param name="secureCode">Der Sicherheitscode.</param>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        bool ValidateTransfer(string secureCode, UUID transactionID);

        /// <summary>Ruft die Transaktion ab.</summary>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        TransactionData FetchTransaction(UUID transactionID);

        /// <summary>Ruft die Transaktion ab.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        /// <param name="startTime">Die Startzeit.</param>
        /// <param name="endTime">Die Endzeit.</param>
        /// <param name="lastIndex">Der letzte Index.</param>
        TransactionData FetchTransaction(string userID, int startTime, int endTime, int lastIndex);

        /// <summary>Ruft die Anzahl der Transaktionen ab.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        /// <param name="startTime">Die Startzeit.</param>
        /// <param name="endTime">Die Endzeit.</param>
        int getTransactionNum(string userID, int startTime, int endTime);

        /// <summary>Führt die Überweisung durch.</summary>
        /// <param name="transactionUUID">Die Transaktions-UUID.</param>
        bool DoTransfer(UUID transactionUUID);

        /// <summary>Führt die Geldaufstockung durch.</summary>
        /// <param name="transactionUUID">Die Transaktions-UUID.</param>
        bool DoAddMoney(UUID transactionUUID);  // Hinzugefügt von Fumi.Iseki

        /// <summary>Versucht, Benutzerinformationen hinzuzufügen.</summary>
        /// <param name="user">Der Benutzer.</param>
        bool TryAddUserInfo(UserInfo user);

        /// <summary>Ruft die Benutzerinformationen ab.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        UserInfo FetchUserInfo(string userID);

        /// <summary>Überprüft, ob ein Benutzer existiert.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        bool UserExists(string userID);

        /// <summary>Aktualisiert die Benutzerinformationen.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        /// <param name="updatedInfo">Die aktualisierten Benutzerinformationen.</param>
        bool UpdateUserInfo(string userID, UserInfo updatedInfo);

        /// <summary>Löscht einen Benutzer aus der Datenbank.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        bool DeleteUser(string userID);

        /// <summary>Protokolliert transaktionsbezogene Fehler oder Änderungen zur Fehlerbehebung.</summary>
        /// <param name="transactionID">Die Transaktions-ID.</param>
        /// <param name="errorMessage">Die Fehlermeldung oder Änderungshinweis.</param>
        void LogTransactionError(UUID transactionID, string errorMessage);

        /// <summary>Ruft eine Liste der Transaktionen für einen Benutzer innerhalb eines bestimmten Zeitrahmens ab.</summary>
        /// <param name="userID">Die Benutzer-ID.</param>
        /// <param name="startTime">Die Startzeit.</param>
        /// <param name="endTime">Die Endzeit.</param>
        IEnumerable<TransactionData> GetTransactionHistory(string userID, int startTime, int endTime);
        MySQLSuperManager GetLockedConnection();
    }
}
