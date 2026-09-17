Hub Tool 0.6.1 ripara le shortcut e il toggle dell’eco microfono dopo un cambio di ID audio da parte di Windows.

- Gli endpoint audio equivalenti vengono riconosciuti quando Windows assegna un nuovo ID.
- Le shortcut vengono trasferite automaticamente dall’endpoint scollegato a quello attivo.
- I duplicati audio obsoleti vengono rimossi, evitando che checkbox e shortcut modifichino soltanto una copia offline.
- La disattivazione Fifine porta a zero il valore fisico del sidetone; la riattivazione ripristina il livello precedente.
- `HubTool.exe` autonomo resta nella cartella principale e non richiede .NET installato.

Build, 35 controlli automatici e 43 controlli hardware completati. L'EXE non è firmato Authenticode; i checksum SHA-256 sono inclusi.
