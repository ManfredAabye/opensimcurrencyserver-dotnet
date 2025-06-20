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
 */

using MySql.Data.MySqlClient;

using System;
using System.Data;
using System.Threading;

namespace OpenSim.Data.MySQL.MySQLMoneyDataWrapper
{
    public class MySQLSuperManager : IDisposable
    {
        private readonly Mutex _mutex = new Mutex(false);
        private readonly MySQLMoneyManager _manager;
        private MySqlConnection _connection;
        private bool _disposed = false;
        private bool _locked = false;

        public MySQLSuperManager(string connectionString)
        {
            _manager = new MySQLMoneyManager(connectionString);
            _connection = _manager.Connection as MySqlConnection;
        }

        public bool IsConnected
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(MySQLSuperManager));
                if (_connection == null) return false;

                return _connection.State == ConnectionState.Open;
            }
        }

        public bool Locked => _locked;

        public MySQLMoneyManager Manager => _manager;

        public void GetLock()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MySQLSuperManager));

            _mutex.WaitOne();
            _locked = true;

            if (_connection != null && _connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }
        }

        public bool TryGetLock(int timeoutMs = 5000)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MySQLSuperManager));
            if (_connection == null) return false;

            try
            {
                if (_mutex.WaitOne(timeoutMs))
                {
                    _locked = true;
                    if (_connection.State != ConnectionState.Open)
                    {
                        _connection.Open();
                    }
                    return true;
                }
                return false;
            }
            catch (AbandonedMutexException)
            {
                _locked = true;
                return true;
            }
        }

        public void Release()
        {
            ReleaseLock(); // Alias für ReleaseLock() zur Abwärtskompatibilität
        }

        public void ReleaseLock()
        {
            if (_disposed || !_locked) return;

            try
            {
                if (_connection != null && _connection.State == ConnectionState.Open)
                {
                    _connection.Close();
                }
                _mutex.ReleaseMutex();
                _locked = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySQLSuperManager] Fehler beim Freigeben: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    if (_locked)
                    {
                        try { _mutex.ReleaseMutex(); } catch { }
                        _locked = false;
                    }
                    _mutex?.Dispose();

                    _connection?.Close();
                    _connection?.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MySQLSuperManager] Fehler beim Aufräumen: {ex.Message}");
                }
            }
            _disposed = true;
        }

        ~MySQLSuperManager()
        {
            Dispose(false);
        }
    }
}
