-- Marks the PostgreSQL baseline EF migration as applied (the SQL Server migration history rows were copied as-is).
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261004041822_PostgresBaseline', '8.0.11')
ON CONFLICT DO NOTHING;
