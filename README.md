# Maschinenüberwachung mit KI-Anomalieerkennung

![CI](https://github.com/talaalhussainn8n/MachineData-AnomalyDetection/actions/workflows/ci.yml/badge.svg)

Überwacht Sensordaten von Produktionsmaschinen in Echtzeit und erkennt Auffälligkeiten, bevor es zum Ausfall kommt.
Messwerte kommen per **MQTT** (oder HTTP), werden mit **ML.NET** und statistischen Verfahren bewertet, in **MS SQL Server** gespeichert und in einem Live-Dashboard angezeigt.

## Funktionen

- **Datenaufnahme über MQTT**: Topic-Schema `factory/{maschine}/{sensor}`, automatischer Wiederaufbau der Verbindung.
- **Zwei Erkennungsverfahren**:
  - **ML.NET SR-CNN** (Spectral Residual + CNN): erkennt Auffälligkeiten im Verlauf ohne gelabelte Trainingsdaten.
  - **Gleitender z-Wert**: reagiert sofort auf Ausreißer; auffällige Werte verfälschen die Referenz nicht.
- **Alarme** mit Handlungshinweis (z. B. Lager prüfen bei Vibration) und Unterdrückung von Wiederholungen.
- **Maschinensimulator** mit Rauschen, Lastzyklen, plötzlichen Störungen (Lagerschaden) und schleichender Überhitzung.
- **Live-Dashboard** mit Verlauf, markierten Anomalien und Alarmliste.

## Architektur

```
 Simulator / Maschine                     MachineMonitor.Api
 ---------------------      MQTT       ---------------------------------------------
 MachineSimulator  ---->  Mosquitto --> MqttIngestionService --+
                                                                 |
 Geräte ohne MQTT  ----  HTTP POST /api/readings  ------------->+--> Channel (Puffer)
                                                                         |
                                                                 ProcessingService
                                                                   |- z-Wert
                                                                   |- ML.NET SR-CNN
                                                                   |- Alarm-Logik
                                                                         |
                                                    EF Core --> MS SQL Server / SQLite
                                                                         |
                                                    REST API --> Live-Dashboard
```

| Projekt | Inhalt |
| --- | --- |
| `src/MachineMonitor.Core` | Modelle, Simulator, Erkennungsverfahren, MQTT-Anbindung |
| `src/MachineMonitor.Api` | ASP.NET Core: Hintergrunddienste, EF Core, REST API, Dashboard |
| `src/MachineMonitor.Simulator` | Konsolenprogramm, das Maschinen simuliert und per MQTT sendet |
| `tests/MachineMonitor.Tests` | Unit-Tests und End-to-End-Test der API (xUnit) |

### Designentscheidungen

- **Puffer zwischen Empfang und Auswertung** (`System.Threading.Channels`): Lastspitzen blockieren den MQTT-Empfang nicht.
- **Gesammeltes Schreiben** in die Datenbank statt Einzelzugriffen pro Messwert.
- **Datenbank austauschbar**: SQLite für die Entwicklung, MS SQL Server im Docker-Betrieb.
- **Zwei Verfahren kombiniert**: der z-Wert ist schnell und erklärbar, SR-CNN erkennt auch Muster im Verlauf.

## Starten

### Schnellstart ohne Broker (eingebaute Simulation, SQLite)

```bash
dotnet run --project src/MachineMonitor.Api
```

Dashboard unter `http://localhost:5000` öffnen. Mit "Überhitzung simulieren" lässt sich ein schleichender Fehler auslösen.

### Kompletter Aufbau mit MQTT und MS SQL Server

```bash
docker compose up --build
```

Startet Mosquitto, SQL Server, die API und den Simulator. Dashboard unter `http://localhost:8080`.

## Tests

```bash
dotnet test tests/MachineMonitor.Tests/MachineMonitor.Tests.csproj
```

## API

| Methode | Pfad | Zweck |
| --- | --- | --- |
| GET | `/api/machines` | Aktueller Zustand aller Maschinen |
| GET | `/api/machines/{id}/readings?sensor=Vibration&minutes=5` | Verlauf eines Sensors |
| POST | `/api/readings` | Messwert per HTTP einliefern |
| GET | `/api/alarms` | Letzte Alarme |
| POST | `/api/alarms/{id}/acknowledge` | Alarm quittieren |
| POST | `/api/machines/{id}/simulate-overheating` | Überhitzung simulieren (nur eingebaute Simulation) |

## Mögliche Erweiterungen

- Anbindung realer Steuerungen über **OPC UA**
- Desktop-Leitstand mit **WPF**
- Benachrichtigung per E-Mail oder Teams bei kritischen Alarmen
- Vorhersage der Restlebensdauer (Predictive Maintenance) aus den gespeicherten Verläufen
