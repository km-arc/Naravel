# Database

The query builder demo lives at `/database` (see `app/Http/Controllers/DatabaseDemoController.cs`, `app/Data/PeopleSeeder.cs`, `config/DatabaseSampleConfig.cs` and the `Database` section of `config/appsettings.json`). It uses an in-memory SQLite database, so there is nothing to install and nothing is written to disk.

Migrations, factories and seeders in the Laravel sense are not part of Naravel: use EF Core for entities and migrations. The sample creates its one table with raw SQL through `IDatabase` only because the demo has to start from nothing.
