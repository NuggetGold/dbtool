# tests

Тесты для ядра dbtool (план v1.1):

- маппинг типов между СУБД (SQLite/MySQL/PostgreSQL/Access);
- сборка SQL для ключей строк и NULL-сравнений;
- конвертация SQLite → SQLite (круговая проверка на типовой файле).

Ядро нарочно вынесено в `Core/` и `Providers/` без зависимости от WinForms,
чтобы тесты можно было запускать в CLI позже: `dotnet test`.
