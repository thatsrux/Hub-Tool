Hub Tool 0.6.0 alleggerisce l’avvio e semplifica la gestione dei dispositivi.

- L’avvio con Windows è facoltativo, nascosto nell’area notifiche e ritarda la scansione di 12 secondi.
- Eco microfono Fifine usa il fallback hardware affidabile e ripristina il volume precedente alla riattivazione.
- La sezione Profili è stata rimossa; restano i profili immagine specifici della videocamera.
- “Rimuovi definitivamente” blocca il ritorno del dispositivo ed elimina le shortcut collegate, mentre “Dimentica” resta reversibile.
- L’elenco delle azioni Shortcut esclude i dispositivi privi di controlli.
- `HubTool.exe` autonomo resta nella cartella principale e non richiede .NET installato.

Build, 33 controlli automatici e 42 controlli hardware completati. L'EXE non è firmato Authenticode; i checksum SHA-256 sono inclusi.
