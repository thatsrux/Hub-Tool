# Obiettivo originale ancora aperto

La prerelease pubblicata non ridefinisce l'obiettivo come completo. Questi sono i divari rispetto alla richiesta originale:

1. **Tutti i controlli di qualsiasi dispositivo:** la matrice CAPABILITIES esplicita le categorie ancora prive di controlli diretti. Servono provider aggiuntivi, API documentate e prove hardware; non è possibile inventare protocolli proprietari. Il gain analogico non va confuso con il volume dell'endpoint Windows.
2. **Persistenza completa:** gestiti i controlli implementati. Restano associazione manuale di ID cambiati, import/export guidato, gestione dei fallimenti e verifica di scollegamenti fisici con modifiche offline.
3. **Shortcut per qualsiasi azione:** esistono shortcut per i controlli attuali e launcher con argomenti. Restano sequenze di azioni e nuove azioni per i futuri provider.
4. **Profili di tutti i dispositivi:** copertura attuale audio/input/DDC. Le future capacità dovranno rientrare nei profili e avere una strategia di applicazione parziale e ripristino verificabile.
5. **Overlay sopra qualsiasi app:** presente topmost standard, espandibile e configurabile. Servono prove interattive, posizione su DPI/multimonitor, gestione focus/ritorno all'app e comportamento durante il gioco. Fullscreen esclusivo e desktop protetto non sono garantiti dalle normali finestre topmost.
6. **Consumo minimo:** architettura a eventi e lista virtualizzata presenti, ma ~182 MiB dopo diagnostica non dimostrano l'efficienza richiesta. Occorre isolare costi di rendering/JIT, misurare idle stabilizzato, startup e tray, e valutare ulteriori interventi sul framework/UI.
7. **Interfaccia perfetta e compatibilità:** quattro pagine renderizzate e corrette. Mancano prove interattive, accessibilità, scaling, casi vuoti/errore e maggiore varietà hardware.
8. **GitHub e release:** repository, EXE/ZIP, checksum e workflow pubblicati e verificati. Restano firma Authenticode e opzioni installer più integrate, senza promettere assenza di avvisi di reputazione.
9. **Funzioni utili/gaming:** valutare mixer per applicazione, controller e test input, batteria periferiche, luminosità pannelli interni, media key, profili rapidi e integrazioni documentate RGB. Sono estensioni all'obiettivo, non sostituti dei requisiti mancanti.

Prima della prossima release: rendere più estese le prove delle scritture reali (con ripristino del valore originale), del focus overlay e dei conflitti shortcut. Le letture hardware e il rendering da soli non le dimostrano.
