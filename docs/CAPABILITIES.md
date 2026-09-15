# Copertura effettiva — 0.5.2

| Categoria | Rilevamento | Controlli diretti | Limiti |
|---|---|---|---|
| Plug and Play | Periferiche d'interfaccia fisiche; esclusi CPU, bus, host controller, root hub, nodi HID generici e dispositivi software | Catalogo, icone, categorie e pannello specifico | I nodi interni e virtuali non fanno parte dell'inventario principale |
| Cuffie / speaker | Endpoint Core Audio attivi + topologia hardware | Volume, mute, dB, singoli canali; eco microfono/sidetone con volume e interruttore quando esposto | EQ, surround e altri DSP proprietari non integrati |
| Microfoni | Endpoint Core Audio attivi | Volume, mute, dB, singoli canali | Il livello in dB non equivale necessariamente a gain analogico o boost hardware |
| Mouse | PnP + preferenze Windows | Velocità, doppio clic, rotella, scambio pulsanti | Preferenze globali; DPI, polling rate, pulsanti firmware e RGB non integrati |
| Tastiere | PnP + preferenze Windows | Velocità e ritardo ripetizione | Preferenze globali; rimappatura per dispositivo, macro e RGB non integrati |
| Monitor | PnP + monitor fisici Windows | Luminosità e contrasto se DDC/CI risponde | Alcuni pannelli interni, dock e driver non espongono questi controlli; WMI brightness non ancora integrata |
| Webcam | PnP + DirectShow/AVICap | Anteprima live; esposizione, fuoco, zoom, pan/tilt, luminosità, contrasto e altri controlli standard; reset, Enhance locale e profili immagine per camera | Estensioni e DSP proprietari non integrati; valori/auto disponibili solo quando supportati; una camera già occupata da un’altra app può rifiutare l’anteprima |
| Luci monitor | USB HID `1A86:FE07` | Accensione, RGB/HEX, preset, luminosità; sync dei bordi dello schermo su 54 zone con fps, saturazione e smoothing | Verificato sul controller Robobloq firmware 1.9.4; cattura dello schermo primario; altri controller RGB richiedono provider dedicati |
| Controller | PnP | Inventario e preferenze overlay | Test input, vibrazione, deadzone e rimappature non ancora integrati |
| Stampanti | PnP | Apertura pannello stampanti | Code e preferenze specifiche non integrate |
| Rete / Bluetooth | Pannelli Windows | Apertura impostazioni | Adattatori interni non elencati come periferiche |
| Hub USB esterni / memoria USB | PnP filtrato | Inventario e pannello dispositivi | Controller host e root hub esclusi; espulsione e gestione alimentazione non integrate |

## Persistenza

Gli ID vengono confrontati senza distinzione maiuscole/minuscole. La disconnessione conserva il record. Un dispositivo dimenticato resta escluso finché l’utente non ripristina l’elenco; i riferimenti in profili e shortcut vengono eliminati. Il ripristino è esplicito; i profili includono audio e tutti i controlli implementati. I profili immagine sono legati al record persistente della singola camera. Un errore di un driver viene segnalato; non viene presentato come successo completo.

## Estensione

I descrittori `DeviceControl` definiscono nome, limiti e unità. `DeviceService` instrada le scritture al provider nativo. Le nuove integrazioni devono interrogare le capacità, validare i valori e mantenere gli ID stabili; non devono inviare comandi HID proprietari indovinati. Al momento i provider si estendono nel codice, non attraverso DLL esterne caricate automaticamente.

## Fonti API

- [Core Audio](https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-interfaces)
- [SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
- [Monitor configuration](https://learn.microsoft.com/en-us/windows/win32/monitor/monitor-configuration-functions)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [HID clients](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/creating-hid-clients)
- [Bit-block transfer](https://learn.microsoft.com/en-us/windows/win32/gdi/bit-block-transfer)
