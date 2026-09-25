# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Poker Client (Unity)

Клиент покерной игры (Texas Hold'em). Unity **6000.5.5f1**, URP 2D, uGUI, Input System, TextMeshPro.
Сервер (Python/FastAPI, NLHE, 6 мест) лежит в этом же репо в `game/` — см. ниже.

## Структура
- `Assets/PokerClient/Scripts/` — весь C# код (4 файла, по одному на слой архитектуры):
  - `WsTransport.cs` — `ClientWebSocket`, приём в фоне, `ConcurrentQueue` в главный поток.
  - `PokerSession.cs` — `MonoBehaviour`: конечный автомат `ClientState`, диспетчер `switch (t)`,
    реконнект с backoff, `ping`; правит `TableModel`, UI не трогает.
  - `TableModel.cs` — состояние стола: `ApplySnapshot`, `Apply(t, d)`, `ScreenPos(seatNo, mySeat)`.
  - `TableView.cs` — весь UI собирается из кода в `Start()`, читает модель в `Update()`. Спрайты приходят через
    `[SerializeField]` с объекта `Poker` в `SampleScene` (там же висит `PokerSession`; бутстрапа из кода нет).
    Карты рисуются программно (белый скруглённый спрайт генерируется в `MakeRoundedSprite`), экран загрузки —
    фон `Loading/` + кадры шара из `Animations/Balls/`, стол — фон локации 01 + объект стола.
- `Assets/PokerClient/Editor/` — вне asmdef, компилируется в `Assembly-CSharp-Editor`, это нормально:
  `BuildTools.cs` (`BuildAndroid`/`BuildIos`) и `DiscoPokerUiTexturePostprocessor.cs` (импорт PNG под `Art/UI/` как uGUI-спрайты).
- `Assets/PokerClient/Tests/EditMode/` — NUnit-тесты модели.
- `Assets/PokerClient/Art/` — ассеты от пользователя. Не генерировать своё, брать отсюда:
  - `Cards/`, `Chips/`, `Table/`, `Fonts/`, `Audio/` — **пока пустые** (ожидаемые имена — в README там).
  - `UI/` — пришедший арт-пак: `HUD/`, `Map/`, `Loading/`, `Tutorial/`, `Locations/Location01..04`,
    `Animations/Balls/` (кадры диско-шара). **Карт и фишек в нём нет.**
    Файлы не переименовывать; ключи для кода — `id` из `UI/Documentation/ui-assets.json`;
    у многих PNG запечён текст/цифры — список в `UI/Documentation/README.md`, брать `*empty*`/`without_UI` варианты.
- `Assets/PokerClient/Prefabs/` — пусто; префабы — когда будут спрайты карт.
- `Assets/Scenes/SampleScene.unity` — единственная сцена.
- `game/` — сервер: `app/core/` чистое ядро, `app/api/ws.py` эндпоинт `/ws`, `app/protocol/messages.py` схема сообщений.
  **Справочник протокола: `game/docs/PROTOCOL.md`, требования к клиенту: `game/docs/CLIENT_NETWORKING.md`** —
  при любом вопросе «какое поле/какое сообщение» смотреть туда, не угадывать.

## Правила
- Два `asmdef`: `Scripts/PokerClient.asmdef` (весь код) и `Tests/EditMode/PokerClient.Tests.asmdef`.
  Второй нужен Test Runner'у, первый — чтобы тесты видели код. Больше не плодить.
- uGUI + TMP, не UI Toolkit.
- Ссылки на ассеты — через `[SerializeField]`, не `Resources.Load`.
- UI собирается из кода в `TableView.cs`; новый спрайт — поле `[SerializeField]` в `TableView` и ссылка на объекте
  `Poker` в сцене (ссылка на спрайт в YAML: `{fileID: 21300000, guid: <из .meta>, type: 3}`). Префабы — когда придут спрайты карт.
- JSON — Newtonsoft (`JObject`), он уже в проекте транзитивно. `JsonUtility` не подходит: нет словарей и null.
- Спрайты карт: имя `{rank}{suit}` (`As`, `Td`, `2c`, `Kh`), рубашка `back`.
- Не трогать `Library/`, `Temp/`, `*.csproj`, `*.slnx` — генерируются Unity.
- Unity-специфика: `.meta` файлы коммитить, не удалять руками.

## Проверка
EditMode-тесты модели в `Assets/PokerClient/Tests/EditMode/TableModelTests.cs` — через Test Runner в редакторе
или батчем (команды ниже). Один тест: добавить `-testFilter PokerClient.Tests.TableModelTests.Street_ReplacesBoardEntirely`.
Результат — в `t.xml` (`<test-case ... result="Passed">`), лог компиляции — `t.log`.

Сервер: `cd game && make test` (pytest), `make lint` (ruff), `make run` (uvicorn с reload), `make bots BOTS=5 HANDS=20`.

## Архитектура клиента

Клиент **тонкий**: правил покера не знает, банк не считает, чужих карт не
получает. Сервер — единственный источник правды.

Четыре независимых слоя (смешивать нельзя):

1. **Транспорт** — только WebSocket: текстовые фреймы, UTF-8, JSON. Приём в фоновом потоке.
2. **Очередь в главный поток** — обязательна: сообщения приходят в фоне, `GameObject` трогать можно только в `Update`.
3. **Диспетчер** — разбирает конверт `{"t", "d", "rid"}`. Неизвестный `t` и лишние поля в `d` — не ошибка, логировать и игнорировать.
4. **Модель стола** — единственное место состояния. UI читает только модель; обработчики правят модель, а не UI напрямую.

Явный конечный автомат состояний: `Disconnected → Connecting → Authorized →
Joining → AtTable`, плюс `Reconnecting`. В `AtTable` переходим только по
`joined_table`, а не по факту отправки `quick_join`.

## Правила, нарушение которых ломает игру

- **Не менять состояние по своему действию.** Нажали «колл» — отправили и ждём; фишки двигаются только по `player_action` от сервера.
- **Кнопки строго по `options`** из `action_required`. Нет `options` — это чужой ход, кнопок быть не должно. Минимальный рейз и «повышать некому» на клиенте не воспроизвести.
- **`amount` — суммарная ставка «до» на улице, а не приращение** (текущая 40, поднять до 120 → `amount: 120`). Для `fold`/`check`/`call`/`all_in` игнорируется.
- **Всегда слать `hand_id`** из `action_required`, иначе опоздавший ход применится к новой раздаче.
- **Все суммы — целые фишки.** Никаких `float` для банка и стеков.
- **Локальные часы не использовать.** Поправку часов брать из `server_time` (`welcome`, `pong`), остаток времени = `deadline_ts − (локальное + поправка)`. Таймер на клиенте — только индикация; фолд по своему таймеру не отправлять, это гонка.
- **`board` из `street` рисовать целиком**, а не накапливать по карте.
- **Рейт-лимит 20 сообщений/с.** Ход отправлять один раз и блокировать кнопки; `ping` — раз в 10–15 с, не из `Update`.
- **Любое расхождение лечится `request_snapshot`**, а не ручной починкой UI. Так же реагировать на `invalid_action` / `not_your_turn`.
- **`seat_no` (0–5) ≠ экранная позиция.** Своё место всегда снизу; нужна отдельная функция пересчёта.

## Подключение и токен

`POST /auth/guest {"nickname"}` → `token`, `player_id`, `balance`. Токен живёт 30
суток и является ключом от аккаунта — сохранять на устройстве. Для продления той
же личности слать прошлый токен; `player_id` слать нельзя, он публичен.

Дальше `ws://host:8000/ws?token=<token>` (или коннект без параметра и первым
сообщением `auth`). Сессия установлена только после `welcome`; при реконнекте
следом сам придёт `table_snapshot` — запрашивать ничего не нужно.

Коды закрытия: `4401` — токен протух, получать новый, не переподключаться;
`4001` — вход с другого устройства, показать сообщение и остановиться, не
переподключаться. Обычный обрыв — реконнект с экспоненциальной задержкой
(1, 2, 4, 8 с, потолок 15) **со случайным разбросом** и без сброса модели стола;
окно удержания места — 120 с.

## Порядок работ

Каждый шаг проверяется на живом сервере с ботами перед следующим: транспорт и
очередь → DTO и диспетчер → модель стола и `quick_join` → отрисовка раздачи без
ходов → свой ход → реконнект → краевые случаи (сит-аут, докупка, выход, `4001`).

## Платформы и инструменты

Цель — Android и iOS, ландшафт (арт 16:9). Шаблон уже мобильный: IL2CPP, ARM64,
minSdk 26, iOS 15. Bundle id `com.discopoker.client` — заглушка, сменить перед релизом.
Тулчейны стоят: Android-модуль (SDK/NDK/JDK) и Xcode 27 (iOS SDK 27).

- Плагин `unity@claude-plugins-official` (скиллы `/unity:ui-ugui`, `/unity:unity-cli`,
  `/unity:sprite-editor`, `/unity:manage-sprite-atlas` и др.).
- Unity CLI: `~/.unity/bin/unity` (`unity test`/`unity build` в beta.8 зависают на старте —
  батч-режим запускать напрямую, только при закрытом редакторе):
  ```
  U=/Applications/Unity/Hub/Editor/6000.5.5f1/Unity.app/Contents/MacOS/Unity
  $U -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults t.xml -logFile t.log
  $U -batchmode -nographics -projectPath "$PWD" -buildTarget Android -executeMethod PokerClient.Editor.BuildTools.BuildAndroid -quit -logFile a.log
  $U -batchmode -nographics -projectPath "$PWD" -buildTarget iOS -executeMethod PokerClient.Editor.BuildTools.BuildIos -quit -logFile i.log
  ```
  Выход: `Builds/android/DiscoPoker.apk`, `Builds/ios/` (Xcode-проект, дальше Xcode → устройство).
- iOS-симулятор (подпись не нужна, но это отдельная сборка Unity под Simulator SDK):
  ```
  $U -batchmode -nographics -projectPath "$PWD" -buildTarget iOS -executeMethod PokerClient.Editor.BuildTools.BuildIosSimulator -quit -logFile i.log
  cd Builds/ios-sim && xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release -sdk iphonesimulator \
    -destination 'generic/platform=iOS Simulator' -derivedDataPath ./DerivedData ARCHS=arm64 ONLY_ACTIVE_ARCH=YES \
    CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO build
  xcrun simctl boot <udid>; xcrun simctl install <udid> DerivedData/Build/Products/Release-iphonesimulator/DiscoPoker.app
  xcrun simctl launch <udid> com.discopoker.client; xcrun simctl io <udid> screenshot s.png
  ```
  `xcrun simctl list devices` — udid'ы (iPhone 17 = `AA18E1B6-…`). В Xcode 27 нет Simulator.app: окно симулятора —
  **Device Hub** (Xcode → Open Developer Tool → Device Hub, или `open /Applications/Xcode.app/Contents/Applications/DeviceHub.app`).
  Из терминала тапать нельзя (только `simctl` и скриншоты `simctl io <udid> screenshot`), поэтому чтобы дойти до стола без тапов:
  `SIMCTL_CHILD_POKER_AUTOJOIN=1 xcrun simctl launch --console <udid> com.discopoker.client > log.txt` — клиент сам
  подключается к сохранённому хосту и делает `quick_join`; `--console` отдаёт Unity-лог (в `log show` он не попадает).
  Аргументы командной строки на iOS в C# не приходят (`GetCommandLineArgs()` пустой), поэтому переменная окружения. `Plugins/iOS/AppUISimulatorStub.mm` — заглушка `NativeAppUI_*` под `TARGET_OS_SIMULATOR`:
  библиотека AppUI (тянется через `com.unity.ai.assistant`) собрана только под устройство, а зовётся при старте плеера.
  `iOSSimulatorArchitecture: 1` (arm64) в ProjectSettings — иначе Unity кладёт x86_64-рантайм.
- Сервер для проверки: `game/scripts/run_local.sh` (сам создаст `.venv`, Postgres/Redis не нужны — работает в памяти),
  боты `python3 game/scripts/bot_client.py --bots 5 --hands 50 --stake low` (5, а не 6 — чтобы осталось место клиенту).
  `curl localhost:8000/health` — жив ли; `http://localhost:8000/docs` — Swagger.
  Телефон подключается по LAN-адресу мака (`ipconfig getifaddr en0`), порт 8000, поле «Сервер» в лобби.
- Unity MCP: relay зарегистрирован в Claude Code как `unity-mcp` (user scope).
  Первое подключение подтверждать в Editor → Project Settings → AI → Unity MCP Server.

## Состояние на 2026-09-19 вечер (для продолжения в новой сессии)

Всё ниже закоммичено: `07399ae` на `main`, рабочее дерево чистое. Сервер и боты остановлены.

**Сделано и проверено против локального сервера с ботами** (Android-эмулятор Pixel_9a и iOS-симулятор iPhone 17):
подключение, `quick_join`, отрисовка раздачи (места, борд, банк, ставки, дилер, вскрытие, победители,
таймер по серверному времени), свой ход по `options` (фолд/чек/колл/бет/рейз со слайдером/олл-ин),
реконнект по `table_snapshot`. 5/5 EditMode-тестов модели.

**Сегодня:**
- Арт-пак разложен: всё в `Art/UI/` (HUD, Map, Loading, Tutorial, Locations/01..04, Animations/Balls,
  Documentation). Карт, фишек, фона стола, шрифтов и звуков в паке **нет**. Остатки чужого Unity-проекта
  удалены, архив `DiscoPocker_ForProgramming.rar` (182 МБ, не в git) вынесен в корень проекта.
- Новый дизайн клиента: экран загрузки (фон `Loading/`, неоновая панель, шар из 10 кадров на 12 fps),
  лобби на том же фоне, стол — фон локации 01 с затемнением + объект стола с креслами, карты рисуются
  кодом (белый скруглённый спрайт, масти из динамического fallback-шрифта). Палитра ночная фиолетовая.
- `PokerSession` и `TableView` теперь живут на объекте `Poker` в `SampleScene`, спрайты через `[SerializeField]`,
  бутстрап из кода убран.
- Android: `Builds/android/DiscoPoker.apk` (9-я сборка), проверено на эмуляторе.
- iOS-симулятор: `BuildIosSimulator` → `Builds/ios-sim` → `xcodebuild` (см. «Платформы»), запущено и проверено
  на iPhone 17 до раздачи с ходом. Окно симулятора в Xcode 27 — **Device Hub**. Тестовый хук
  `POKER_AUTOJOIN=1` (переменная окружения, `SIMCTL_CHILD_` префикс) — автоподключение и `quick_join`.
- На устройство iOS по-прежнему не ставилось (нет Apple ID/сертификата).

**Грабли, которые уже съедены (не повторять):**
- `ClientWebSocket.ConnectAsync` в Mono игнорирует CancellationToken — таймаут только через
  `Task.WhenAny` + `Abort()` (WsTransport). Без этого реконнект виснет навсегда.
- CanvasScaler match 0.5: на 2424×1080 высота холста ~960 единиц, низ верстать от нижнего края,
  верхнее место не выше y=300 (иначе рубашки карт над ним режутся краем).
- ♠♥♦♣ нет в статическом атласе `LiberationSans SDF`, но есть в TTF и подключённом динамическом fallback:
  `HasCharacter(c, searchFallbacks: true, tryAddCharacter: true)`, иначе масти рисуются буквами.
- Эмулятор ходит на мак по LAN-IP (192.168.0.43) или `10.0.2.2`; свободное место на /data
  AVD было забито, помог `-wipe-data` (config.ini: `disk.dataPartition.size=8G`).
- `adb shell input keyevent` в лобби может нажать кнопку. Боты сливают стеки за 5–10 раздач и уходят
  в сит-аут; наш игрок после 3 автофолдов — тоже (сервер помнит токен). Для чистого теста перезапускать
  сервер + ботов (`pkill -f bot_client.py; pkill -f "uvicorn app.main"`), в памяти балансы обнуляются.
- Логи: Android — `adb logcat -d | grep -E "←|→"`; iOS — только `simctl launch --console`, в `log show`
  Unity-лог не попадает. Аргументы командной строки на iOS в C# пустые.
- iOS-симулятор: `iOSSimulatorArchitecture` должен быть 1 (arm64), иначе Unity кладёт x86_64-рантайм;
  AppUI (через `com.unity.ai.assistant`) зовёт нативный `NativeAppUI_*` при старте плеера, для симулятора
  его закрывает `Plugins/iOS/AppUISimulatorStub.mm`. Без стаба — чёрный экран и зависший крэш-репортер.
- Экран загрузки на локальном сервере мелькает за доли секунды: проверять на хосте, который молча
  роняет пакеты (например `8.8.8.8:8000`), а не на недоступном (тот отваливается мгновенно).
- На iPhone 17 слева вырез камеры перекрывает край; панели в него не попадают, но safe area учитывать.

**Бэклог, по важности:**
1. Кнопка «Вернуться» (`sit_in`), когда `you.sitting_out`/`seat_update` сит-аут — сейчас
   после 3 автофолдов игрок застревает в сит-ауте без выхода. Рядом — докупка (`rebuy`).
2. Показ ошибок сервера (`error`) заметнее и обработка `4001` с экраном «вход с другого устройства».
3. Спрайты карт/фишек, когда придут в `Art/Cards`, `Art/Chips`; префабы вместо UI из кода.
   Кнопки Fold/Call/Raise и панели стола от художника тоже пока не пришли.
4. Выбор уровня ставок в лобби (сейчас всегда `low`), кнопка «Отключиться» за столом.
5. Анимации раздачи (`cards_dealt`) и движения фишек — отдельная очередь от сетевой.
6. iOS-устройство: добавить Apple ID в Xcode, Team ID в Player Settings, прогнать на iPhone (спарен).
   Safe area под вырез.
7. Сервер: при одновременном `quick_join` пяти ботов один уехал на второй стол (гонка в
   registry) — не клиент, но мешает тестам.
