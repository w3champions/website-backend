# Fixture provenance

Public repository: nothing here is secret or identifying.

- `real-reforged-customkeys.txt`: the author's own Reforged `CustomKeys.txt` (25,790 bytes, LF).
- `real-war3preferences.txt`: the author's own `War3Preferences.txt` (Classic mode), unmodified except for one hand-added `[Command Grid Hotkeys 00]` section at the top (this machine has none). The identifying values (`userbnet`, `userlocal`, `offlineavatar`, `bnetGateway`) are empty in the source and were checked.
- `launcher-generated-customkeys.txt`: output of launcher-e `hotkeyService.patchModifiedHotkeys([], hotkeys, allCommands)` over its `defaultHotkeyData` (button positions were assigned deterministically to some entries to exercise `Buttonpos` formatting).
- `jcfieldsdev-grid-en-customkeys.txt`, `jcfieldsdev-qweaz-en-customkeys.txt`: `www/hotkeys/grid-en.txt` and `www/hotkeys/qweaz-en.txt` from https://github.com/jcfieldsdev/warcraft3-hotkey-editor (author jcfieldsdev), licensed MIT. The upstream `LICENSE.md` carries the standard MIT text without a separate copyright line; the files are unmodified copies. They are large (72,149 and 284,727 bytes, mostly tooltips) and exist to prove the size cap and key list accept real shared packs.

The upstream MIT permission notice applies to the two jcfieldsdev files:
"Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction ... The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software." See the upstream `LICENSE.md` for the full text.
