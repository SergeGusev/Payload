## 2026-05-17T12:34:48.0836956Z
Request:
А что за Docker image мы сделали и зачем он нужен?

Result:
Explained that the repository does not contain a custom PolyCopyTrader Docker image. The persistent Docker setup is `docker-compose.local.yml`, which uses `postgres:16-alpine` as a fallback local PostgreSQL container on `127.0.0.1:54328`; separately, `postgres:18-alpine` was used temporarily as PostgreSQL client tooling for database copy/restore when local `pg_dump`/`pg_restore` were unavailable.
