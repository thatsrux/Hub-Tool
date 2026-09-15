Hub Tool 0.4.0 aggiunge il controllo nativo delle luci monitor DX Light/QuikLight.

- Dispositivo dedicato per il controller USB `1A86:FE07`, verificato con 54 LED e firmware 1.9.4.
- Accensione, luminosità, colore RGB/HEX e preset direttamente da Hub.
- Sincronizzazione a zone con i bordi dello schermo, fluidità da 5 a 30 fps, intensità e morbidezza regolabili.
- Importazione delle preferenze di DX Light al primo avvio e supporto completo in profili, shortcut, ripristino e overlay.
- Passaggio automatico a Hub quando DX Light viene chiuso: il controller viene riaperto e lo stato salvato viene riapplicato.
- `HubTool.exe` autonomo resta nella cartella principale e non richiede .NET installato.

Build, 33 verifiche con lettura hardware e una scrittura reale invariata sul controller completate. L'EXE non è firmato Authenticode; i checksum SHA-256 sono inclusi.
