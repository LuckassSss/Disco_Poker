# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Poker Client (Unity)

Клиент покерной игры (Texas Hold'em). Unity **6000.5.5f1**, URP 2D, uGUI, Input System, TextMeshPro.
Сервер (Python/FastAPI, NLHE, 5 мест) — отдельный репозиторий, локально лежит рядом: `../back/` (свой `CLAUDE.md`).

## Структура
- `Assets/PokerClient/Scripts/` — весь C# код (4 файла, по одному на слой архитектуры):
  - `WsTransport.cs` — `ClientWebSocket`, приём в фоне, `ConcurrentQueue` в главный поток.
  - `PokerSession.cs` — `MonoBehaviour`: конечный автомат `ClientState`, диспетчер `switch (t)`,
    реконнект с backoff, `ping`; правит `TableModel`, UI не трогает.
  - `TableModel.cs` — состояние стола: `ApplySnapshot`, `Apply(t, d)`, `ScreenPos(seatNo, mySeat)`.
  - `TableView.cs` — весь UI собирается из кода в `Start()`, читает модель в `Update()`. Спрайты приходят через
    `[SerializeField]` с объекта `Poker` в `SampleScene` (там же висит `PokerSession`; бутстрапа из кода нет).
    Карты рисуются программно (белый скруглённый спрайт генерируется в `MakeRoundedSprite`), экран загрузки —
    фон `Loading/` + кадры шара из `Animations/Balls/`, стол — рендер `Locations/Location01/Tables/table_1` по высоте экрана,
    места игроков — якоря в долях картинки (`SeatUV`). Лобби: верхняя панель по макету `Locations/Location01/Backgrounds/W UI.png`
    (спрайты из `HUD/`), карта локации — фон 20:9 `Backgrounds/location-*-mobile-20x9` (прежний с полями по бокам) на весь экран (`EnvelopeParent`),
    столы — прямоугольники в долях фона (`TableRects`). `MakeImage` ставит `raycastTarget=false`; кнопка из спрайта — `SpriteButton`.
- `Assets/PokerClient/Editor/` — вне asmdef, компилируется в `Assembly-CSharp-Editor`, это нормально:
  `BuildTools.cs` (`BuildAndroid`/`BuildIos`/`BuildIosSimulator`, меню `Poker/…`) и `DiscoPokerUiTexturePostprocessor.cs` (импорт PNG под `Art/UI/` как uGUI-спрайты).
- `Assets/PokerClient/Tests/EditMode/` — NUnit-тесты модели.
- `Assets/PokerClient/Art/` — ассеты от пользователя. Не генерировать своё, брать отсюда:
  - `Cards/`, `Chips/`, `Table/`, `Fonts/`, `Audio/` — **пока пустые** (ожидаемые имена — в README там).
  - `UI/` — пришедший арт-пак: `HUD/`, `Map/`, `Loading/`, `Tutorial/`, `Locations/Location01..04`,
    `Animations/Balls/` (кадры диско-шара). **Карт и фишек в нём нет.**
    Файлы не переименовывать; ключи для кода — `id` из `UI/Documentation/ui-assets.json`;
    у многих PNG запечён текст/цифры — список в `UI/Documentation/README.md`, брать `*empty*`/`without_UI` варианты.
    `UI/Locations/Location01..03/Tables/` — рендеры покерных столов (вид сверху, 1672×941, 5 кресел = 5-max на сервере), выгружены
    из Figma (`figma.com/design/hULeSpuZvTsyODA1jnZWJd`, мудборд без фреймов; локация 04 и остальное — там же).
- `Assets/PokerClient/Prefabs/` — пусто; префабы — когда будут спрайты карт.
- `Assets/Scenes/SampleScene.unity` — единственная сцена.

**Справочник протокола: `../back/docs/PROTOCOL.md`, требования к клиенту: `../back/docs/CLIENT_NETWORKING.md`,
устройство сервера: `../back/docs/ARCHITECTURE.md`, ТЗ: `../back/docs/SPECIFICATION.md`** — при любом вопросе
«какое поле/какое сообщение» смотреть туда, не угадывать. Правка протокола — коммиты в оба репозитория.

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

Сервер для проверок клиента — см. «Платформы и инструменты» ниже; его тесты и команды — в `../back/CLAUDE.md`.
Запускать с `GUEST_START_BALANCE=1000000`: по умолчанию 10 000 = бай-ину, докупиться не на что.

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
- **`seat_no` (0–4) ≠ экранная позиция.** Своё место всегда снизу; нужна отдельная функция пересчёта.

## Подключение и токен

`POST /auth/register|login {"login","password"[,"nickname"]}` → `token`, `player_id`, `balance`.
Токен аккаунта живёт 3 дня; при каждом запуске клиент шлёт его в `POST /auth/refresh` и получает
новый (`PokerSession.Resume`), `401` → форма входа. Каждая выдача отзывает прежние токены игрока
(старый токен → 401 / `4401`). `POST /auth/guest {"nickname"}` (30 суток, продление старым токеном) остался для ботов
и `POKER_AUTOJOIN`. `player_id` слать нельзя, он публичен.

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
  Меню редактора `Poker/…` — те же сборки плюс `Import TMP Essentials`.
- Android-эмулятор (`adb` не в PATH):
  ```
  ADB=/Applications/Unity/Hub/Editor/6000.5.5f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
  ~/Library/Android/sdk/emulator/emulator -avd Pixel_9a &
  $ADB install -r Builds/android/DiscoPoker.apk && $ADB shell am start -n com.discopoker.client/com.unity3d.player.UnityPlayerGameActivity
  $ADB logcat -d | grep -E "←|→"      # лог сообщений клиента
  ```
  Поля «Сервер» в UI нет: хост по умолчанию зашит в `PokerSession.Awake` — эмулятор (`deviceModel` содержит `sdk`)
  идёт на `10.0.2.2:8000`, телефон — на LAN-адрес мака (`ipconfig getifaddr en0`, меняется с сетью — править в коде).
  Значение сохраняется в `PlayerPrefs`: после смены адреса делать `$ADB shell pm clear com.discopoker.client`.
  Запуск без случайных событий: `$ADB shell am start -n com.discopoker.client/com.unity3d.player.UnityPlayerGameActivity`.
  На Android/в редакторе автоподключение — аргумент `-autojoin`.
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
  Аргументы командной строки на iOS в C# не приходят (`GetCommandLineArgs()` пустой), поэтому переменная окружения. `Assets/PokerClient/Plugins/iOS/AppUISimulatorStub.mm` — заглушка `NativeAppUI_*` под `TARGET_OS_SIMULATOR`:
  библиотека AppUI (тянется через `com.unity.ai.assistant`) собрана только под устройство, а зовётся при старте плеера.
  `iOSSimulatorArchitecture: 1` (arm64) в ProjectSettings — иначе Unity кладёт x86_64-рантайм.
- Сервер для проверки: `../back/scripts/run_local.sh` (сам создаст `.venv`, Postgres/Redis не нужны — работает в памяти),
  боты `python3 ../back/scripts/bot_client.py --bots 4 --hands 50 --stake low` (4, а не 5 — чтобы осталось место клиенту).
  `curl localhost:8000/health` — жив ли; `http://localhost:8000/docs` — Swagger.
- Unity MCP: relay зарегистрирован в Claude Code как `unity-mcp` (user scope).
  Первое подключение подтверждать в Editor → Project Settings → AI → Unity MCP Server.

## Прогресс

Где остановились, что проверено, съеденные грабли и бэклог — в `PROGRESS.md`. Читать первым в новой сессии,
обновлять в конце сессии (вместе с коммитом).
