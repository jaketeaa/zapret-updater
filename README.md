# Zapret Updater

[![Build](https://github.com/jaketeaa/zapret-updater/actions/workflows/build.yml/badge.svg)](https://github.com/jaketeaa/zapret-updater/actions)
[![Release](https://img.shields.io/github/v/release/jaketeaa/zapret-updater)](https://github.com/jaketeaa/zapret-updater/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/jaketeaa/zapret-updater/total)](https://github.com/jaketeaa/zapret-updater/releases)
[![License](https://img.shields.io/github/license/jaketeaa/zapret-updater)](LICENSE)

<img width="648" height="328" alt="photo_2026-10-25_21-46-27" src="https://github.com/user-attachments/assets/64ea4d4e-abdc-4f37-bdc8-ee1088c5c9e5" />

Автоматическое обновление [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) на Windows.
Неофициальная утилита, не связана с автором zapret.

**by jaketea**

## Что делает
- Проверяет последнюю версию на GitHub и обновляет файлы, сохраняя пользовательские списки (`lists\*user*.txt`).
- Возвращает прежнюю стратегию. Если службы нет, запускает тесты всех конфигураций и ставит лучшую.
- Настраивает Game Filter (TCP and UDP), обновляет IPSet и hosts (текст из Блокнота переносится в hosts автоматически).
- Сообщает, если вышла новая версия самой программы.
- Выводит цветной лог с временем, результатами тестов (`general (ALT4) = 4/17 сервисов`) и итоговой проверкой службы.

## Использование
1. Скачай `zapret-updater.exe` из раздела [Releases](../../releases).
2. Запусти от имени администратора.

Если Windows пишет «защитила ваш компьютер» (SmartScreen): **Подробнее -> Выполнить в любом случае**. Файл не подписан сертификатом. Либо собери его сам (см. «Сборка»).

### Параметры
| Параметр | Что делает |
|---|---|
| `--force` | обновить, даже если версия актуальна |
| `--test` | всегда запускать тесты и выбрать конфигурацию заново |
| `--fresh` | чистая установка в папку `zapret` на рабочем столе |
| `--dir <путь>` | указать папку zapret |
| `--nowait` | не ждать Enter в конце |
| `--no-selfcheck` | не проверять обновления самой программы |
| `--verbose` | выводить сырой вывод процессов |
| `--help`, `-h` | показать справку |

Лог: `Документы\ZapretUpdater\zapret-updater.log`.

> Внимание: папка zapret очищается целиком (кроме `zapret-updater*`). Системные папки и корни дисков программа очищать отказывается.

## Проверка файла
В релизе рядом с exe лежит файл `zapret-updater.exe.sha256`. Чтобы проверить, что скачанный файл не подменён, выполни:

    certutil -hashfile zapret-updater.exe SHA256

Результат должен совпасть с содержимым `.sha256` из релиза.

## Частые проблемы

**Windows пишет «защитила ваш компьютер» (SmartScreen).**
Файл не подписан. Нажми «Подробнее» -> «Выполнить в любом случае». Либо ПКМ по exe -> Свойства -> «Разблокировать». Либо собери сам через `build.bat`.

**Антивирус ругается.**
Программа запрашивает права администратора, останавливает службы и правит hosts, что похоже на поведение вредоносных программ. Проверь файл на virustotal.com и добавь его в исключения.

**Не удаляется папка zapret или bin.**
Папка используется работающей службой. Останови её командами (от администратора): `sc stop zapret`, `taskkill /F /IM winws.exe`, `sc stop WinDivert`, `sc delete zapret`, `sc delete WinDivert`.

**Окно перестало отвечать.**
Нажми Enter. Подробности ищи в `zapret-updater.log`.

## Сборка
Нужен .NET Framework 4.x (есть в Windows 10/11). Запусти `build.bat`, получишь `zapret-updater.exe`.
Релизы собираются автоматически через GitHub Actions при создании тега `v*`.

## Лицензия
MIT, см. файл [LICENSE](LICENSE).
