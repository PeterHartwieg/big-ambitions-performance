# City Performance 0.5.0

- Experimental first Workshop release. All behavior features and experiments now default on; performance summaries, trace recording, automatic trace export and extra diagnostics default off. Existing saved choices are preserved.
- If problems occur, turn on Performance summaries (logging), then disable features one by one and repeat the same route or action to find the best setup for your city.
- Audited-build and conflict guards remain active. Bakery, Hyperreal and HART Free Camera features require their supported dependencies.
- Features fail independently after incompatible game updates. No measured performance gain is promised.
- Background autosaves validate against native serialization before using the worker. Keep a manual save. Restart after updating.
