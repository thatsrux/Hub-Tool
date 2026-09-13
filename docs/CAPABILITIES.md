# Copertura effettiva — 0.2.0

| Categoria | Rilevamento | Controlli diretti | Limiti |
|---|---|---|---|
| Plug and Play | Tutte le classi presenti esposte da SetupAPI | Inventario, preferenze overlay, pannello categoria | Non è un pannello universale dei driver; componenti interni e virtuali sono inclusi |
| Cuffie / speaker | Endpoint Core Audio attivi | Volume, mute, dB, singoli canali | EQ, surround e DSP proprietari non integrati |
| Microfoni | Endpoint Core Audio attivi | Volume, mute, dB, singoli canali | Il livello in dB non equivale necessariamente a gain analogico o boost hardware |
| Mouse | PnP + preferenze Windows | Velocità, doppio clic, rotella, scambio pulsanti | Preferenze globali; DPI, polling rate, pulsanti firmware e RGB non integrati |
| Tastiere | PnP + preferenze Windows | Velocità e ritardo ripetizione | Preferenze globali; rimappatura per dispositivo, macro e RGB non integrati |
| Monitor | PnP + monitor fisici Windows | Luminosità e contrasto se DDC/CI risponde | Alcuni pannelli interni, dock e driver non espongono questi controlli; WMI brightness non ancora integrata |
| Webcam | PnP | Apertura pannello Windows | Esposizione, fuoco e controlli video non ancora integrati |
| Controller | PnP | Inventario e preferenze overlay | Test input, vibrazione, deadzone e rimappature non ancora integrati |
| Stampanti | PnP | Apertura pannello stampanti | Code e preferenze specifiche non integrate |
| Rete / Bluetooth | PnP | Apertura pannello categoria | Connessioni, pairing e configurazione avanzata non integrati |
| Dischi / USB / altri | PnP | Inventario e pannello dispositivi | Espulsione, gestione alimentazione e altri controlli non integrati |

## Persistenza

Gli ID vengono confrontati senza distinzione maiuscole/minuscole. La disconnessione conserva il record. Il ripristino è esplicito; i profili includono audio e tutti i controlli implementati. Un errore di un driver viene segnalato; non viene presentato come successo completo. La modifica della porta o del driver può produrre un nuovo ID: l'associazione manuale fra vecchio e nuovo ID è ancora da implementare.

## Estensione

I descrittori `DeviceControl` definiscono nome, limiti e unità. `DeviceService` instrada le scritture al provider nativo. Le nuove integrazioni devono interrogare le capacità, validare i valori e mantenere gli ID stabili; non devono inviare comandi HID proprietari indovinati. Al momento i provider si estendono nel codice, non attraverso DLL esterne caricate automaticamente.

## Fonti API

- [Core Audio](https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-interfaces)
- [SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
- [Monitor configuration](https://learn.microsoft.com/en-us/windows/win32/monitor/monitor-configuration-functions)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
