Hub Tool 0.3.1: repository più semplice da usare e build autonoma immediatamente accessibile.

- `HubTool.exe` è disponibile direttamente nella cartella principale ed è autonomo: non richiede un'installazione separata di .NET.
- Codice dell'applicazione spostato in `src/` e verifiche in `tests/`.
- Workflow CI, packaging e istruzioni aggiornati ai nuovi percorsi.

Build e controlli automatici passati. L'EXE non è firmato Authenticode; i checksum SHA-256 sono inclusi. Consulta README per i limiti dei driver.
