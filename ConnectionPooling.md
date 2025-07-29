# Was ist Connection Pooling?

**Connection Pooling** bedeutet, dass die Datenbankverbindungen nicht für jeden Zugriff neu erstellt und wieder entfernt werden, sondern in einem Pool ("Becken") zwischengespeichert werden.  
Wenn eine Anwendung eine Verbindung braucht, bekommt sie eine freie aus dem Pool. Ist keine frei, wird (bis zum Maximum) eine neue erstellt.
Nach der Benutzung wird die Verbindung nicht geschlossen, sondern wieder in den Pool gelegt.

Dadurch:

- **Schneller Zugriff**: Öffnen und Schließen von Verbindungen ist teuer; Pooling spart Zeit und Ressourcen.
- **Effizientere Ressourcennutzung**: Es werden nur so viele Verbindungen gehalten, wie wirklich gebraucht werden.
- **Skalierbarkeit**: Die Anwendung kann viele parallele Anfragen bedienen, ohne unnötig viele Verbindungen offen zu halten.

---

## Was hat sich im MoneyServer geändert?

### Vorher

- Beim Start wurden z.B. 20, 50 oder sogar 700 Datenbankverbindungen **fest** geöffnet und in einem Dictionary gehalten.
- Jede Anfrage griff auf einen festen Index im Dictionary zu (`GetLockedConnection`).
- Waren zu wenig Connections initialisiert, gab es Fehler ("Key not found").

### Jetzt

- **Kein Dictionary und keine Schleife mehr beim Start**: Verbindungen werden nicht mehr fest vorgehalten.
- **Connection Pooling**:  
  Der MySQL-Connector verwaltet die Verbindungen automatisch.  
  Der Connection String enthält z.B. `Pooling=true;Max Pool Size=100;`.
- **Dynamisches Öffnen und Freigeben**:  
  Bei jedem Zugriff auf die Datenbank wird eine Verbindung aus dem Pool geholt (`using (var connection = new MySqlConnection(...))`) und nach Gebrauch zurückgegeben.
- **Kein Fehler durch fehlende Indizes**:  
  Es ist egal, wie viele parallele Requests kommen – solange der Pool nicht voll ist, bekommt jeder eine Connection.

---

## Vorteile für dich

- **Keine Fehler durch Indexberechnung oder fehlende Verbindungen.**
- **Weniger Ressourcenverbrauch**: Es werden nur so viele Verbindungen gehalten, wie wirklich gebraucht.
- **Höhere Stabilität und Skalierbarkeit**: Du kannst die Poolgröße im Connection String einfach anpassen.
- **Wartungsfreundlicher Code**: Weniger komplex, keine manuelle Verwaltung von Verbindungsobjekten.

---

## Zusammengefasst

**Ich habe von einer festen, manuellen Verbindungsverwaltung auf modernes, automatisches Connection Pooling umgestellt.**  
Der MoneyServer ist jetzt effizienter, stabiler und kann bessere Performance liefern, ohne die Datenbank zu überlasten oder fehlerhafte Indizes zu produzieren.
