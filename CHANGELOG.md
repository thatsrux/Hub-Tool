# Changelog

## 0.2.0 — 2026-09-13

- Aggiunti controlli Windows diretti di mouse e tastiera, luminosità/contrasto DDC/CI, canali audio e dB.
- Estesi i profili ai controlli implementati e le shortcut a singoli dispositivi e programmi con argomenti.
- Corretto il merge dei dispositivi assenti e lo stato di connessione al riavvio; validati JSON e valori non finiti.
- Spostata la discovery lenta fuori dalla UI, aggiunte notifiche volume e lista virtualizzata.
- Ridisegnate le quattro pagine, aggiunti filtri, icona, posizione overlay e protezione dalla doppia istanza.
- Ridotte le dipendenze al solo NAudio.Wasapi/Core; aggiunti test, rendering diagnostico, misure preliminari e workflow GitHub.
- Preparati EXE autonomo, ZIP portable/compatto, licenze e checksum. La release resta sperimentale; copertura universale e consumi minimi non sono dimostrati.

## 0.1.0 — 2026-09-13 (in sviluppo)

- Creato progetto WPF e inventario SetupAPI persistente.
- Aggiunti endpoint Core Audio, profili, shortcut, tray e overlay.
- Documentati i limiti delle integrazioni disponibili e della verifica.
