# Fixture provenance

Public repository: nothing here is secret or identifying.

- `real-reforged-customkeys.txt`: the author's own Reforged `CustomKeys.txt` (25,790 bytes, LF).
- `real-war3preferences.txt`: the author's own `War3Preferences.txt` (Classic mode), unmodified except for one hand-added `[Command Grid Hotkeys 00]` section at the top (this machine has none). The identifying values (`userbnet`, `userlocal`, `offlineavatar`, `bnetGateway`) are empty in the source and were checked.
- `launcher-generated-customkeys.txt`: output of launcher-e `hotkeyService.patchModifiedHotkeys([], hotkeys, allCommands)` over its `defaultHotkeyData` (button positions were assigned deterministically to some entries to exercise `Buttonpos` formatting).
- `jcfieldsdev-grid-en-customkeys.txt`, `jcfieldsdev-qweaz-en-customkeys.txt`: `www/hotkeys/grid-en.txt` and `www/hotkeys/qweaz-en.txt` from https://github.com/jcfieldsdev/warcraft3-hotkey-editor (author jcfieldsdev), licensed MIT. The upstream `LICENSE.md` carries the standard MIT text without a separate copyright line; the files are unmodified copies. They are large (72,149 and 284,727 bytes, mostly tooltips) and exist to prove the size cap and key list accept real shared packs.

The following upstream licence (`LICENSE.md` of jcfieldsdev/warcraft3-hotkey-editor, verbatim) applies to the two jcfieldsdev files:

> # The MIT License (MIT)
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of
> this software and associated documentation files (the "Software"), to deal in
> the Software without restriction, including without limitation the rights to
> use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
> the Software, and to permit persons to whom the Software is furnished to do so,
> subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
> FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
> IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
> CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
