/*
 * Copyright (c) Contributors, http://opensimulator.org/ See CONTRIBUTORS.TXT for a full list of copyright holders.
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
    Die Datei definiert das C#-Interface IMoneyManager.
    Sie legt die Methoden fest, die eine Klasse zur Verwaltung von Geldtransaktionen, Guthaben und Benutzerinformationen implementieren muss.
    Typische Aufgaben des Interfaces:
        Abfragen und Ändern von Benutzer-Guthaben (getBalance, withdrawMoney, giveMoney)
        Hinzufügen und Abfragen von Transaktionen (addTransaction, FetchTransaction)
        Verwaltung von Benutzern (addUser, addUserInfo, fetchUserInfo, updateUserInfo)
        Validierung und Ablauf von Transaktionen (ValidateTransfer, SetTransExpired)

NullPointer-Checks & Fehlerquellen

    Interface selbst:
    Das Interface enthält keine Implementierung. Es ist lediglich eine Methodendefinition. Das bedeutet:
        Es gibt keine Logik, also auch keine Möglichkeit für NullPointer-Fehler innerhalb dieser Datei.
        Fehlerquellen oder NullPointer-Probleme hängen ausschließlich von den konkreten Implementierungen dieser Methoden ab.

    Parameter:
        Die Methoden nehmen meist Strings, UUIDs, Ints und komplexe Typen wie TransactionData oder UserInfo als Parameter.
        In einer späteren Implementierung sollte geprüft werden, ob übergebene Objekte (z.B. user, transaction) null sind, bevor darauf zugegriffen wird.
        Rückgabewerte wie TransactionData oder UserInfo könnten null sein, wenn nichts gefunden wird – auch das ist Sache der Implementierung.

    Rückgabewerte:
        Methoden, die Objekte zurückgeben (FetchTransaction, fetchUserInfo), haben keine Angaben, ob sie null zurückgeben dürfen. 
        Das sollte dokumentiert oder in der Implementierung behandelt werden.

Zusammenfassung
    NullPointer-Gefahr:
    Im Interface selbst nicht möglich, da keine Ausführung erfolgt.
    Fehlerquellen:
    Keine im Interface; alle Fehlerquellen und NullPointer-Risiken entstehen erst in den Klassen, die dieses Interface implementieren.
    Funktion:
    Definition aller nötigen Methoden zur Verwaltung von Geldtransaktionen und Benutzerinformationen im OpenSim-Kontext.

Fazit:
Die Datei IMoneyManager.cs ist ein reines Methoden-Interface und enthält keine Logik, die zu NullPointer-Fehlern führen kann. 
Typische Fehlerquellen und NullPointer sollten in den konkreten Implementierungen der Methoden sorgfältig behandelt werden (z.B. Nullprüfungen auf Parameter, Rückgabewerte).
 */

#pragma warning disable IDE1006

using OpenMetaverse;

namespace OpenSim.Data.MySQL.MySQLMoneyDataWrapper
{
    public interface IMoneyManager
    {
        /// <summary>Gets the balance.</summary>
        /// <param name="userID">The user identifier.</param>
        int getBalance(string userID);

        /// <summary>Withdraws the money.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="senderID">The sender identifier.</param>
        /// <param name="amount">The amount.</param>
        bool withdrawMoney(UUID transactionID, string senderID, int amount);

        /// <summary>Gives the money.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="receiverID">The receiver identifier.</param>
        /// <param name="amount">The amount.</param>
        bool giveMoney(UUID transactionID, string receiverID, int amount);

        /// <summary>Adds the transaction.</summary>
        /// <param name="transaction">The transaction.</param>
        bool addTransaction(TransactionData transaction);

        /// <summary>Updates the transaction status.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="status">The status.</param>
        /// <param name="description">The description.</param>
        bool updateTransactionStatus(UUID transactionID, int status, string description);

        /// <summary>Fetches the transaction.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        TransactionData FetchTransaction(UUID transactionID);

        /// <summary>Fetches the transaction.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="index">The index.</param>
        /// <param name="retNum">The ret number.</param>
        TransactionData[] FetchTransaction(string userID, int startTime, int endTime, uint index, uint retNum);

        /// <summary>Gets the transaction number.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        int getTransactionNum(string userID, int startTime, int endTime);

        /// <summary>Adds the user.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="balance">The balance.</param>
        /// <param name="status">The status.</param>
        /// <param name="type">The type.</param>
        bool addUser(string userID, int balance, int status, int type);

        /// <summary>Sets the trans expired.</summary>
        /// <param name="deadTime">The dead time.</param>
        bool SetTransExpired(int deadTime);

        /// <summary>Validates the transfer.</summary>
        /// <param name="secureCode">The secure code.</param>
        /// <param name="transactionID">The transaction identifier.</param>
        bool ValidateTransfer(string secureCode, UUID transactionID);

        /// <summary>Adds the user information.</summary>
        /// <param name="user">The user.</param>
        bool addUserInfo(UserInfo user);

        /// <summary>Fetches the user information.</summary>
        /// <param name="userID">The user identifier.</param>
        UserInfo fetchUserInfo(string userID);

        /// <summary>Updates the user information.</summary>
        /// <param name="user">The user.</param>
        bool updateUserInfo(UserInfo user);
    }
}
