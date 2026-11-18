# Инструменты для работы с Unity из Claude

Запускать из этой папки (`cd /d/Metroidvany/tools/unity`), Python — с `-I -X utf8`.

| Файл | Что делает |
|---|---|
| `umcp.py` | Клиент MCP For Unity (HTTP 127.0.0.1:8080). `python -I -X utf8 umcp.py call execute_code '{"action":"execute","code":"return 1;"}'`, `... call read_console '{"action":"get","types":["error"],"count":10,"format":"detailed","include_stacktrace":true}'`. Компилятор сниппетов — codedom: без локальных функций; `AssetDatabase.DeleteAsset` в сниппетах заблокирован. |
| `uidle.sh` | Ждёт, пока Unity не компилирует и не импортирует (3 спокойные проверки подряд). |
| `replay.sh` | Безопасный перезапуск Play: стоп → Refresh → ждать конца компиляции → Play. **Никогда не входить в Play во время компиляции** (уронило Unity 14.11 в 22:39). |
| `rigcc/rt.rsp`, `rigcc/ed.rsp` | Компиляция кода рига вне Unity (Roslyn Unity): `R="C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data"; "$R/NetCoreRuntime/dotnet.exe" "$R/DotNetSdkRoslyn/csc.dll" @rt.rsp` (рантайм) и `@ed.rsp` (редактор). Новые .cs — дописать путь в rsp. Пусто на выходе = собралось. |
| `envrun.py` | Запустить ситуацию стенда по индексу и печатать телеметрию (`probe_env.cs.txt`). |
| `tdump.py`, `tana.py` | Выгрузка/разбор следа `RigTrace` (стопы тела и призрака по шагам физики). |
| `cs.py` | Выполнить C#-сниппет из файла (`python -I -X utf8 cs.py file.cs`, `-` — из stdin): без экранирования JSON в командной строке. |
| `v3run.py` | Риг v3: запустить ситуацию стенда и печатать телеметрию людей v3 (`probe_v3.cs.txt`: скорость, отбрасывание, шаги, качание, пики суставов, удары). `python -I -X utf8 v3run.py 21 20 1.5 [кадры]`. Ситуации v3 — индексы 21–27. |
| `burst.py` | Серия кадров удара в замедлении: `burst.py имя 9 0.2 "r.PushChest(r.Animate.BodyForward * -75f)" fixside 3.6` (`side/front/back`, `fix…` — неподвижная камера). **Грузит редактор (автор видел 3–4 к/с) — только короткие серии, не пока автор смотрит.** |
| `strip.py` | Склеить кропы кадров в полосу через Unity (PIL нет): `strip.py out.png x0 x1 y0 y1 a.png b.png …`. |
| `drive.py` | Вести игрока v3 вводом в мотор (без клавиатуры) и печатать скорость, поворот капсулы и корпуса, наклон. |

Сервер MCP For Unity: если 127.0.0.1:8080 не отвечает —
`cd /d/Metroidvany && "/c/Users/Kirill/AppData/Local/uv/cache/archive-v0/6kQl5TTARUfyXJRs/Scripts/mcp-for-unity.exe" --transport http --http-url http://127.0.0.1:8080 --project-scoped-tools`
(в фоне), затем автор нажимает Connect в Unity: Window → MCP for Unity.

Снимок кадра: `FortressSetup.Shot("D:/Metroidvany/renders/rig/x.png", camPos, lookAt)` (редакторский метод проекта).
Play не останавливается, когда Unity без фокуса, только если `Application.runInBackground = true` (стенд RigTests это ставит сам).
