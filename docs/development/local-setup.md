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

### Configurar variáveis locais do Docker

O arquivo `.env` é utilizado apenas para configurações locais do Docker Compose e não deve ser versionado.

Crie o arquivo local a partir do exemplo:

```powershell
Copy-Item .env.example .env
```

Depois, defina uma senha local no arquivo `.env`:

```text
POSTGRES_PASSWORD=YOUR_LOCAL_POSTGRES_PASSWORD
```

Substitua `YOUR_LOCAL_POSTGRES_PASSWORD` por uma senha utilizada apenas no ambiente local de desenvolvimento.

O arquivo `.env` está protegido pelo `.gitignore` e não deve ser adicionado ao Git.

### Iniciar o PostgreSQL

Execute:

```powershell
docker compose up -d postgres
```

Verifique se o container está saudável:

```powershell
docker compose ps
```

São utilizados bancos separados:

- `virtual_employee` — desenvolvimento
- `virtual_employee_tests` — testes de integração

Os testes de integração não devem utilizar o banco de desenvolvimento.

## Connection string de desenvolvimento

Credenciais não devem ser armazenadas em `appsettings.Development.json`.

A aplicação utiliza .NET User Secrets para armazenar a connection string do ambiente local.

Configure utilizando:

```powershell
# Substitua YOUR_CONNECTION_STRING pelo valor do seu ambiente.
dotnet user-secrets set `
  "ConnectionStrings:Database" `
  "YOUR_CONNECTION_STRING" `
  --project .\src\VirtualEmployee.Api
```

O valor configurado através de User Secrets permanece fora do repositório Git.

## Banco de testes

Caso o banco de testes ainda não exista, crie utilizando:

```powershell
docker exec -it virtual-employee-postgres `
  psql -U postgres -d postgres `
  -c "CREATE DATABASE virtual_employee_tests;"
```

Configure a connection string dos testes através de uma variável de ambiente na sessão do terminal:

```powershell
# Substitua YOUR_TEST_CONNECTION_STRING pelo valor do seu ambiente.
$env:TEST_DATABASE_CONNECTION_STRING="YOUR_TEST_CONNECTION_STRING"
```

A variável existe somente na sessão atual do PowerShell.

Nenhuma credencial deve ser armazenada diretamente no código dos testes.

## Entity Framework Core

### Criar migration

```powershell
dotnet ef migrations add <MigrationName> `
  --project .\src\VirtualEmployee.Infrastructure `
  --startup-project .\src\VirtualEmployee.Api `
  --context AppDbContext `
  --output-dir Persistence\Migrations
```

### Aplicar migrations

```powershell
dotnet ef database update `
  --project .\src\VirtualEmployee.Infrastructure `
  --startup-project .\src\VirtualEmployee.Api `
  --context AppDbContext
```

Os testes de integração utilizam migrations reais do EF Core em vez de `EnsureCreated()`.

## Build

Para compilar toda a solution:

```powershell
dotnet build VirtualEmployee.slnx
```

## Testes de integração

Para executar somente os testes de integração:

```powershell
dotnet test .\tests\VirtualEmployee.IntegrationTests\VirtualEmployee.IntegrationTests.csproj
```

Os testes de integração utilizam o banco `virtual_employee_tests`.

A variável `TEST_DATABASE_CONNECTION_STRING` deve estar configurada antes da execução.

## Todos os testes

Para executar todos os projetos de teste da solution:

```powershell
dotnet test VirtualEmployee.slnx
```

## Segurança

Nunca versionar:

- credenciais;
- connection strings contendo senhas;
- tokens;
- API keys;
- secrets de provedores externos;
- arquivos `.env` locais;
- certificados ou chaves privadas.

Secrets devem ser fornecidos através do mecanismo apropriado para cada ambiente.

Para desenvolvimento local:

- Docker Compose utiliza `.env`;
- a aplicação .NET utiliza User Secrets;
- testes de integração utilizam `TEST_DATABASE_CONNECTION_STRING`.

O arquivo `.env.example` documenta apenas as variáveis necessárias e não deve conter credenciais reais.