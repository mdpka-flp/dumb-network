# Dumb Network

Кроссплатформенный (Linux / Windows) клиент для [zapret](https://github.com/bol-van/zapret)
на Avalonia UI. Слева — прокручиваемый список готовых стратегий, справа — большая
кнопка включения и журнал.

> Используйте только там, где обход блокировок не нарушает закон и правила вашей сети.

## Что уже работает

- 7 готовых стратегий: Fake TLS + QUIC, Multisplit + SeqOvl, классическая схема
  bolvan, Discord + STUN, только QUIC, HTTP и агрессивная.
- Запуск и остановка `nfqws` (Linux) / `winws` (Windows) с выбранными аргументами.
- Linux: GUI запускается без `sudo`. При первом включении открывается окно ввода
  пароля администратора; root-права получает только отдельный helper-процесс.
- Linux: автоматическая настройка nftables (`table inet dumbzapret`, NFQUEUE 200)
  с откатом на iptables (`chain DUMBZAPRET`).
- Windows: `winws` + WinDivert, UAC-манифест уже подключён.
- Журнал процесса, корректное снятие правил firewall при выходе.
- Тесты, включая проверку каждой Linux-стратегии реальным `nfqws --dry-run`.

## Требования

- .NET 10 SDK.
- Linux: `sudo` и пользователь, которому разрешено через него запускать приложение;
  `nft` или `iptables`; ядро с NFQUEUE.
- Windows 10/11 x64: права администратора (запрашиваются автоматически).
  Windows ARM64 не поддерживается — upstream не поставляет подписанный WinDivert.

## Запуск

```bash
dotnet build DumbNetwork.slnx
dotnet run --project src/DumbNetwork
```

На Linux приложение запускается обычным пользователем — `sudo` для GUI не нужен.
Нажмите кнопку питания: откроется окно «Права администратора», введите пароль
своего пользователя (тот же, что запрашивает `sudo`). После успешной проверки
helper остаётся запущенным до закрытия приложения, поэтому пароль спрашивается
один раз за сеанс.

- Пароль передаётся только процессу `sudo` через stdin, не сохраняется и не
  попадает в журнал.
- Если приложение уже запущено от root, окно не появляется — используется
  локальный запуск без helper.
- Если в sudoers настроен `NOPASSWD`, helper запустится без проверки пароля.
  Диалог при этом всё равно откроется на первом включении; введённое значение
  не используется.
- Пользователь должен иметь право запускать приложение через `sudo`. Если sudo
  недоступен или запрещён, приложение покажет причину в журнале и в статусе.

Windows: соберите и запустите `.exe` — UAC появится автоматически. На Windows
используется системное окно UAC, а не встроенный диалог пароля.

## Бэкенд: что скачано и куда

Взят официальный релиз **v72.13** (tar.gz) и распакован только нужный минимум:

| Файл из релиза | Куда положен |
| --- | --- |
| `binaries/linux-x86_64/nfqws` | `backend/zapret/v72.13/linux-x64/nfqws` |
| `binaries/linux-arm64/nfqws` | `backend/zapret/v72.13/linux-arm64/nfqws` |
| `binaries/windows-x86_64/winws.exe` | `backend/zapret/v72.13/windows-x64/winws.exe` |
| `binaries/windows-x86_64/WinDivert.dll` | `backend/zapret/v72.13/windows-x64/WinDivert.dll` |
| `binaries/windows-x86_64/WinDivert64.sys` | `backend/zapret/v72.13/windows-x64/WinDivert64.sys` |
| `binaries/windows-x86_64/cygwin1.dll` | `backend/zapret/v72.13/windows-x64/cygwin1.dll` |
| `files/fake/*.bin` | `backend/zapret/v72.13/files/fake/` |
| `docs/LICENSE.txt` | `backend/zapret/v72.13/LICENSE.txt` |

Скрипт `scripts/fetch-zapret.sh` скачивает релиз, сверяет SHA-256 и раскладывает
файлы. Обновление версии:

```bash
ZAPRET_VERSION=v72.14 ./scripts/fetch-zapret.sh
```

Остальное из архива (init-скрипты, blockcheck, tpws, ip2net, mdig, исходники,
другие архитектуры) клиенту не нужно. Подробности — в `backend/zapret/README.md`.

## Структура

```
backend/zapret/v72.13/        # бинарники и fake-пейлоады (копируются в output)
scripts/fetch-zapret.sh       # обновление бэкенда
src/DumbNetwork/
├── Strategies/               # каталог готовых стратегий
├── Services/
│   ├── BackendLayout.cs      # поиск бэкенда и версии
│   ├── ArgumentBuilder.cs    # аргументы nfqws/winws
│   ├── ZapretRunner.cs       # локальный процесс + состояние
│   ├── IZapretController.cs  # общий интерфейс запуска
│   ├── ZapretControllerFactory.cs
│   ├── PrivilegedHelperClient.cs  # GUI-сторона sudo/helper
│   ├── PrivilegedHelper.cs        # root-сторона helper
│   ├── HelperProtocol.cs          # JSON-lines протокол
│   ├── SudoDiagnostics.cs         # разбор ошибок sudo
│   └── Firewall/             # nftables / iptables
├── ViewModels/               # MVVM (CommunityToolkit)
└── Views/
    ├── MainWindow.axaml      # основной интерфейс
    └── PasswordDialog.axaml  # окно ввода пароля
tests/DumbNetwork.Tests/      # юнит-тесты + nfqws --dry-run
```

## Как это устроено

1. `StrategyCatalog` хранит аргументы `nfqws`/`winws` и список портов.
   Токен `{fake:file.bin}` заменяется на абсолютный путь к `files/fake`.
2. На Linux без root GUI использует `PrivilegedHelperClient`. При первом включении
   он запускает этот же исполняемый файл с аргументом `--privileged-helper` через
   `sudo -k -S` и передаёт пароль в stdin. Sudo пишет приглашение в stderr; клиент
   распознаёт его, отправляет одну строку с паролем и дальше общается с helper по
   JSON-lines через stdin/stdout.
3. Helper принимает только `start` с известным ID стратегии, `stop` и `shutdown`.
   Он не исполняет произвольные команды или аргументы, поэтому канал нельзя
   использовать как универсальный root-shell. Helper запускает
   `nfqws --qnum=200 --dpi-desync-fwmark=0x40000000`, затем `NftFirewall` создаёт
   отдельную таблицу и ставит порты в NFQUEUE. Пакеты с меткой `0x40000000`
   (сгенерированные самим nfqws) не зацикливаются.
4. Windows: `winws` сам фильтрует трафик через WinDivert; внешний firewall не нужен.
   Приложение запускается с UAC-манифестом.
5. Остановка: сначала снимаются правила firewall, затем завершается процесс.
   При закрытии окна это делается автоматически. Если GUI завершился, helper
   получает EOF на stdin и тоже снимает правила и останавливает nfqws.

## Ограничения текущей версии

- Нет hostlist/ipset и авто-подбора стратегий — выбранная стратегия применяется
  ко всему трафику на указанных портах.
- Одновременно активна только одна стратегия.
- На Linux повышение прав идёт через `sudo` и встроенный диалог пароля; polkit,
  `pkexec` и setuid-helper не используются. Если sudo запрещён для приложения,
  запуск невозможен — используйте локальный запуск от root или настройте sudoers.
- Переменная `DUMBZAPRET_BACKEND`, заданная для GUI, не передаётся helper-процессу
  через sudo. Helper ищет бандл рядом с приложением и в родительских каталогах
  рабочей директории; для нестандартного пути запускайте приложение из каталога,
  где бандл доступен, либо запускайте GUI от root с этой переменной.
- Если на интерфейсе включён hardware flow offload, NFQUEUE может не видеть трафик;
  для проверки отключите offload на внешнем интерфейсе.
- Windows: только x64; Linux: x64 и arm64 (остальные архитектуры можно добавить
  в `BackendLayout` и бандл).

## Тесты

```bash
dotnet test DumbNetwork.slnx
```

Тесты каталога стратегий, сборки аргументов, nft-скрипта, обработки ошибок sudo,
сценария повторного ввода пароля и реальный `nfqws --dry-run` для всех
Linux-стратегий из бандла. Настоящий `sudo` в автоматических тестах не
используется: для него нужен пароль пользователя.

Опциональный интеграционный тест реально ставит nftables-правила и запускает
процесс-заглушку вместо nfqws (безопасно в отдельном netns):

```bash
unshare -Urn sh -c 'ip link set lo up; DUMBZAPRET_INTEGRATION=1 \
  dotnet test tests/DumbNetwork.Tests/DumbNetwork.Tests.csproj --filter LinuxIntegrationTests'
```

Настоящий `nfqws` в unprivileged user namespace не стартует (вызов `setgroups`
при сбросе root), поэтому для проверки самого nfqws нужен реальный root.
