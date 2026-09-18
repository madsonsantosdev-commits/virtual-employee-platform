# Local Development Setup

Este documento descreve a configuração do ambiente local para desenvolvimento e testes da Virtual Employee Platform.

## Pré-requisitos

- .NET SDK 10.0.401
- Docker Desktop com WSL 2
- PostgreSQL 18 via Docker
- Git

A versão do SDK .NET é definida pelo arquivo `global.json`.

## PostgreSQL

O PostgreSQL local é executado através do `docker-compose.yml`.

```powershell
docker compose up -d postgres
```

Verifique se o container está saudável:

```powershell
docker ps
```

São utilizados bancos separados:

- `virtual_employee` — desenvolvimento
- `virtual_employee_tests` — testes de integração

Os testes de integração não devem utilizar o banco de desenvolvimento.

## Connection string de desenvolvimento

Credenciais não devem ser armazenadas em `appsettings.Development.json`.

Utilize .NET User Secrets:

```powershell
# Substitua YOUR_CONNECTION_STRING pelo valor do seu ambiente.
dotnet user-secrets set `
  "ConnectionStrings:Database" `
  "YOUR_CONNECTION_STRING" `
  --project .\src\VirtualEmployee.Api
```

## Banco de testes

Caso ainda não exista:

```powershell
docker exec -it virtual-employee-postgres `
  psql -U postgres -d postgres `
  -c "CREATE DATABASE virtual_employee_tests;"
```

Configure a connection string dos testes na sessão do terminal:

```powershell
# Substitua YOUR_TEST_CONNECTION_STRING pelo valor do seu ambiente.
$env:TEST_DATABASE_CONNECTION_STRING="YOUR_TEST_CONNECTION_STRING"
```

A variável existe somente na sessão atual do PowerShell.

Nenhuma credencial deve ser armazenada no código dos testes.

## Entity Framework Core

Criar migration:

```powershell
dotnet ef migrations add <MigrationName> `
  --project .\src\VirtualEmployee.Infrastructure `
  --startup-project .\src\VirtualEmployee.Api `
  --context AppDbContext `
  --output-dir Persistence\Migrations
```

Aplicar migrations:

```powershell
dotnet ef database update `
  --project .\src\VirtualEmployee.Infrastructure `
  --startup-project .\src\VirtualEmployee.Api `
  --context AppDbContext
```

Os testes de integração utilizam migrations reais do EF Core em vez de `EnsureCreated()`.

## Build

```powershell
dotnet build VirtualEmployee.slnx
```

## Testes de integração

```powershell
dotnet test .\tests\VirtualEmployee.IntegrationTests\VirtualEmployee.IntegrationTests.csproj
```

## Todos os testes

```powershell
dotnet test VirtualEmployee.slnx
```

## Segurança

Nunca versionar credenciais, connection strings contendo senhas, tokens, API keys, secrets de provedores externos ou arquivos `.env` locais.

Secrets devem ser fornecidos através do mecanismo apropriado para cada ambiente.