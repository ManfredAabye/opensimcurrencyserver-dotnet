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
    Die Klasse MySQLSuperManager dient als Wrapper/Manager für einen Thread-Sicherheitsmechanismus (Mutex) und kapselt eine Instanz von MySQLMoneyManager.
    Sie enthält:
        Ein Mutex-Objekt für Thread-Synchronisation.
        Ein Flag Locked zur Statusanzeige.
        Eine öffentliche Instanz von MySQLMoneyManager namens Manager.
        Die Methode GetLock() blockiert, bis der Mutex verfügbar ist.
        Die Methode Release() gibt den Mutex frei und behandelt freigabe-bezogene Fehler.
        Ein String Running (wird hier aber nicht verwendet).

Null Pointer Checks
    Manager: Im Konstruktor wird garantiert, dass Manager immer initialisiert ist (niemals null nach Konstruktion).
    m_lock: Wird direkt beim Feld-Deklaration erstellt, kann also nie null sein.
    Locked: Ist ein bool, daher nie null.
    Running: Ist public, aber im Code nicht direkt verwendet. Wenn er von außen auf null gesetzt wird, kann das keinen Fehler verursachen, da er nicht verwendet wird.

Fehlerquellen
    Mutex-Handling:
        In Release() wird das Freigeben des Mutex von einem Try-Catch-Block umschlossen. Sollte ein Fehler beim Freigeben auftreten (z.B. Freigabe von einem Thread, der den Mutex nicht besitzt), wird die Exception geloggt und erneut geworfen.
        In GetLock() wird WaitOne() direkt aufgerufen, was blockiert, bis der Lock verfügbar ist. Es gibt hier keine Exception-Absicherung, aber üblicherweise ist dies sicher, solange das Objekt korrekt verwendet wird.

    Thread-Safety:
        Die Klasse ist thread-safe bezüglich des Mutex, nicht aber bezüglich der Instanzvariablen (Running ist public ohne Schutz, aber nicht verwendet).
        Es wird kein expliziter Null-Check auf Manager benötigt, da sie im Konstruktor immer initialisiert wird.

    Unbenutzte Variable:
        Das Feld Running ist nicht implementiert/genutzt. Das ist kein Fehler, aber ein Hinweis auf evtl. toten Code.

Zusammenfassung
    Null Pointer: Keine Gefahr für NullPointer-Exceptions im aktuellen Code.
    Fehlerquellen: Einzige potenzielle Fehlerquelle ist das Freigeben des Mutex in Release(), aber dies wird per Exception abgefangen und geloggt.
    Funktion: Thread-Safe-Manager/Wrapper für den Zugriff auf einen MySQLMoneyManager.

Fazit:
Die Klasse ist solide und sicher gegen NullPointer-Fehler. Fehler beim Mutex-Handling werden korrekt behandelt. Es gibt keine offensichtlichen Schwachstellen.
Lediglich das Feld Running ist ungenutzt und könnte entfernt oder implementiert werden.
 */

using MySqlX.XDevAPI;

using OpenMetaverse.ImportExport.Collada14;

using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;

using System;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Threading;

namespace OpenSim.Data.MySQL.MySQLMoneyDataWrapper
{
    public class MySQLSuperManager(string connectionString)
    {
        public bool Locked { get; private set; }
        private readonly Mutex m_lock = new(false);
        public MySQLMoneyManager Manager { get; } = new MySQLMoneyManager(connectionString);
        public string Running;

        /// <summary>Erwirbt die exklusive Sperre für kritische Operationen.</summary>
        public void GetLock()
        {
            m_lock.WaitOne();
            Locked = true;
        }

        /// <summary>Gibt die Sperre wieder frei.</summary>
        public void Release()
        {
            try
            {
                m_lock.ReleaseMutex();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ein Fehler beim Freigeben des Mutex ist aufgetreten: {ex.Message}");
                throw;
            }
            finally
            {
                Locked = false;
            }
        }
    }
}
