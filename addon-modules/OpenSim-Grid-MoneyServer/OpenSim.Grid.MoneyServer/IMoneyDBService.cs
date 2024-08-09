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
 */


#pragma warning disable IDE1006

using OpenMetaverse;

using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;


namespace OpenSim.Grid.MoneyServer
{
    /// <summary>
    /// IMoney DB Service
    /// </summary>
    public interface IMoneyDBService
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

        /// <summary>Adds the user.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="balance">The balance.</param>
        /// <param name="status">The status.</param>
        /// <param name="type">The type.</param>
        bool addUser(string userID, int balance, int status, int type);

        /// <summary>Updates the transaction status.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="status">The status.</param>
        /// <param name="description">The description.</param>
        bool updateTransactionStatus(UUID transactionID, int status, string description);

        /// <summary>Sets the trans expired.</summary>
        /// <param name="deadTime">The dead time.</param>
        bool SetTransExpired(int deadTime);

        /// <summary>Validates the transfer.</summary>
        /// <param name="secureCode">The secure code.</param>
        /// <param name="transactionID">The transaction identifier.</param>
        bool ValidateTransfer(string secureCode, UUID transactionID);

        /// <summary>Fetches the transaction.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        TransactionData FetchTransaction(UUID transactionID);

        /// <summary>Fetches the transaction.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="lastIndex">The last index.</param>
        TransactionData FetchTransaction(string userID, int startTime, int endTime, int lastIndex);

        /// <summary>Gets the transaction number.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        int getTransactionNum(string userID, int startTime, int endTime);

        /// <summary>Does the transfer.</summary>
        /// <param name="transactionUUID">The transaction UUID.</param>
        bool DoTransfer(UUID transactionUUID);

        /// <summary>Does the add money.</summary>
        /// <param name="transactionUUID">The transaction UUID.</param>
        bool DoAddMoney(UUID transactionUUID);      // Added by Fumi.Iseki

        /// <summary>Tries the add user information.</summary>
        /// <param name="user">The user.</param>
        bool TryAddUserInfo(UserInfo user);

        /// <summary>Fetches the user information.</summary>
        /// <param name="userID">The user identifier.</param>
        UserInfo FetchUserInfo(string userID);

    }
}
